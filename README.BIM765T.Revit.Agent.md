# 765T Assistant Platform — Quick Reference

> File này là **bản tóm tắt nhanh** toàn bộ hệ thống. Canonical truth nằm tại các file trong Read Order bên dưới.
> Cập nhật lần cuối: 2026-03-30

---

## Read Order

1. `CLAUDE.md` — critical notes & dev guidance
2. `README.md` / `README.en.md` — project overview
3. `AGENTS.md` — operating constitution
4. `ASSISTANT.md` — repo adapter & assistant baseline
5. `docs/765T_PRODUCT_VISION.md` — product direction & target state
6. `docs/ARCHITECTURE.md` — system boundary & ownership
7. `docs/PATTERNS.md` — implementation patterns & safety flow
8. `docs/assistant/BASELINE.md` — current runtime truth
9. `docs/assistant/CONFIG_MATRIX.md` — config ownership

---

## Architecture — Two Processes, Named Pipes

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

**Critical boundary:** Chỉ `BIM765T.Revit.Agent` được chạm Revit API. Enforced bởi Architecture.Tests.

---

## Core Boundary

| Project | Framework | Vai trò |
|---------|-----------|---------|
| `BIM765T.Revit.Agent` | net48 / WPF | Execution kernel — Revit API, worker shell, ExternalEvent scheduler |
| `BIM765T.Revit.WorkerHost` | net8.0 | Control plane — orchestration, memory projection, external AI gateway |
| `BIM765T.Revit.Copilot.Core` | netstandard2.0 | Shared services — packs, playbooks, routing, context, LLM backbone |
| `BIM765T.Revit.Contracts` | netstandard2.0 | Append-only DTOs & proto contracts |
| `BIM765T.Revit.Contracts.Proto` | netstandard2.0 | gRPC proto definitions |
| `BIM765T.Revit.Bridge` | net8.0 | CLI bridge over named pipes |
| `BIM765T.Revit.McpHost` | net8.0 | MCP stdio adapter |
| `BIM765T.Revit.Agent.Core` | netstandard2.0 | Core agent logic (no Revit API dependency) |

---

## Entry Points

| Entry point | File | Mô tả |
|-------------|------|-------|
| Revit add-in | `src/BIM765T.Revit.Agent/Addin/AgentApplication.cs` | `IExternalApplication` → `AgentHost.Initialize()` → Ribbon + Dockable Pane |
| WorkerHost | `src/BIM765T.Revit.WorkerHost/Program.cs` | `WebApplication.CreateBuilder` → HTTP :50765 + gRPC + Named Pipes |
| Bridge CLI | `src/BIM765T.Revit.Bridge/Program.cs` | CLI over named pipes |
| MCP Host | `src/BIM765T.Revit.McpHost/Program.cs` | MCP stdio adapter |

---

## UI Truth

Dockable pane hiện tại là **chat-first single worker shell** với 4 surface concepts:

- **Chat** — giao tiếp chính với AI
- **Commands** — command atlas / command palette
- **Evidence** — kết quả, báo cáo, artifacts
- **Activity** — timeline, health check, events

> Đây là surface concepts trong cards/rail/inspector, **không phải** 4 tabs riêng biệt.

---

## Safety Truth — Tiered Mutation Flow

| Loại action | Flow |
|-------------|------|
| Read-only / harmless | Quick-path trực tiếp, không bottleneck |
| Deterministic mutation | Preview hoặc light confirm (theo manifest/policy) |
| High-impact mutation | `preview → approval → execute → verify` |

- Approval token TTL mặc định: **10 phút** (configurable)
- Context hash phải match giữa preview và execute
- Approval token cache trên disk phải encrypted (DPAPI); nếu không có thì skip persist, **không** fallback plaintext

---

## Memory Truth

| Layer | Công nghệ | Vai trò |
|-------|-----------|---------|
| Durable truth | **SQLite** | Event store + memory projection (bắt buộc) |
| Vector layer | **Qdrant** | Hash embeddings hiện tại (chưa semantic thật) |
| Embedding | **Ollama** (optional) | `nomic-embed-text` — fallback sang hash nếu không có server |
| Agent state | **JSON files** | Task/queue/episodic state (migration window) |

**Health states:**
- `vector_available_non_semantic` — Qdrant reachable, hash embeddings
- `lexical_only` — Qdrant unavailable, SQLite vẫn hoạt động

**Memory namespaces:**
`atlas-native-commands`, `atlas-custom-tools`, `atlas-curated-scripts`, `playbooks-policies`, `project-runtime-memory`, `evidence-lessons`

---

## LLM Provider — Resolution Priority

Code truth từ `OpenRouterFirstLlmProviderConfigResolver`:

| Priority | Provider | Env var(s) | Default model |
|----------|----------|------------|---------------|
| 0 (pin) | `BIM765T_LLM_PROVIDER` | Hard pin: `OPENROUTER` / `MINIMAX` / `OPENAI` / `ANTHROPIC` / `RULE_FIRST` | — |
| 1 | OpenRouter | `OPENROUTER_API_KEY` | `openai/gpt-5.2` |
| 2 | MiniMax | `MINIMAX_API_KEY` / `MINIMAX_AUTH_TOKEN` | `MiniMax-M2.7-highspeed` |
| 3 | OpenAI | `OPENAI_API_KEY` / `OPENAI_AUTH_TOKEN` | `gpt-5-mini` |
| 4 | Anthropic | `ANTHROPIC_AUTH_TOKEN` / `ANTHROPIC_API_KEY` | `claude-sonnet-4-20250514` |

> **First-found-wins** trừ khi pin bằng `BIM765T_LLM_PROVIDER`. Provider choice là configuration, **không phải** product identity.

---

## Tool System

- **20 tool modules** tại `src/BIM765T.Revit.Agent/Services/Bridge/*ToolModule.cs`
- **237 tools** đã đăng ký (production-ready)
- **14 specialist packs** configured (sheet, audit, family, delivery, debug, governance, annotation, coordination, systems, integration, command-atlas, documentation, script-broker, memory-librarian)
- Tool module chỉ register/orchestrate — heavy Revit logic nằm trong service layer
- Pattern: `IToolModule` → `ToolRegistry` → `ToolManifestFactory`

**Key tool modules:** AuditCenter, CommandAtlas, CopilotTask, DataLifecycle, DeliveryOps, ElementAndReview, FamilyAuthoring, FixLoop, Intelligence, MutationFileAndDomain, Parameter, PenetrationWorkflow, QueryPerformance, ScriptOrchestration, SessionDocument, SheetView, SpatialIntelligence, ViewAnnotationAndType, Worker, WorkflowInspector

---

## Key Patterns

- **ExternalEvent** cho mutations từ background threads → `ToolExternalEventHandler` → `ToolInvocationQueue` → UI thread. **Không bao giờ** `.Result` trên UI thread.
- **Named pipes:** Agent = `KernelPipeHostedService` (server), WorkerHost = `KernelPipeClient`
- **Pipe names:** `BIM765T.Revit.Agent` (legacy), `BIM765T.Revit.Agent.Kernel` (kernel channel), `BIM765T.Revit.WorkerHost` (public)
- **Mission orchestration:** `MissionOrchestrator` → `PlannerAgent` → `RetrieverAgent` → `SafetyAgent` → `VerifierAgent`
- **Autonomy mode:** `ship` (default) — broader candidate pool, lower confidence threshold (0.22 vs 0.45)
- **Conversational fast-path:** 7 intents, 1-3s response, 1 LLM call — **không** qua full pipeline

---

## Build & Test

```powershell
dotnet build BIM765T.Revit.Agent.sln -c Release     # Build all
dotnet test  BIM765T.Revit.Agent.sln -c Release      # Run 442 tests
dotnet test tests/BIM765T.Revit.Contracts.Tests -c Release --filter "FullyQualifiedName~JsonUtilExtendedTests"  # Single test
powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1  # Deploy addin
```

**Yêu cầu:** Agent project (net48) cần `RevitAPI.dll` + `RevitAPIUI.dll` qua NuGet `Nice3point.Revit.Api` (version driven by `$(RevitYear)`, default = 2024).

---

## Runtime Paths

| What | Where |
|------|-------|
| User workspaces (shipping) | `%APPDATA%\BIM765T.Revit.Agent\workspaces\` |
| WorkerHost state | `%APPDATA%\BIM765T.Revit.Agent\workerhost\` |
| SQLite event store | `%APPDATA%\BIM765T.Revit.Agent\workerhost\workerhost.sqlite` |
| Qdrant companion | `%APPDATA%\BIM765T.Revit.Agent\companion\qdrant\` |
| User settings | `%APPDATA%\BIM765T.Revit.Agent\settings.json` |
| Repo seed workspace | `workspaces/default/workspace.json` (dev fixture, **không phải** user runtime) |
| Manual smoke test | `docs/assistant/MVP_SMOKE_CHECKLIST.md` |

---

## Config & Runtime

| What | Where |
|------|-------|
| Revit-side config | `src/BIM765T.Revit.Agent/Config/AgentSettings.cs` |
| WorkerHost config | `src/BIM765T.Revit.WorkerHost/Configuration/WorkerHostSettings.cs` |
| Shared constants | `src/BIM765T.Revit.Contracts/Common/BridgeConstants.cs` |
| WorkerHost HTTP port | `:50765` (localhost only) |
| Kernel pipe | `BIM765T.Revit.Agent.Kernel` |
| Public pipe | `BIM765T.Revit.WorkerHost` |
| Rate limit (global) | 100 req/min per IP |
| Rate limit (chat) | 10 req/min per IP |

---

## Canonical Docs

| File | Vai trò |
|------|---------|
| `CLAUDE.md` | Critical notes, dev guidance, architecture lessons |
| `AGENTS.md` | Operating constitution |
| `ASSISTANT.md` | Repo adapter & assistant baseline |
| `docs/765T_PRODUCT_VISION.md` | Product direction & target state (**không phải** runtime truth) |
| `docs/ARCHITECTURE.md` | System boundary & ownership model |
| `docs/PATTERNS.md` | Implementation patterns & safety flow |
| `docs/assistant/BASELINE.md` | **Current runtime truth** |
| `docs/assistant/CONFIG_MATRIX.md` | Config ownership matrix |
| `docs/assistant/SPECIALISTS.md` | Internal specialist definitions |
| `docs/assistant/USE_CASE_MATRIX.md` | P0/P1 use cases & canonical loops |
| `docs/INDEX.md` | Full documentation navigator |

---

## Known Gaps (2026-03-30)

- **WorkerHost standalone chat:** ~~Block nếu không có Revit~~ **FIXED** — `ShouldHandleLocally` giờ default true cho unknown messages, chỉ route sang kernel pipe khi có mutation/live-context keywords
- **Role-based filtering:** Chưa implement — 237 tools broadcast cho tất cả roles (target: 25 tools/role)
- **Embedded worker lane:** Agent vẫn host `worker.*` session/reasoning — chưa fully decoupled sang WorkerHost
- **Streaming/UI telemetry harness:** Chưa có end-to-end test harness cho quy mô hàng trăm/ngàn run

## Recent Fixes (2026-03-30 review pass)

- **HttpClient leak** — `AgentHost.cs` tạo 5x `new HttpClient()` không dispose → shared single instance cho process lifetime
- **Sync-over-async deadlock** — `EmbeddingProviderFactory.ProbeOllamaAvailability` dùng `.GetAwaiter().GetResult()` → synchronous `HttpClient.Send()`
- **Qdrant swallowed errors** — `QdrantSemanticMemoryClient` bỏ qua HTTP failures im lặng → throw `HttpRequestException` cho callers catch/log
- **Redundant regex flag** — `SafetyAgent` dùng `RegexOptions.Compiled` với `[GeneratedRegex]` (redundant) → removed
- **Standalone routing logic** — `StandaloneConversationService` fallthrough routing unknown msgs to kernel → default local handling

---

## Vietnamese Encoding Note

Khi tạo/sửa Vietnamese `.md` files — **luôn** save UTF-8 with BOM hoặc verify encoding trước khi commit. Xem commit `4420cc3` cho danh sách 12 files đã bị lỗi encoding.
