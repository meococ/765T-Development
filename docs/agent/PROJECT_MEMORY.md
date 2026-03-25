# Project Memory

> Operational memory / hybrid reference. If this file conflicts with `docs/assistant/*`, `docs/ARCHITECTURE.md`, or `docs/PATTERNS.md`, the canonical docs win.

## Project in one paragraph

`BIM765T Revit Bridge Platform` là nền tảng Revit add-in + bridge + MCP host để AI hỗ trợ BIM automation theo cách an toàn, audit được, và không cho AI chạm trực tiếp vào raw Revit API ngoài vùng kiểm soát.

## Current local repo identity

- Workspace root: `D:\Development`
- Repo root: `D:\Development\BIM765T-Revit-Agent`
- GitHub: `https://github.com/meococ/Develop-Revit-API.git`
- Default branch: `main`

## Core architecture

- `BIM765T.Revit.Contracts`
  - DTOs, envelopes, validation, status codes, diagnostics
- `BIM765T.Revit.Contracts.Proto`
  - canonical proto contracts cho WorkerHost/control-plane v2
- `BIM765T.Revit.Agent.Core`
  - pure-core execution/orchestration seam co the build/test tren runner khong can Revit
- `BIM765T.Revit.WorkerHost`
  - public control plane `net8.0`
  - gRPC over named pipes
  - SQLite event store + snapshots + outbox
  - memory bootstrap/search (Qdrant + lexical fallback)
- `BIM765T.Revit.Agent`
  - Revit add-in (`net48`) chạy trong Revit 2024
  - private execution kernel
  - nhận request qua private protobuf kernel pipe
  - raise `ExternalEvent`
  - execute trên UI thread
- `BIM765T.Revit.Bridge`
  - CLI facade gọi WorkerHost (`net8.0`)
- `BIM765T.Revit.McpHost`
  - MCP stdio facade gọi WorkerHost (`net8.0`)
- `BIM765T.Revit.Copilot.Core`
  - durable task/context/runtime logic thuần non-Revit

## Safety invariants

- AI không gọi raw Revit API trực tiếp từ ngoài bridge.
- Public client khong noi truc tiep vao add-in `net48`; boundary cong khai di qua WorkerHost.
- Mọi mutation phải đi qua `dry-run -> approval token -> execute`.
- Context fingerprint dùng để chặn scope drift giữa preview và execute.
- Revit mutation phải tôn trọng `ExternalEvent`, UI thread, transaction safety, rollback strategy.
- Với live mutation/export, nhiều `Revit.exe` trên cùng pipe phải coi là unsafe cho tới khi đã isolate đúng process.
- `session.list_tools` la live catalog authoritative tu `ToolRegistry`; MCP `tools/list` phai doc catalog nay qua bridge live va fail-closed neu bridge/catalog khong san sang.
- Qdrant la optional enhancement; lexical fallback tu SQLite projection la mandatory de runtime khong chet khi vector service unavailable.
- WorkerHost Wave 2 hien co them:
  - `LegacyStateMigrator` de import JSON state cu (`tasks`, `memory`, `worker/episodes`, `task-queue`) vao SQLite event store + memory projection
  - `RuntimeHealthService` va CLI `--health-json`
  - deploy scripts `tools/infra/start_workerhost.ps1`, `tools/check_workerhost_health.ps1`, `tools/infra/start_qdrant_local.ps1`

## Build and run entry points

```powershell
dotnet build BIM765T.Revit.Agent.sln -c Release
dotnet test BIM765T.Revit.Agent.sln -c Release
.\src\BIM765T.Revit.Agent\deploy\install-addin.ps1
.\tools\infra\package_revit_bridge_build.ps1 -TargetRoot D:\BIM765Tbuild_v9 -InstallManifest
.\tools\check_bridge_health.ps1
.\tools\check_workerhost_health.ps1
.\src\BIM765T.Revit.WorkerHost\bin\Release\net8.0\BIM765T.Revit.WorkerHost.exe --health-json
.\src\BIM765T.Revit.WorkerHost\bin\Release\net8.0\BIM765T.Revit.WorkerHost.exe --migrate-legacy-state
```

- Repo-level dev defaults song o `.editorconfig`, `.gitattributes`, va `Directory.Build.props` de giu format, line ending, va analyzer defaults dong bo cho ca solution.
- Version baseline hien tai duoc chot o repo root: `Version=1.0.0`, `AssemblyVersion=1.0.0.0`, `FileVersion=1.0.0.0`; SDK projects nen de generated assembly info bat tru khi co manual assembly attributes that su.
- Contracts hien co rule append-only ro hon: field moi phai them o cuoi `DataMember(Order)`, khong reorder/repurpose field cu; pipe envelopes mang `ProtocolVersion` va default ve `pipe/1` neu caller cu khong gui.
- `ToolPayloadValidator` da bo giant switch, chuyen sang explicit validator registry; `JsonUtil` check property top-level theo parser thay vi string search de tranh false positive khi field name nam trong value text.
- Pipe ingress hien qua `PipeRequestProcessor` de tach parse/auth/rate-limit/protocol handling khoi `NamedPipeServerStream`; `RequestRateLimiter` va cac service thoi gian quan trong da co `ISystemClock` seam.
- `BridgeCapabilities` hien expose ca `BridgeProtocolVersion=pipe/1` va `McpProtocolVersion` de session/capability surfaces bao dung protocol truth.
- `ToolExecutor` da tach core policy/error/finalize logic vao `BIM765T.Revit.Agent.Core`; wrapper trong Agent chi con UI-thread invocation + doc/view enrichment + journal record.
- Intelligence layer wave dau da co 2 companion tools:
  - `tool.get_guidance`: risk/cost/prerequisites/follow-ups/recovery tools
  - `context.get_delta_summary`: tom tat delta nong tu recent operations/events + goi y next tools
- Intelligence layer production slice hien co them:
  - repo-local knowledge packs trong `docs/agent/skills/`
  - curated tool graph overlay trong `docs/agent/skills/tool-intelligence/TOOL_GRAPH.overlay.json`
  - task templates human-readable trong `docs/agent/skills/tool-intelligence/TASK_TEMPLATES.md`
  - `data.extract_schedule_structured` cho structured schedule rows/columns
  - `review.smart_qc` v1 de aggregate model health, standards, naming, duplicates, va sheet hygiene thanh finding machine-readable
  - `family.xray` v1 de inspect types, nested families, parameters, formulas, reference planes, va connectors
  - `sheet.capture_intelligence` v1 de capture title block, viewport composition, placed schedules, sheet notes, layout map, va optional artifact references
  - `context.get_delta_summary` da duoc nang cap them add/remove/modify estimates, top categories, va discipline hints
- Quality gate runner-capable hien gom `BIM765T.Revit.Architecture.Tests` + script `tools/testing/check_coverage_thresholds.ps1`; threshold hien tai:
  - Contracts >= 55% (nâng từ 42%, 2026-03-21)
  - Copilot.Core >= 68%
  - Agent.Core >= 85%

## High-signal folders

- `src/BIM765T.Revit.Agent/Infrastructure/`
- `src/BIM765T.Revit.Agent/Services/Bridge/`
- `src/BIM765T.Revit.Agent/Services/Platform/`
- `src/BIM765T.Revit.Agent/Workflow/`
- `src/BIM765T.Revit.Contracts/`
- `src/BIM765T.Revit.Copilot.Core/`
- `packs/`
- `workspaces/default/`
- `catalog/`
- `tests/BIM765T.Revit.Contracts.Tests/`
- `tests/BIM765T.Revit.Agent.Core.Tests/`
- `tools/`

## Monorepo scale-up truth

- Reusable machine-readable truth mới nằm ở:
  - `packs/standards/*`
  - `packs/playbooks/*`
  - `packs/skills/*`
  - `packs/agents/*`
- `workspaces/default/workspace.json` là workspace entrypoint mặc định cho enabled packs + agent policy.
- `catalog/` giữ export machine-readable cho pack/playbook/standards/workspace.
- `scratch/` trong workspace là vùng tạm duy nhất mặc định gitignore; các state folders còn lại được giữ để review/handoff.

## Current runtime mental model

- `session.*`, `document.*`, `view.*`, `selection.*`
  - read/context acquisition
- `element.*`, `annotation.*`, `parameter.*`, `review.*`
  - domain operations
- `workflow.*`
  - orchestrated flows
- `task.*`, `context.*`, `artifact.*`, `memory.*`
  - copilot runtime surface
- inspector lane
  - `element.explain`
  - `element.graph`
  - `parameter.trace`
  - `view.usage`
  - `sheet.dependencies`

## Penetration replace jobs - stable workflow

- Blueprint chuẩn: `docs/agent/TASK_JOBDIRECTION_PENETRATION_REPLACE_CASE.md`
- Replace/externalize case chỉ nên claim “same workflow” khi có đủ:
  - source instances query được
  - target family rõ ràng
  - old -> new mapping deterministic
  - QC surface đủ cho family/type/position/size/axis
  - mutation vẫn đi qua bridge-safe flow
- Sau partial failure hoặc `RATE_LIMITED`, ưu tiên targeted rerun đúng source ids lỗi; không rerun full batch mặc định.
- Done definition cho penetration replace gồm cả review surface trong Revit: comments + schedule + save local file.

## Round cassette penetration openings - operational pattern

- Blueprint vận hành: `docs/agent/TASK_JOBDIRECTION_ROUND_PENETRATION_REVIEW_REPAIR_CASE.md`
- Current handoff: `ROUND_PENETRATION_TASK_HANDOFF.md`
- Repo-local scripts:
  - `tools/testing/run_round_penetration_cut_workflow.ps1`
  - `tools/round/repair_round_penetration_from_qc.ps1`
  - `tools/round/export_round_penetration_review_sheet.ps1`
- Artifact truth cho review/repair:
  - `artifacts/round-penetration-cut/*/qc.json`
  - `artifacts/round-penetration-cut/*/review-sheet.csv`
  - `artifacts/round-penetration-cut-repair/*/post-repair-qc.json`
- Stable rules:
  - không rerun full batch mặc định
  - `CUT_MISSING` / `AXIS_REVIEW` -> delete traced opening rồi rerun đúng source ids
  - `RESIDUAL_PLAN` -> manual review, không auto-nudge
  - `ORPHAN_INSTANCE` -> chỉ delete khi trace comment đã confirm an toàn

## Nested family type sync - stable pattern
Áp dụng cho case như `Penetration Alpha M` chứa nested `Penetration Alpha`:
- active doc phải là **family doc**
- project có thể mở song song để reload family về model
- đừng map per-parent-type bằng `ChangeTypeId(...)` đơn thuần
- pattern ổn định là:
  1. tìm nested instance đúng family con
  2. lấy element parameter `Type` của nested instance
  3. create/reuse một family type control parameter
  4. associate nested `Type` parameter vào family parameter đó
  5. `FamilyManager.CurrentType = parentType`
  6. `FamilyManager.Set(controlParameter, targetChildTypeId)` cho từng parent type
- nhận diện family doc không được phụ thuộc chỉ vào `OwnerFamily.Name`; fallback theo `Title` và `PathName`

## Round IFC / MiTek - current truth

- Round model replacement trong Revit đã hoàn tất: `95 -> 95`, QC model xanh, schedule review có đủ.
- Nhưng downstream export truth là track riêng; `ProjectAxis=OK` không đủ để claim export done.
- Spike quan trọng:
  - `artifacts/round-ifc-mitek-spike/20260317-064539`
  - `artifacts/round-ifc-localx-spike/20260317-075921`
- Team structure đã confirm 3 case local-X spike đều đúng trong MiTek.
- Rule ổn định hiện tại cho **Round only**:
  - dùng **`AXIS_X` canonical thật**
  - rotate instance theo hướng thật cho X/Y/Z
  - không dùng lại conclusion cũ kiểu `Z blocked`
  - cũng không promote batch alias `AXIS_XY__...` / `AXIS_XZ__...` thành production truth
- Export view phải bám active 3D sạch hoặc copy section box/orientation từ active 3D; nếu không IFC rất dễ kéo theo nhiều element rác.
- Current handoff truth cho Round phải đọc ở:
  - `ROUND_PENETRATION_TASK_HANDOFF.md`
  - `docs/agent/TASK_JOBDIRECTION_ROUND_EXPORT_IFC_MITEK_CASE.md`

## Copilot runtime foundation - stable so far

- `BIM765T.Revit.Copilot.Core` hiện là pure/non-Revit seam cho:
  - durable task-run store
  - context anchors and bundle resolution
  - artifact summary
  - capability lookup
  - task metrics
- Durable task state hiện ở `%APPDATA%\BIM765T.Revit.Agent\state\` dạng JSON files.
- Tool surface hiện có:
  - `task.*`
  - `context.*`
  - `artifact.summarize`
  - `memory.find_similar_runs`
  - `tool.find_by_capability`
  - `session.get_runtime_health`
  - `session.get_queue_state`
- `task.*` hiện wrap chủ yếu:
  - `workflow`
  - `fix_loop`

## Project init / context engine V1 - current truth

- Tool surface bootstrap mới:
  - `project.init_preview`
  - `project.init_apply`
  - `project.get_manifest`
  - `project.get_context_bundle`
- V1 chỉ làm **bootstrap + grounding curated**:
  - discover `.rvt/.rfa/.pdf`
  - chọn primary `.rvt`
  - tạo workspace curated files:
    - `project.context.json`
    - `reports/project-init.manifest.json`
    - `reports/project-init.summary.md`
    - `memory/project-brief.md`
    - optional `reports/project-init.primary-model.json`
- Thứ tự context layer được chốt:
  - `core safety > firm doctrine > project overlay > session/run memory`
- WorkerHost hiện expose HTTP wrappers cho web:
  - `POST /api/projects/init/preview`
  - `POST /api/projects/init/apply`
  - `GET /api/projects/{workspaceId}/manifest`
  - `GET /api/projects/{workspaceId}/context`
- Web V1 trong `BIM765T-Revit-WebPage` đã có 2 surface:
  - `Project Init`
  - `Context Overview`
- V1 **không** deep-scan toàn project, không ingest full PDF text, không Ask Project tự do.

## Family authoring benchmark - stable pattern

- Benchmark deterministic hiện tại cho lane `family.*` là `ME_Benchmark_Parametric_ServiceBox_v1`.
- Doc/current truth:
  - `docs/agent/FAMILY_AUTHORING_BENCHMARK_V1.md`
  - `docs/agent/playbooks/family_benchmark_servicebox.v1.json`
  - `docs/agent/presets/family_benchmark_servicebox.v1.json`
  - `tools/testing/run_family_authoring_benchmark.ps1`
- Mục tiêu của benchmark này là harden backend theo thứ tự:
  - create document
  - parameters/formulas
  - reference planes
  - subcategories
  - solid/void/accessory forms
  - visibility/material
  - type catalog
  - xray/list_geometry/save verification
- Rule ổn định:
  - V1 ưu tiên deterministic và audit được, không claim full flex geometry production-grade.
  - Alignment / connector / blend/revolution stress là stretch scenario tách riêng, không được để làm nhiễu benchmark core pass/fail.
  - Script runner mặc định chỉ tạo `plan.json`; phải truyền `-Execute` mới mutate Revit/backend live.
  - Khi runtime hiện hành chỉ expose private kernel pipe, benchmark nên chạy với `artifacts\\bridgehotfixexe\\BIM765T.Revit.Bridge.exe`; đừng giả định legacy JSON pipe hay public WorkerHost control plane luôn reachable.
  - Với benchmark batched dài, runner hiện normalize `ResolvedContext.ActiveDocEpoch = 0` trước execute để tránh false `CONTEXT_MISMATCH` do `DocumentChanged` race; document/view/selection guards vẫn là source of truth chính.

## 765T Worker v1 - current stable product slice

- Front door trong Revit docked pane hien tai la mot shell XAML/MVVM gom 5 section: Worker Home, Queue / Progress, Actions / Approvals, Evidence, Expert Lab.
- UI va MCP dung chung mot orchestration surface `worker.*`:
  - `worker.message`
  - `worker.get_session`
  - `worker.list_sessions`
  - `worker.end_session`
  - `worker.set_persona`
  - `worker.list_personas`
  - `worker.get_context`
- Worker v1 la rule-first:
  - co `ConversationManager`, `MissionCoordinator`, `IntentClassifier`, `WorkerReasoningEngine`
  - khong can LLM de van usable cho internal pilot
- Memory v1 chi gom:
  - `SessionMemoryStore` trong RAM, co LRU eviction
  - `EpisodicMemoryStore` persist JSON duoi `%APPDATA%\\BIM765T.Revit.Agent\\state\\worker\\episodes\\`
- Worker response surface phai cho user thay ro:
  - mission state + stage + progress
  - context summary + queue summary
  - pending approval
  - tool result cards + evidence
  - suggested next actions + recovery hints
- Worker brain khong duoc bypass kernel:
  - mutation request -> preview truoc
  - approval card chi la UI shell
  - execute van di qua approval token + expected context cua runtime
- Persona hien tai la config nhe:
  - `revit_worker`
  - `qa_reviewer`
  - `helper`
  Persona chi doi tone/expertise/guardrails wording, khong doi policy an toan.
- Worker shell hien tiep tuc rule-first, nhung live catalog / queue state / memory-friendly context retrieval da duoc surface ro hon qua worker response va manifest enrich.

## Agent memory rules

- `README.md` dùng cho boot nhanh.
- `PROJECT_MEMORY.md` giữ current stable truth.
- `LESSONS_LEARNED.md` giữ problem -> root cause -> fix -> prevention.
- `BUILD_LOG.md` giữ lịch sử triển khai; coi đây là chronology, không phải current truth tuyệt đối.
- `.assistant/context/`, `.assistant/memory/`, `.assistant/runs/` là runtime memory, không phải curated source mặc định.

## WorkerHost v2 hardening - stable so far

- `BIM765T.Revit.WorkerHost` da support Windows Service mode tren Windows qua `UseWindowsService`.
- Public gRPC named pipe cua WorkerHost co ACL mode config (`authenticated_users`, `builtin_users`, `current_user`) va duoc set truoc khi open listener.
- SQLite event store co replay fallback: neu snapshot mat/corrupt thi rebuild lai tu `events` roi upsert snapshot moi.
- Background `MemoryOutboxProjectorService` projection cac mission event quan trong vao `memory_projection`, giu SQLite la durable truth ngay ca khi Qdrant unavailable.
- Outbox runtime da harden them:
  - retry/backoff bounded
  - dead-letter sau khi qua max attempts
  - reclaim stale `processing` lease de recover sau crash/projector restart
- Legacy migration da co `--dry-run` report de inventory truoc khi import that.
- Script operational chinh cho Wave 3:
  - `tools/infra/install_workerhost_service.ps1`
  - `tools/infra/uninstall_workerhost_service.ps1`
  - `tools/infra/install_qdrant_startup_task.ps1`
  - `tools/infra/uninstall_qdrant_startup_task.ps1`
  - `tools/infra/migrate_workerhost_legacy_state.ps1`
- Script/package operational cho Wave 4:
  - `tools/infra/install_enterprise_companion.ps1`
  - `tools/infra/package_revit_bridge_build.ps1` gio package them companion scripts/manifest va optional `qdrant.exe`

## Code architecture refactoring — stable since 2026-03-21

- **ToolPayloadValidator** da duoc tach thanh 7 partial class files theo domain:
  - `ToolPayloadValidator.cs` (Core): Validate, ValidateObject, CreateValidators, Register, shared helpers
  - `ToolPayloadValidator.Session.cs`: TextNote, ElementQuery, Review, SmartQc, FamilyXray, SheetCapture, Schedule, TaskContext
  - `ToolPayloadValidator.Worker.cs`: Worker*, FixLoop*, ExternalTaskIntake
  - `ToolPayloadValidator.CopilotTask.cs`: Task*, TaskQueue*, ConnectorCallback, HotState, Context*, Artifact, Memory, ToolCapability, ToolGuidance
  - `ToolPayloadValidator.Mutation.cs`: SetParameters, Delete/Move/Rotate, PlaceFamilyInstance, SaveAs, View*, Filter*, ElementType
  - `ToolPayloadValidator.Penetration.cs`: PenetrationInventory, RoundShadow*, RoundPenetrationCut*, RoundPenetrationCommon
  - `ToolPayloadValidator.FamilyAuthoring.cs`: Family*, ScheduleCreate, Export (IFC/DWG/PDF), HullDryRun, Script*
- Convention nay co file `src/BIM765T.Revit.Contracts/Validation/CONVENTIONS.md`: codes=English UPPER_SNAKE_CASE, messages=Vietnamese, exceptions=English.
- **CopilotTaskDtos.cs** da duoc tach thanh 6 sub-domain files:
  - `CopilotTaskPlanDtos.cs` — Task planning & execution lifecycle
  - `CopilotTaskQueueDtos.cs` — Task queue management
  - `CopilotTaskMemoryDtos.cs` — Memory, context & artifact operations
  - `CopilotTaskStateDtos.cs` — State, checkpoints, recovery & run
  - `CopilotTaskContextDtos.cs` — Context bundle, artifact, guidance responses
  - `CopilotTaskPlaybookDtos.cs` — Playbook definitions
  - File goc `CopilotTaskDtos.cs` da bi xoa.
- **Service Bundles** da duoc tao de giam constructor parameter count:
  - `ServiceBundles.cs` trong `Services/Bridge/` chua 5 bundle classes:
    - `PlatformBundle` (4 services: platform, mutation, viewAutomation, fileLifecycle)
    - `InspectionBundle` (11 services: review, typeCatalog, audit, QC, xray, intelligence, schedule, spatial)
    - `HullBundle` (3 services: hullCollector, hullPlanner, hullValidator)
    - `WorkflowBundle` (6 services: workflowRuntime, fixLoop, deliveryOps, templateSheet, sheetView, dataExport)
    - `CopilotBundle` (2 services: copilotTasks, worker)
  - `ToolModuleContext` va `ToolRegistry` constructor nhan 5 bundles thay vi 26+ params
  - Properties cua `ToolModuleContext` giu nguyen ten va type (backward compatible voi tat ca tool modules)
- **AgentHost.Initialize()** da extract duplicate document-key lambda thanh `ResolveDocumentKey()` static method.
- **Directory.Build.props** da chuyen `TreatWarningsAsErrors` va `EnforceCodeStyleInBuild` sang condition-based:
  - `true` khi CI=true HOAC Configuration=Release
  - `false` cho local Debug builds
- **CI pipeline** (`.github/workflows/ci.yml`) da them WorkerHost + WorkerHost.Tests vao build/test steps.
- **WorkerHost.csproj** co comment giai thich tai sao `ImplicitUsings=enable` override global `disable` tu Directory.Build.props.

## Dream Team agents — stable since 2026-03-21

- 5 agent chuyen biet (+ em la dieu phoi chinh) trong `.assistant/agents/`:
  - `bim-manager-pro.md` — BIM Manager Pro: strategy, domain validation, standards, SOW (sonnet)
  - `revit-api-developer.md` — Revit API Developer: implementation, C# coding, Revit API patterns, script tools (sonnet)
  - `revit-ui-engineer.md` — Revit UI Engineer: WPF/XAML, ViewModel, MVVM, UI↔backend connection (sonnet)
  - `research-frontend-organizer.md` — Research & Frontend Organizer: UX research, feature discovery, BIM workflow → frontend transformation (sonnet) [nếu có]
  - `marketing-repo-manager.md` — Marketing & Repo Manager: web dev, repo management, releases, documentation (sonnet)
- Agent router config: `.assistant/config/agent-router.json` — routing rules, capability discovery
- Agent memory architecture: `.assistant/config/agent-memory-architecture.json` — per-agent vector DB, soul, episodic memory
- Soul files: `.assistant/souls/` — persistent identity/personality per agent
- Personas: `docs/agent/personas/` — 5 specialized persona JSON files
- Dream Team docs: `docs/archive/legacy-agent/DREAM_TEAM.md`, `docs/archive/legacy-agent/AGENT_MEMORY_SOUL.md`

## Project Brain deep scan Wave 2 - current truth

- Tool surface moi:
  - `project.deep_scan`
  - `project.get_deep_scan`
- HTTP wrappers moi trong WorkerHost:
  - `POST /api/projects/{workspaceId}/deep-scan`
  - `GET /api/projects/{workspaceId}/deep-scan`
- Deep scan Wave 2 hien la read-only orchestration cho primary model, reusing existing kernel-safe tools de lay:
  - model health
  - links status
  - workset health
  - sheet inventory / sheet summary / sheet intelligence
  - structured schedules
  - smart QC findings
- Curated artifacts duoc persist o workspace:
  - `reports/project-brain.deep-scan.json`
  - `reports/project-brain.deep-scan.summary.md`
- `project.context.json` va `project.get_context_bundle` hien duoc enrich them voi:
  - deep scan status
  - summary
  - finding count
  - report path
  - top refs
- Web workspace `BIM765T-Revit-WebPage` hien da co tab `Project Brain Deep Scan` de chay scan, xem summary, stats, strengths, weaknesses, pending unknowns, va document slices.
- Muc tieu gan hien tai:
  - them PDF/standards ingestion grounded
  - them `Ask Project` grounded chat
  - them delta rescan / compare scans
- Non-goals cua Wave 2:
  - khong mutate model
  - khong OCR/full-PDF ingestion
  - khong cho free-form answer neu khong co curated evidence/context refs
