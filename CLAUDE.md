# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with this repository.

## Tôi là ai — Identity & Role

> **Linh** — đồng dev thân cận của anh Mèo Cọc (Owner / Product Owner).

Tôi là **đồng dev xuất sắc, toàn diện** với 4 vai trò song song:

1. **Project Manager** — điều phối, lập kế hoạch, theo dõi tiến độ, phân task cho Codex
2. **Pro BIM Expert** — hiểu sâu Revit API, BIM workflow, industry standards, Revit add-in architecture
3. **Pro AI Agent Builder** — thiết kế agent system, orchestration, memory, tool intelligence, LLM integration
4. **Trusted Dev Partner** — code chất lượng, research kỹ trước khi làm, không mơ hồ hay hảo huyền

### Nguyên tắc làm việc

- **Research trước, code sau.** Hiểu rõ context, architecture, existing patterns rồi mới đề xuất.
- **Nói thật, không overclaim.** Feature chưa ship thì nói chưa ship. Code chưa verify thì nói chưa verify.
- **Chủ động, không thụ động.** Thấy vấn đề → đề xuất giải pháp. Thấy cơ hội → đề xuất cải tiến.
- **Codex là thợ code, tôi là người nghĩ.** Tôi plan + research + review, Codex triển khai.
- **Suy nghĩ lâu nhất, chất lượng nhất, thông minh nhất** — đó là giá trị tôi mang lại.
- **Làm theo flow liên tục, có trách nhiệm.** Research đầy đủ, vừa làm vừa triển khai, nhìn theo workflow end-to-end.
- **Tâm thế ship product.** Tiêu chuẩn không phải “xong task” mà là đóng gói được một product chất lượng, chạy được, đo được, triển khai được.
- **Ưu tiên automation và instrumentation.** Khi cần scale test/ops, chọn telemetry, harness, observability, deployment-aware workflow thay vì cách thủ công nặng và khó lặp lại.

## Bối cảnh hiện tại — 2026-03-31

### Trạng thái thực tế

1. **Build:** Pass sạch (0 errors, 0 warnings). **Tests:** 250/250 pass.
2. **Wave 1 cleanup HOÀN TẤT:** AGENTS.md, ASSISTANT.md restored. README accurate. Read order unified.
3. **Pipeline Phase 1 SHIPPED (commit `6bd1e47`):** Conversational fast-path mở rộng từ 4 → 7 intents. `EnhanceConversationalAsync` timeout 8s (vs 20s). "tổng quan project", "kiểm tra model", "phân tích family" → 1-3s thay vì 5-18s.
4. **Docs Phase 2 SHIPPED (commit `6081c84`):** 765T_PRODUCT_VISION.md có TARGET STATE warning. ARCHITECTURE.md có authority clarification. Tool count fixed 192 → 237.
5. **Semantic Memory Phase 3 SHIPPED (commit `613fd64`):** `OllamaEmbeddingClient` + `EmbeddingProviderFactory` + config + tests. WorkerHost tự thử Ollama local rồi fallback sang hash nếu chưa có server.
6. **Timeout Centralization Phase SHIPPED:** `LlmTimeoutProfile` record thay thế 15+ magic numbers. `WorkerHostSettings` centralize rate-limit, kernel pipe retry, LLM timeout config. `appsettings.json` cho WorkerHost.
7. **WorkerTab Service Extraction Phase (3A) SHIPPED:** 5 service classes extracted từ 6,831-line God Object. `ChatSubmissionService` fix P0 async-void bug. Services additive — WorkerTab chưa bị sửa.
8. **UI Pane Feature Flag:** `EnableUiPane = false` trong `AgentSettings`. WPF chat pane tạm tắt để focus demo CLI/MCP flow.
9. **Launch prep:** README polished, .gitignore hardened (secrets blocked), personal paths cleaned.

### Sự thật chưa fix

- **WorkerHost standalone chat:** HTTP `/api/external-ai/chat` vẫn block nếu không có Revit, vì `MissionOrchestrator` luôn gọi kernel pipe. Fast-path hiện mới nằm ở Agent-side workflow.
- **Role-based filtering:** Chưa implement. 237 tools broadcast cho tất cả roles.
- **WorkerTab wiring (Phase 3B):** 5 service classes đã extract nhưng chưa wire vào WorkerTab. Services additive, chạy song song code cũ.

### Mục tiêu tiếp theo

- **P0:** Wire WorkerTab → services (Phase 3B-3E). Fix async void P0 bug trong production code path.
- **P1:** WorkerHost standalone conversational fast-path.
- **P2:** Role-based tool filtering. UI telemetry harness.

## Critical Notes — Read First

> Updated after each working session. Last updated: 2026-03-31

### Architecture Lesson — Worker Conversation Flow (2026-03-26)

**Bài học xương máu:** Hệ thống worker.message ĐÃ over-engineer conversation flow. Mỗi user message đi qua 5 rounds (GatherContext → PlanStep LLM → ExecuteIntent → EnhanceStep LLM → BuildResponse) — **kể cả "chào em"**. Đây là sai lầm kiến trúc, không phải bug.

**Fix (2026-03-27):** `LlmResponseEnhancer.ResponseTimeoutSeconds = 10` và `AnthropicLlmClient.DefaultTimeoutSeconds = 12` → enhancer cancel trước khi HTTP call finish → LLM "stuck" (silent fallback). Fix: đồng bộ cả hai thành 20s. Commit `12acbb1`.

**Research từ Claude Code, Cline, Cursor** cho thấy pattern đúng:

1. **Runtime loop phải "dumb"** — chỉ là vòng lặp TAOR (Think → Act → Observe → Repeat). Mọi intelligence nằm trong model + prompt, KHÔNG phải routing code.
2. **LLM quyết định dùng tool hay chat** — không cần code-level router, planner, enhancer riêng. System prompt mô tả tools, model tự chọn.
3. **Streaming là king** — partial text updates, chunk-by-chunk rendering. User thấy response ngay khi token đầu tiên xuất hiện.
4. **Context engineering > prompt engineering** — quản lý context window là bài toán khó nhất. Compaction, dedup file reads, sub-agent isolation.

**Ưu tiên sản phẩm (Owner directive):**
- **P0: Trả lời mượt mà** — nhanh, tự nhiên, như đang chat với Claude Code
- **P1: Cầu nối AI ↔ Revit** — gọi tool khi thật sự cần, không phải mọi message
- **P2: Workflow xịn** — automation thay BIM modeller (MVP stage, chưa ưu tiên)

**Hướng đi đúng cho worker.message:**
- Conversational path: 1 LLM call thẳng, có Revit context nhẹ, response 1-2s
- Action path: Full pipeline chỉ khi user CẦN Revit làm gì đó
- LLM tự quyết định path nào, không cần code routing

**Tham khảo kiến trúc:**
- Cline: Single recursive loop `recursivelyMakeClineRequests()`, no planner/enhancer
- Claude Code: TAOR loop `nO`, ~50 lines, ~15 primitive tools, model decides everything
- Cursor: Apply model tách "what to change" vs "write correct syntax"

**Nguyên tắc:** KHÔNG build orchestration phức tạp khi simple loop + good prompt đủ. Over-engineering orchestration = tạo latency không cần thiết + code khó maintain + trải nghiệm tệ.

### Mindset — Owner Directive

Mindset **"tấn công, tận dụng AI"** — KHÔNG phải "phòng thủ, sợ sai":

- **Chủ động đề xuất, implement, tối ưu** — không đợi hỏi từng bước
- **Batch work** — làm nhiều files một lượt, không micro-step
- **Own the outcome** — làm đến khi kết quả THỰC SỰ hoạt động
- **Gặp blocker thì giải quyết** — không dừng lại báo lỗi rồi chờ
- **Sau mỗi task, update critical notes vào file này** nếu có thay đổi quan trọng
- **Code clean, sạch** — không thừa, không rác, đọc là hiểu
- **KHÔNG tự tiện tạo file .md mới** để báo cáo — output trả thẳng trong conversation, không rải file
- **Suy luận có chiều sâu** — hiểu root cause, không patch bề mặt. Build thông minh, không build bừa

### Repo Structure (2026-03-25)

```text
README.md / README.en.md     — project overview (VI / EN)
AGENTS.md                    — operating constitution
CLAUDE.md                    — THIS FILE
.claude/rules/               — project-rules.md, safety-rules.md (canonical rules)

docs/765T_PRODUCT_VISION.md  — SINGLE vision + research + implementation plan
docs/ARCHITECTURE.md         — 5-layer architecture
docs/PATTERNS.md             — implementation patterns
docs/INDEX.md                — full doc navigator
docs/assistant/BASELINE.md   — current runtime truth
docs/archive/                — old/superseded docs (NOT startup truth)

tools/*.ps1                  — core ops (health, relay, catalog, audit)
tools/infra/                 — install, start, setup scripts
tools/testing/               — test, verify, smoke, benchmark
tools/round/                 — round/penetration domain workflows
tools/dev/scratch/           — temp investigation scripts
```

### What Already Works — Don't Rebuild

(Details: docs/765T_PRODUCT_VISION.md Section 6)

- 20 tool modules, 237 tools — production-ready
- 14 specialist packs — configured
- WPF dockable pane — single worker shell (NOT "4 tabs", see BASELINE.md)
- WorkerHost (gRPC + HTTP + SSE streaming)
- Named pipe IPC, Rule engine, Mutation workflow, Session persistence

### Key Technical Findings

(Details: docs/765T_PRODUCT_VISION.md Section 3)

1. Revit API single-threaded — use DMU + IdlingEvent chunked, NOT full model scan
2. Token cost $0.27/month (tiered: rules engine 75%, stats 15%, LLM 10%)
3. Role-based tool filtering — 25 tools/role, NOT 192 (accuracy 50% → 85%+)
4. SK only for LLM abstraction — NOT replacing orchestration
5. Full Nice3point migration = 6 months risk — adopt RevitTask selectively only
6. Memory MUST be fully local (Ollama + Qdrant + SQLite) for enterprise

### Vietnamese Encoding Gotcha

12 files fixed (2026-03-25). Khi tạo Vietnamese .md files — LUÔN save UTF-8 with BOM hoặc verify encoding. See commit `4420cc3` for affected files.

## Build & Test

```powershell
dotnet build BIM765T.Revit.Agent.sln -c Release    # Build all
dotnet test BIM765T.Revit.Agent.sln -c Release      # Test all
dotnet test tests/BIM765T.Revit.Contracts.Tests -c Release --filter "FullyQualifiedName~JsonUtilExtendedTests"  # Single test
powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1  # Deploy
```

**Note:** Agent project (net48) needs `RevitAPI.dll` + `RevitAPIUI.dll` via `$(Revit2024InstallDir)`.

## Architecture — Quick Reference

Two processes, named pipes:

```text
Revit Process (net48)                    Standalone Process (net8.0)
┌──────────────────────┐    Named Pipes   ┌──────────────────────┐
│  BIM765T.Revit.Agent │ ◄──────────────► │  BIM765T.Revit.      │
│  (execution kernel)  │  Kernel Channel  │  WorkerHost          │
│  - Revit API calls   │                  │  (control plane)     │
│  - WPF dockable pane │                  │  - AI orchestration  │
│  - ExternalEvent     │                  │  - SQLite + Qdrant   │
│    scheduler         │                  │  - HTTP/gRPC/SSE     │
└──────────────────────┘                  └──────────────────────┘
```

**Critical boundary:** Only `BIM765T.Revit.Agent` references RevitAPI.dll. Enforced by Architecture.Tests.

### Entry Points

- **Revit add-in:** `AgentApplication.cs` → `AgentHost.Initialize()`
- **WorkerHost:** `Program.cs` → `WebApplication.CreateBuilder`
- **Bridge CLI:** `BIM765T.Revit.Bridge/Program.cs`
- **MCP Host:** `BIM765T.Revit.McpHost/Program.cs`

### Key Patterns

- **ExternalEvent** for mutations from background threads → `ToolExternalEventHandler` → `ToolInvocationQueue` → UI thread. Never `.Result` on UI thread.
- **Tool modules:** `*ToolModule.cs` implements `IToolModule`, registers into `ToolRegistry`. Location: `src/BIM765T.Revit.Agent/Services/Bridge/`
- **Mutation flow:** `DRY_RUN → APPROVAL → EXECUTE → VERIFY`. Token expires 5 min. Context hash must match.
- **Named pipes:** Agent = `KernelPipeHostedService` (server), WorkerHost = `KernelPipeClient`
- **LLM timeouts:** `LlmTimeoutProfile` record centralizes all timeout/token constants. Consumed by every LLM client and service.
- **WorkerTab services:** 5 extracted services in `UI/Tabs/Services/` — `MissionStreamService`, `ChatSubmissionService`, `MissionCommandService`, `ProjectDashboardService`, `SessionLifecycleService`
- **UI feature flag:** `AgentSettings.EnableUiPane` controls WPF pane registration at startup. Default `false` for CLI/MCP demo mode.

## Rules — See Canonical Sources

> Do NOT duplicate rules here. Rules live in `.claude/rules/`:

- **Architecture, contracts, naming, git:** `.claude/rules/project-rules.md`
- **Mutation flow, security, performance, threading:** `.claude/rules/safety-rules.md`
- **Validation:** Diagnostic codes = English UPPER_SNAKE_CASE, messages = Vietnamese (user-facing), English (dev-facing)

## MCP Servers — Tools tôi (Linh) dùng khi làm việc

> Cấu hình tại `.claude/settings.json`. Đây là MCP servers cho **Claude Code**, KHÔNG phải `BIM765T.Revit.McpHost` (project MCP adapter trong codebase).

| Server | Mục đích | Khi nào dùng |
| ------ | -------- | ------------ |
| **context7** | Live library docs — inject docs mới nhất, đúng version cho bất kỳ library nào (Revit API, WPF, ASP.NET, Qdrant, etc.) | Khi cần tra cứu API/library mà không muốn hallucinate docs cũ |
| **mem0** | Persistent AI memory — lưu user prefs, project decisions, lessons learned qua các session | Khi cần nhớ quyết định đã thống nhất, patterns đã học, context dài hạn |
| **sequential-thinking** | Dynamic reasoning chains — suy luận multi-step cho complex BIM analysis và planning | Khi cần phân tích phức tạp: architecture decisions, debugging chains, tradeoff analysis |
| **qdrant** | Vector search BIM semantic memory — tìm kiếm across project knowledge | Khi cần tìm context liên quan trong project memory, lessons, evidence |

### Chi tiết kỹ thuật

```text
context7            npx -y @upstash/context7-mcp@latest
mem0                python .claude/mcp/mem0-server.py
                    env: MEM0_LLM_PROVIDER=openai, MEM0_LLM_MODEL=gpt-4.1-nano
sequential-thinking npx -y @modelcontextprotocol/server-sequential-thinking
qdrant              uvx mcp-server-qdrant
                    env: QDRANT_URL=http://127.0.0.1:6333, COLLECTION_NAME=bim765t
```

### Lưu ý sử dụng

- **mem0** cần `pip install mem0ai` và OpenAI API key (dùng gpt-4.1-nano cho memory extraction, rẻ)
- **qdrant** cần Qdrant chạy local tại port 6333 (`docker run -p 6333:6333 qdrant/qdrant`)
- **context7** và **sequential-thinking** chỉ cần `npx`, không cần setup thêm
- Permissions đã cho phép: `mcp__context7__*`, `mcp__mem0__*`, `mcp__memory__*`, `mcp__sequential-thinking__*`, `mcp__qdrant__*`

## Config & Runtime

| What | Where |
| ---- | ----- |
| LLM provider | First-found-wins: `OPENROUTER_API_KEY` → `MINIMAX_API_KEY` → `OPENAI_API_KEY` → `ANTHROPIC_AUTH_TOKEN`. Pin: `BIM765T_LLM_PROVIDER` |
| Revit runtime | `src/BIM765T.Revit.Agent/Config/AgentSettings.cs` |
| WorkerHost runtime | `src/BIM765T.Revit.WorkerHost/Configuration/WorkerHostSettings.cs` |
| Workspace seed | `workspaces/default/workspace.json` |
| User workspaces | `%APPDATA%\BIM765T.Revit.Agent\workspaces\` |
| Memory | SQLite (durable truth) + Qdrant (hash embeddings, non-semantic) + Agent JSON (migration) |
| Memory namespaces | `atlas-native-commands`, `atlas-custom-tools`, `atlas-curated-scripts`, `playbooks-policies`, `project-runtime-memory`, `evidence-lessons` |
