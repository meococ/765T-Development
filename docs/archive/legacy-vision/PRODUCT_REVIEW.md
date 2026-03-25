# BIM765T — Phân Tích Đa Góc Nhìn: Từ Vi Mô Đến Vĩ Mô

> **Ngày:** 2026-03-17
> **Phương pháp:** So sánh LangGraph / AutoGen / OpenAI Swarm / Gemini ADK / MCP spec + 4 personas thực tế
> **Tác giả:** Review session cùng AI assistant — để anh (BIM Manager) dùng làm roadmap quyết định

---

## Mục Lục

1. [So sánh với Pro AI Agent Frameworks](#1-so-sánh-với-pro-ai-agent-frameworks)
2. [Góc nhìn 4 Personas](#2-góc-nhìn-4-personas)
3. [Matrix ưu tiên Vi Mô → Vĩ Mô](#3-matrix-ưu-tiên-vi-mô--vĩ-mô)
4. [Roadmap theo giai đoạn](#4-roadmap-theo-giai-đoạn)
5. [Competitive Positioning](#5-competitive-positioning)
6. [Verdict cuối](#6-verdict-cuối)

---

## 1. So Sánh Với Pro AI Agent Frameworks

### 1.1 — Điều BIM765T đã VƯỢT các framework generic

Đây là điều quan trọng nhất cần nói trước: về **safety + mutation control**, BIM765T tốt hơn LangGraph, AutoGen, CrewAI, OpenAI Swarm. Không phải nói cho có — mà là structural.

| Safety Pattern | LangGraph | AutoGen | CrewAI | OpenAI Swarm | **BIM765T** |
|---|---|---|---|---|---|
| Dry-run trước mutation | ❌ | Sandbox retry | ❌ | ❌ | ✅ Native |
| Context fingerprint chống TOCTOU | ❌ | ❌ | ❌ | ❌ | ✅ SHA256 |
| Approval token có TTL + payload binding | Interrupt gate (no binding) | ❌ | ❌ | ❌ | ✅ Persisted + bound |
| Per-item rollback trong batch | ❌ | ❌ | ❌ | ❌ | ✅ TransactionGroup |
| Domain grounding trước khi act | ❌ | ❌ | ❌ | ❌ | ✅ Inspector Lane (5 tools) |
| MCP-native protocol | External adapter | External | External | External | ✅ Built-in |
| Audit trail có structured envelope | ❌ | Log only | ❌ | ❌ | ✅ StatusCode + Diagnostics[] |

> **Kết luận:** Safety architecture của BIM765T là **genuine competitive moat** — không phải nice-to-have mà là foundational. Giữ nguyên, không refactor core.

---

### 1.2 — Gaps so với pro frameworks (5 gaps thực sự)

#### Gap 1 — Durable Execution (LangGraph's killer feature)

LangGraph persist checkpoint tại mỗi node vào DB. Crash bất cứ lúc nào → resume chính xác từ step đó mà không cần user redo.

```
LangGraph:  Step1 → [DB checkpoint] → Step2 → [DB checkpoint] → Step3
BIM765T:    Step1 → Step2 → Step3   (toàn bộ trong RAM, Revit crash = mất hết)
```

**Impact thực tế:** `parameter_rollout` trên 500 elements chạy 15 phút → Revit treo → user phải replay từ đầu.

**Fix:** Persist `WorkflowRun` snapshot ra `%APPDATA%\BIM765T.Revit.Agent\runs\<runId>.json` sau mỗi step completion.

---

#### Gap 2 — Conditional Branching (không phải linear pipeline)

LangGraph / LangChain cho phép conditional edges:

```
inspect_model
  → [penetration_found: true]   → create_round_shadow
  → [penetration_found: false]  → report_clean
  → [partial_fail: true]        → recovery_path → human_review
```

BIM765T: 5 workflows đều là **linear sequence**. Partial-fail ở step 3 → AI phải dừng chờ human prompt để tiếp tục — không có autonomous recovery branch.

**Impact thực tế:** Workflow `penetration_round_shadow` xử lý 200 elements, 30 fail do family missing → toàn bộ workflow dừng, không có "skip failed, continue with rest" path.

---

#### Gap 3 — Distributed Tracing (production prerequisite)

LangSmith (Anthropic), Google Cloud Trace, OpenTelemetry — tất cả cung cấp **correlation ID duy nhất** cho mỗi request, trace từ entry point → pipe → ExternalEvent → Revit API call → response.

BIM765T hiện tại: flat log file `yyyyMMdd.log`. Debug incident qua 4 layers = grep thủ công + correlate bằng timestamp.

**Fix cụ thể:** Thêm `CorrelationId` vào `ToolRequestEnvelope` + `ToolResponseEnvelope` → log tại mỗi layer với cùng ID → query `WHERE correlationId = 'abc123'` thay vì grep.

---

#### Gap 4 — Tool Schema as First-Class Artifact

MCP protocol spec 2024 yêu cầu tools có thể được publish và discovered bởi external clients. BIM765T có schemas đầy đủ nhưng **embedded trong code** — không exportable thành `tools.json` standalone.

**Impact:** Khó integrate với external orchestrators (n8n, Zapier, custom dashboards). Khó onboard developer mới mà không cần đọc source.

---

#### Gap 5 — Workflow Context Window Management

Với workflow dài (parameter rollout 1000+ elements, 4+ giờ), **context window LLM sẽ bị overflow**. LangGraph có summarization node: tự động tóm tắt history cũ → giữ context lean.

BIM765T: chưa có. Nếu workflow vượt ~80-100 bước conversation, AI bắt đầu "quên" context đầu, ra quyết định sai.

---

## 2. Góc Nhìn 4 Personas

### 🧱 Persona 1 — BIM Modeller (Staff, không kỹ thuật)

Người dùng cuối hàng ngày. Mở Revit, làm việc, không muốn đọc JSON hay nhớ tool names.

**Có thể làm tốt ngay hôm nay:**
- Home Tab → 3 quick actions (Model Health, Task Context, Warnings) → kết quả tức thì
- Inspector tab: explain element, trace parameter, view dependency graph
- Workflows tab: plan → apply → verify với 5 workflows built-in
- Evidence tab: xem history artifacts

**5 thứ SẼ frustrate họ:**

| Tình huống thực tế | Vấn đề kỹ thuật | Mức độ |
|---|---|---|
| Dry-run xong, đi lấy cà phê 15 phút → về execute | `APPROVAL_MISMATCH` — token expired (10 min TTL) | 🔴 Cao |
| "Cái gì bị thay đổi?" sau dry-run | Phải đọc JSON artifact — không có visual diff trong viewport | 🔴 Cao |
| Chạy batch 800 parameter set | Silence hoàn toàn 10 phút, không có progress bar | 🟡 Trung |
| Element placement failed — "OutOfRange coordinate" | Modeller không biết local axis vs world axis | 🟡 Trung |
| Mặc định `AllowWriteTools=false` | "Sao tôi không sửa được gì?" → cần tìm settings.json | 🟡 Trung |

**Learning curve thực tế:**
```
Week 1   → 3 quick actions, đọc output
Month 1  → Hiểu dry-run → token → execute flow
Month 3+ → Workflow chains, playbook config
```

> **Recommendation:** Cần "Getting Started" 1 trang cho BIM Coordinator. Không phải developer doc — là user guide với screenshots.

---

### 🎛️ Persona 2 — BIM Manager (Team lead, quality owner)

Người setup policy, review audit, approve mutations của team.

**Kiểm soát tốt:**
- `AllowWriteTools / AllowDeleteTools` — master switch
- `policy.json` — DisabledTools[], HighRiskTools[], rate limits
- Presets — family roots, export roots với containment enforcement
- Playbooks — fix-loop rules, recovery scenarios
- `session.get_recent_operations` → audit trail mutation có timestamp

**5 thứ SẼ frustrate BIM Manager:**

| Vấn đề | Tác động | Mức độ |
|---|---|---|
| Policy file là JSON thô, không có validation UI | Typo trong tool name → tool silently fail, không biết tại sao | 🔴 Cao |
| `AllowWriteTools` là binary (on/off) | Bật = mở toàn bộ 30+ mutation tools — không granular per-user | 🔴 Cao |
| Không có per-user tracking | Ai set parameter X? Ai delete family Y? Không trace được | 🔴 Cao |
| Config không hot-reload | Chỉnh `policy.json` → phải restart Revit mới có effect | 🟡 Trung |
| Không có team activity view | Không biết người khác đang chạy batch gì, có conflict không | 🔴 Cao |

> **Scenario nguy hiểm nhất:** 2 BIM Modeller cùng lúc chạy `parameter.set_safe` trên cùng workset → không có collision detection trong tool layer (worksharing handle ở Revit layer nhưng không có warning sớm).

---

### 🤖 Persona 3 — AI / Platform Developer (Muốn extend, integrate, maintain)

**Điểm mạnh thực sự:**
- Pattern thêm tool mới có checklist rõ ràng (6 bước trong BUILD_LOG)
- DTO typing nghiêm ngặt — `Nullable: enable`, explicit usings, C# 12
- 60+ PowerShell scripts = executable runbooks, không phải docs chết
- LESSONS_LEARNED.md có 30+ real incidents với root cause + fix — quý hơn bất kỳ architectural doc nào
- MCP-native protocol — đúng hướng industry standard 2024-2026

**Gaps kỹ thuật:**

| Gap | Impact | Fix |
|---|---|---|
| **0 unit tests cho tool handlers** | Regression khi refactor không được catch | Thêm `IRevitFacade` interface → mock trong tests |
| **Pipe server single-threaded** | 1 tool chậm = block toàn bộ queue | Concurrent dispatch với semaphore |
| **Rate limiter global** | User A spam = user B bị throttle | Per-(user, document) rate limit |
| **Không có CI/CD pipeline** | Mọi thứ manual build + deploy | GitHub Actions basic + install-addin.ps1 |
| **Không có REST/HTTP layer** | Chỉ named pipe + MCP stdio | Không integrate được với external tools |

**Bottleneck architecture quan trọng nhất:**
```csharp
// Hiện tại: ToolExternalEventHandler dequeue + execute SEQUENTIAL
// Tool A chạy 2 phút → Tool B, C, D đều chờ

// Cần: Independent read-only tools chạy concurrent
// Tool A (mutation) → lock + queue
// Tool B, C (read-only) → run in parallel
```

---

### 🚀 Persona 4 — Startup / Product Owner

**MVP analysis — Honest assessment:**

| Feature | MVP cần? | Đã có? | Over-engineer? |
|---|---|---|---|
| Read inspection + colorize element | ✅ | ✅ | Không |
| Dry-run mutation + approval | ✅ | ✅ | Không |
| 3 killer workflows (health, sheet QC, param rollout) | ✅ | ✅ | Không |
| Evidence bundle JSON | ~ | ✅ | **Có** — complex cho v1 |
| Playbook + Preset config | ~ | ✅ | **Có** — nên là UI, không phải JSON |
| 6-tab UI | ✅ | ✅ | Hợp lý |
| **109 tools** | ~ | ✅ | **Có** — 25-30 tools đủ cho v1 |
| Mode C dual-agent | ❌ | ✅ | Research feature, chưa phải product |
| MCP-native | ✅ | ✅ | Đúng hướng |

**Positioning statement rõ nhất:**
> *"Dynamo không cần biết lập trình. Mọi thay đổi đều có preview + approval + audit trail tự động."*

**Rào cản scale hiện tại:**

| Rào cản | Severity | Lý do |
|---|---|---|
| Local-only, không cloud | 🔴 Critical nếu SaaS | Không thể multi-tenant |
| Windows + Revit 2024 only | 🟡 Trung | Thị trường hẹp nhưng focused |
| Không có REST API | 🟡 Trung | Không integrate được với n8n, Power BI, v.v. |
| Onboarding cần kỹ thuật | 🔴 Critical | Named pipe, settings.json — user thường không tự setup được |

**Mô hình giá tiềm năng:** $600–900/seat/năm cho BIM team 5–50 người. Tier enterprise: $3,000–5,000/năm cho unlimited seats + priority support.

---

## 3. Matrix Ưu Tiên: Vi Mô → Vĩ Mô

### 🔴 Critical — Làm Ngay (Risk Thực, Blockers)

Những thứ này có latent bug hoặc security/reliability risk đang âm ỉ:

| # | Vấn đề | File liên quan | Effort | Impact |
|---|---|---|---|---|
| C1 | **CorrelationId qua 4 layers** — không có = debug incident mất giờ | `ToolRequestEnvelope`, `ToolResponseEnvelope`, McpHost, Bridge | 2 ngày | Observability |
| C2 | **Per-tool execution timeout** — không có = DoS surface (1 tool treo = queue chết) | `ToolRegistry.cs`, tool manifest, `ToolExternalEventHandler` | 1 ngày | Stability |
| C3 | **HMAC cho approval token persistence** — file token không có integrity check | `ApprovalTokenStore.cs` | 1 ngày | Security |
| C4 | **Centralize `MaxMcpPayloadBytes`** — hardcoded ở McpHost, không khớp BridgeConstants | `BridgeConstants.cs`, `McpHost/Program.cs` | 0.5 ngày | Latent bug |
| C5 | **PowerShell array/scalar systemic fix** — known bug trong 60+ PS scripts | `tools/*.ps1`, `deploy/*.ps1` | 1 ngày | Reliability |

---

### 🟡 High Value — Trong 1 Tháng (Team Adoption Blockers)

Không có những thứ này, team 5 người không dùng được an toàn:

| # | Vấn đề | Impact cụ thể |
|---|---|---|
| H1 | **User identity tracking** — Windows user gắn vào mọi operation trong audit log | "Ai làm cái này?" → có câu trả lời |
| H2 | **Role-based tool gates** trong `policy.json` | BIM Manager giới hạn mutation tools cho từng role |
| H3 | **Approval UI** — card trong Workflows tab thay vì CLI token copy-paste | Modeller approve bằng click, không phải terminal |
| H4 | **Progress streaming** cho batch ops (report every 10 items) | Batch 500 không còn là black box 10 phút |
| H5 | **Workflow state persistence to disk** — resume sau Revit crash | `parameter_rollout` 15 phút không cần replay từ đầu |
| H6 | **`IRevitFacade` interface** — unlock 109 tool handlers có unit-testable | Refactor an toàn, regression được catch |
| H7 | **Structured audit log JSON Lines** tách khỏi app log | Query "tất cả mutations hôm nay" thành 1 grep |
| H8 | **Config hot-reload** watch `%APPDATA%` | Chỉnh policy → effect ngay, không restart Revit |

---

### 🟢 Strategic — Q2 (Scale & Architectural Upgrade)

| # | Vấn đề | Tại sao quan trọng |
|---|---|---|
| S1 | **Conditional workflow branching** — `[if partial_fail] → recovery_path` | Autonomous recovery, không cần human interrupt |
| S2 | **Per-document config override** (`project.json` trong thư mục .rvt) | Project A và B có risk policy khác nhau |
| S3 | **Tool schema export** — `tools.json` MCP-compatible per module | External integration, developer onboarding |
| S4 | **Mode C relay: checksum + heartbeat** — atomic handoff guarantee | Dual-agent không bị desync |
| S5 | **Circuit breaker per tool** — disable nếu fail >5 lần/giờ, re-enable sau 10 phút | Self-healing, không cần restart |
| S6 | **Simple metrics JSON** — tool latency p50/p95, fail rate, approval rate | Biết bottleneck ở đâu |
| S7 | **Workflow context summarization** — tóm tắt history cũ sau 80 bước | Context window không bị overflow |
| S8 | **Concurrent read-only tool execution** — không block queue | Performance 3-5x cho inspection workflows |

---

### 🔵 Product Vision — Q3+ (Nếu Muốn Scale Thành Sản Phẩm Thực)

| # | Vấn đề | Mô tả |
|---|---|---|
| P1 | **Settings/Policy UI (admin panel)** | Thay vì edit JSON thô — form với validation |
| P2 | **Team activity dashboard** | "Ai đang chạy gì, workset nào đang locked" |
| P3 | **Auto-update service** | Khi load add-in, check version → prompt update |
| P4 | **"Getting Started" guide cho BIM Coordinator** | 1-2 trang với screenshots, không phải dev doc |
| P5 | **Error troubleshooting flowchart** | Keyed to status codes: `CONTEXT_MISMATCH` → "làm gì tiếp" |
| P6 | **REST/HTTP Bridge wrapper** | Mở ecosystem integration (n8n, Power BI, webhook) |
| P7 | **Installer UI** (không phải PowerShell script) | Onboarding cho user không biết terminal |

---

## 4. Roadmap Theo Giai Đoạn

```
Phase A ──────────────────── "Không Có Bug Ngầm"          (2 tuần)
  C1: CorrelationId qua 4 layers
  C2: Per-tool timeout
  C3: HMAC cho token store
  C4: Centralize MaxMcpPayloadBytes
  C5: PS array/scalar fix
  → Outcome: System stable, không có latent security/reliability risk

Phase B ──────────────────── "Team Dùng Được"             (4 tuần tiếp)
  H1–H8: user identity, role gates, approval UI,
         progress streaming, workflow persistence,
         IRevitFacade, audit log, hot-reload
  → Outcome: 5 người dùng cùng lúc, BIM Manager kiểm soát được

Phase C ──────────────────── "Scale Architecture"          (Q2 2026)
  S1–S8: conditional branching, project config,
         schema export, Mode C relay, circuit breaker,
         metrics, context summarization, concurrent tools
  → Outcome: Platform self-healing, observable, extensible

Phase D ──────────────────── "Sản Phẩm Thực"              (Q3 2026+)
  P1–P7: settings UI, team dashboard, auto-update,
         user docs, REST API, installer
  → Outcome: Onboard customer mới không cần anh hướng dẫn tay
```

---

## 5. Competitive Positioning

### Nơi BIM765T ngồi trong landscape

```
                      READ-ONLY / QUERY
                   (tìm thông tin, tô màu)
                            │
      DiRoots OneFilter ────┤──── AI chatbots (BIMsmith, etc.)
      Speckle viewer        │     "Chat với model Revit"
                            │
  ────────────────────────────────────────────────────────────
                            │         MUTATION
                            │      (thay đổi model)
                            │
      Dynamo ───────────────┤
      pyRevit               │
                            │
                            │    ◄── BIM765T ngồi đây
                            │        guarded mutation +
                            │        approval workflow +
                            │        domain grounding
                       Autodesk Forma
                       (early design,
                        cloud, generative)
```

### Đối thủ thực sự cần theo dõi

| Đối thủ | Threat Level | Lý do |
|---|---|---|
| **Autodesk native AI** | 🔴 Cao (long-term) | Direct API, distribution, budget R&D. Nhưng họ focus early design, không phải operational BIM + shop drawings |
| **Speckle + LLM** | 🟡 Trung | Data platform tốt, nhưng không có mutation safety, không có domain grounding |
| **AI chatbot wave** (startups 2024-25) | 🟢 Thấp | Làm read-only tốt, nhưng không có guarded mutation — khác niche |
| **Dynamo / pyRevit** | 🟢 Thấp | Không AI-native, cần biết code — BIM765T là "Dynamo không cần lập trình" |

### Moat thực sự (defensible advantages)

1. **Write safety pipeline** — không framework nào build cái này cho BIM context
2. **Inspector Lane** — grounding AI bằng domain knowledge trước khi act — genuine innovation
3. **MCP-native** — đúng hướng industry standard đang emerge
4. **Modular construction domain** — penetration workflow, round shadow, MiTek-specific — vertical focus

---

## 6. Verdict Cuối

### Những gì đang làm xuất sắc

| # | Điều | Tại sao quan trọng |
|---|---|---|
| ✅ | Write safety pipeline với dry-run → approval → execute | Vượt tất cả generic AI framework |
| ✅ | Inspector Lane — grounding trước khi act | Không ai trong AEC space làm cái này đúng |
| ✅ | Context fingerprint + TOCTOU protection | Production-grade, bảo vệ Revit model |
| ✅ | MCP-native protocol | Đúng hướng, không phải custom protocol sẽ chết |
| ✅ | Memory system (BUILD_LOG, LESSONS_LEARNED) | Knowledge management tốt hơn hầu hết startup AEC |
| ✅ | Explicit usings, nullable enable, C# 12 | Code quality nghiêm ngặt từ đầu |

### 3 quyết định sẽ xác định BIM765T có scale được không

```
1. TEST COVERAGE
   Hiện tại: 0 unit tests cho tool handlers/services
   Risk: Refactor bất kỳ service nào = không biết regression ở đâu
   Fix key: IRevitFacade interface → mock Revit trong tests

2. OBSERVABILITY
   Hiện tại: flat log file, không có trace ID
   Risk: Incident production = grep thủ công qua 4 layers
   Fix key: CorrelationId trong envelope → log tất cả layers cùng ID

3. USER IDENTITY + ROLE GATES
   Hiện tại: ai cũng thấy tất cả tools, không có per-user audit
   Risk: Team 5 người dùng → không ai chịu trách nhiệm khi có mutation sai
   Fix key: Windows user vào audit log + AllowedRoles per tool trong policy.json
```

### Bottom line

> **BIM765T đang ở trình độ safety design tốt hơn LangGraph/AutoGen, nhưng operational maturity thấp hơn** (no tracing, no durable execution, no test coverage, no user identity).
>
> Foundation solid. Architecture đúng hướng. Đây là **senior engineer's work đang cần production hardening** — không phải rewrite từ đầu.
>
> Con đường rõ ràng: Phase A (2 tuần) loại bỏ bug ngầm, Phase B (4 tuần) unlock team adoption. Đó là 6 tuần để đi từ "power tool của 1 người" thành "platform cho team 5 người."

---

*File này là living document — update khi có thay đổi architecture lớn hoặc competitive landscape shift.*
*Maintained by: BIM Manager (Mèo Cọc) + AI assistant*
