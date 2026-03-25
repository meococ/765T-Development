# Build Log

> Append-only log ghi lại mỗi task build/enhance/fix.
> Agent đọc file này để: (1) tránh lặp lỗi cũ, (2) hiểu pattern đã dùng, (3) build workflow/skill tốt hơn.
> Khi 1 entry trở thành kiến thức nền → promote sang `LESSONS_LEARNED.md` hoặc `PROJECT_MEMORY.md`.

---

## Entry Format

```
### YYYY-MM-DD — [Task Title]
**Module**: affected module(s)
**Files touched**: list
**What was built**: summary
**Problems hit & fixes**:
- Problem → Fix → Why it works
**Revit API gotchas**:
- API surface gotcha and correct usage
**Pattern decisions**:
- Design choice → Why → Alternative considered
**Reusable for**: skill/workflow này có thể áp dụng cho...
```

---

## 2026-03-21 — Code Architecture Refactoring: File Splits, Service Bundles, CI Hardening

**Module**: Contracts (Validation, Platform), Agent (Services/Bridge, Infrastructure), CI pipeline

**Files touched**:
- DEL: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskDtos.cs` (1665 lines → split into 6 files)
- NEW: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskPlanDtos.cs` (193 lines)
- NEW: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskQueueDtos.cs` (262 lines)
- NEW: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskMemoryDtos.cs` (244 lines)
- NEW: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskStateDtos.cs` (551 lines)
- NEW: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskContextDtos.cs` (260 lines)
- NEW: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskPlaybookDtos.cs` (180 lines)
- MOD: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.cs` (1775 → 198 lines, partial class core)
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.Session.cs` (215 lines)
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.Worker.cs` (128 lines)
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.CopilotTask.cs` (280 lines)
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.Mutation.cs` (284 lines)
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.Penetration.cs` (392 lines)
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.FamilyAuthoring.cs` (336 lines)
- NEW: `src/BIM765T.Revit.Contracts/Validation/CONVENTIONS.md`
- NEW: `src/BIM765T.Revit.Agent/Services/Bridge/ServiceBundles.cs` (139 lines, 5 bundle classes)
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolModuleContext.cs` (26 params → 5 bundles)
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolRegistry.cs` (26 params → 5 bundles)
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/AgentHost.cs` (extracted ResolveDocumentKey, creates bundles)
- MOD: `Directory.Build.props` (condition-based TreatWarningsAsErrors + EnforceCodeStyleInBuild)
- MOD: `.github/workflows/ci.yml` (added WorkerHost + WorkerHost.Tests)
- MOD: `tools/check_coverage_thresholds.ps1` (Contracts threshold 42% → 55%)
- MOD: `src/BIM765T.Revit.WorkerHost/BIM765T.Revit.WorkerHost.csproj` (ImplicitUsings comment)

**What was built**:
11-item code architecture refactoring covering: god file splits (validator 87KB → 7 partials, DTOs 47KB → 6 files), service bundle DI pattern (26 params → 5 bundles), CI pipeline hardening (WorkerHost in CI, conditional warnings-as-errors, raised coverage gates), and coding convention documentation.

**Problems hit & fixes**:

1. **Agent.Core.Tests build failure after ViewModel refactoring**
   - Problem: `ExpertPackCatalogItem` not found because test project uses `<Compile Include>` links, not ProjectReference.
   - Fix: Added `<Compile Include>` for `ExpertLabCatalogModels.cs` in test .csproj.
   - Lesson: Any file extraction in Agent project must be mirrored in test project's Compile Include list.

2. **Stash/pop creates ghost duplicate types**
   - Problem: `git stash` + test + `git stash pop` showed false duplicate DataContract errors because untracked split files (CopilotTaskStateDtos.cs etc.) survived the stash.
   - Fix: Recognized as test artifact, not real error. Current workspace build confirmed clean.
   - Lesson: Untracked files are NOT stashed — use worktree or clean branch for pristine baseline testing.

**Pattern decisions**:

- **Partial class for validators** — Keeps CreateValidators() registry in Core file, domain methods in separate files. Compiler merges them. Better than interface extraction (no polymorphism needed).
- **5 domain bundles** — Grouped by subsystem responsibility, not by technical layer. Bundle names match business domains (Platform, Inspection, Hull, Workflow, Copilot).
- **Condition-based MSBuild** — Single source of truth for build policies. CI flag `$(CI)` OR Release config triggers stricter rules. Local Debug stays permissive.
- **DTOs split by lifecycle** — Plan/Queue/Memory/State/Context/Playbook reflects natural CQRS-like separation. Types that are often edited together stay in the same file.

**Reusable for**:
- Pattern tach file lon: partial class cho validator/handler/pipeline; sub-domain files cho DTOs. Threshold: >400 lines or >15 methods.
- Pattern bundle DI: group >5 related services into immutable bundle class. Pass bundle to consumers.
- Pattern CI expansion: any net8.0 project can be added to CI. Only net48 (Revit) stays local-only.

---

## 2026-03-16 — Smart View Template & Sheet Analysis (6 tools + 2 enhanced)

**Module**: SheetViewToolModule, TemplateSheetAnalysisService

**Files touched**:
- NEW: `src/BIM765T.Revit.Contracts/Platform/TemplateSheetAnalysisDtos.cs`
- NEW: `src/BIM765T.Revit.Agent/Services/Platform/TemplateSheetAnalysisService.cs`
- MOD: `src/BIM765T.Revit.Contracts/Platform/SheetViewManagementDtos.cs` (enhanced ViewTemplateItem, SheetItem)
- MOD: `src/BIM765T.Revit.Contracts/Platform/AuditDtos.cs` (added Score, MaxPoints to ComplianceSectionResult)
- MOD: `src/BIM765T.Revit.Contracts/Bridge/ToolNames.cs` (6 new constants)
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/SheetViewManagementService.cs` (enhanced ListSheets, ListViewTemplates)
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/SheetViewToolModule.cs` (6 new read tools registered)
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolModuleContext.cs` (added TemplateSheetAnalysisService)
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolRegistry.cs` (wired new service)
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/AgentHost.cs` (instantiated new service)

**What was built**:
6 read-only analysis tools: `audit.template_health`, `audit.sheet_organization`, `audit.template_sheet_map`, `view.template_inspect`, `sheet.group_summary`, `audit.view_template_compliance`. Plus enhanced `view.list_templates` and `sheet.list_all` with richer metadata.

**Problems hit & fixes**:

1. **Duplicate class `ComplianceSectionResult`**
   - Problem: Tạo class `ComplianceSectionResult` trong `TemplateSheetAnalysisDtos.cs` nhưng nó đã tồn tại trong `AuditDtos.cs` → CS0101 duplicate definition.
   - Fix: Xóa definition trong file mới, thêm `Score` + `MaxPoints` fields vào class cũ ở `AuditDtos.cs`.
   - Lesson: **Trước khi tạo DTO mới, LUÔN grep toàn bộ Contracts project** xem class name đã tồn tại chưa. Codebase có 8+ DTO files, dễ trùng.

2. **`ViewDiscipline.Undefined` không tồn tại trong Revit 2024 API**
   - Problem: Viết `disc != ViewDiscipline.Undefined` nhưng Revit 2024 enum `ViewDiscipline` không có member `Undefined` → CS0117.
   - Fix: Bỏ check `Undefined`, dùng thẳng `disc.ToString()`. Wrap trong try-catch vì không phải mọi view type đều expose Discipline.
   - Lesson: **Revit API enum members thay đổi giữa versions**. Luôn wrap access vào try-catch, không assume enum value tồn tại.

**Revit API gotchas**:

- `View.Discipline` — Trả về `ViewDiscipline` enum. Revit 2024 KHÔNG có `Undefined` member. Dùng `disc.ToString()` an toàn hơn. Một số view types (Schedule, Legend) có thể throw khi access property này.
- `View.GetTemplateParameterIds()` — Trả về `ICollection<ElementId>` của parameter IDs mà template controls. Cần cast `(BuiltInParameter)(int)paramId.Value` để resolve tên, nhưng không phải lúc nào cũng có mapping → fallback `ParamId:N`.
- `View.GetFilterOverrides(filterId)` — Trả về `OverrideGraphicSettings`. Access `.ProjectionLineColor`, `.CutLineColor`, `.Transparency`, `.Halftone` để build human-readable summary.
- `View.GetFilterVisibility(filterId)` — bool, có thể throw nếu filter không còn valid.
- `View.GetFilters()` — Trả về `IList<ElementId>` filter IDs. An toàn hơn `.GetFilterCount()`.
- `FilteredElementCollector(doc, viewId)` — View-scoped collector, hữu ích cho title block per sheet. CÓ THỂ throw nếu view bị corrupt → wrap try-catch.
- `ViewSheet.GetAllViewports()` — Trả về `IList<ElementId>`. Luôn có sẵn, rất nhẹ — dùng để count viewport mà không cần resolve detail.

**Pattern decisions**:

- **Levenshtein cho duplicate detection** — So sánh tên template, threshold 85%. Yêu cầu cùng ViewType mới so sánh. Đủ bắt "Plan - LOD400" vs "Plan - LOD400 Copy 1" mà không false positive cross-discipline.
- **Auto sheet grouping by prefix** — Split bằng `-` và `_`, bỏ trailing numeric segments. VD: `SR-QQ-T-300` → `SR-QQ-T`. Minimum 2 sheets/group.
- **Scoring A-F** — Template health grade dựa vào unused%, duplicates, naming. Sheet organization grade dựa vào empty%, heavy%, title block coverage. Master compliance 0-100 weighted (Template 30pts + Sheet 30pts + Chain 20pts + Naming 20pts).
- **ReviewReport trên mọi response** — Tất cả analysis tools đều trả ReviewReport với Issues[] để AI chain tiếp.

**Reusable for**:
- Skill build: "model health review" workflow có thể chain: `audit.view_template_compliance` → `audit.template_health` → `view.template_inspect` (cho top offenders) → generate report.
- Pattern thêm read-only tool mới: DTO → ToolNames → Service method → Register in module → Wire in Context/Registry/AgentHost → Build → Test. Luôn theo thứ tự này.
- Levenshtein similarity helper có thể reuse cho family naming audit, parameter naming audit.

---

## Wiring Checklist (reusable cho mọi new service)

Khi thêm 1 service mới vào Agent:

1. [ ] Tạo DTOs trong `Contracts/Platform/` — grep trước xem class name đã tồn tại chưa
2. [ ] Thêm ToolNames constants trong `Contracts/Bridge/ToolNames.cs`
3. [ ] Tạo service class trong `Agent/Services/Platform/`
4. [ ] Thêm property vào `ToolModuleContext.cs` (constructor + property)
5. [ ] Thêm parameter vào `ToolRegistry.cs` constructor, truyền vào ToolModuleContext
6. [ ] Instantiate trong `AgentHost.cs` Initialize(), truyền vào ToolRegistry constructor
7. [ ] Register tools trong module (`SheetViewToolModule.cs` hoặc module phù hợp)
8. [ ] `dotnet build` — fix errors
9. [ ] `dotnet test` — verify existing tests pass
10. [ ] `check_bridge_health.ps1` — verify tool count tăng đúng


---

## 2026-03-16 - Round externalization from nested family to project-level Round_Project (95/95 aligned)

**Module**: PenetrationWorkflowToolModule, PenetrationShadowService, GenericMutationServices, round externalization PowerShell workflow

**Files touched**:
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/PenetrationShadowService.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/GenericMutationServices.cs`
- MOD: `tools/build_round_project_wrappers.ps1`
- MOD: `tools/externalize_round_from_plan.ps1`
- MOD: `tools/reconcile_round_wrapper_qc.ps1`
- MOD: `ROUND_TASK_HANDOFF.md`
- Artifacts: `artifacts/round-externalization-*`, `artifacts/round-wrapper-build/*`, `artifacts/round-wrapper-qc/*`, `artifacts/round-final-status/*`

**What was built**:
- Planned and externalized `Round` instances out of nested `Penetration Alpha` into project-level `Round_Project` families.
- Generated size-specific wrapper types, loaded them into the project, placed 95 new instances, paired old/new instances exactly, and QC-checked position, size, geometry axis, and project axis.
- Final result reached `95/95` for position, size, geometry-axis, and project-axis alignment.
- Final wrapper material strategy was changed to use `Mii_Penetration` explicitly because source `Round` is void and has no reusable explicit material.

**Problems hit & fixes**:
- Problem: Initial wrapper strategy cloned the old curve-based `Round` family and still inherited host/face behavior, causing repeated placement failures and axis drift.
  - Fix: Switched to clean project-level `Round_Project` wrappers with size-specific variants and native geometry placement strategy.
  - Why it works: Project-level wrapper geometry is controllable and no longer depends on nested host transforms from `Penetration Alpha`.
- Problem: New instances looked "close" but failed exact QC because placement used transform origin, not the real opening anchor.
  - Fix: Changed planning/externalization to anchor by the real `LocationCurve` midpoint of the old `Round` instance.
  - Why it works: Midpoint reflects the actual center of the opening in model space and is stable across nested transforms.
- Problem: `OneLevelBased` placement produced correct XY but wrong Z on some instances.
  - Fix: After placement, normalized `Elevation from Level` and only then used move fallback.
  - Why it works: Revit treats insertion point Z and `Elevation from Level` as coupled constraints; both must be resolved together.
- Problem: Wrapper type naming drift (`mode` vs size-specific type name) caused wrong axis family/type selection.
  - Fix: Passed wrapper family/type naming contract end-to-end through plan -> build -> execute.
  - Why it works: All stages now resolve the same mode/type pair instead of falling back to defaults.
- Problem: Long mutation loops were hard to trust if only counts passed.
  - Fix: Added exact pair reconciliation and encoded QC result into `Comments` with pair id / old element id / mode / P / SZ / AX / PS flags.
  - Why it works: Review schedule becomes a deterministic acceptance surface, not just a rough audit.

**Revit API gotchas**:
- `NewFamilyInstance(point, symbol, level, structuralType)` for `OneLevelBased` families may still require post-normalizing `Elevation from Level` to hit exact world Z.
- Bounding-box based QC is unreliable if family architecture inflates extents; pair QC should prefer exact type, anchor point, and expected geometry logic.
- Nested family transform chains (`Round -> Penetration Alpha -> outer family/project`) make raw transform origin a poor proxy for the real opening center.
- Void source families may not expose reusable explicit material through geometry/subcategory APIs; wrapper material may need an explicit project-standard override.

**Pattern decisions**:
- **QC first, then declare success** -> counts/schedules were not enough; success required exact pair mapping and four gates: position, size, geometry axis, project axis.
- **Exact pair reconciliation over nearest-neighbor matching** -> paired old/new through deterministic chain (`old Round -> wrapper -> new child`) so review stayed stable after reruns.
- **Project-level wrapper over direct reuse of old Round** -> slower up front, but prevents recurrence of the same nested transform problem.
- **Material policy explicit, not inherited** -> because source family is void, final wrapper uses `Mii_Penetration` as a project standard instead of guessing inheritance.
- **Artifact-driven acceptance** -> each phase had JSON/CSV evidence before moving to the next phase.

**Reusable for**:
- Any "extract from nested family to project-level clean family" workflow.
- IFC-cleanup / hostless replacement workflows that need exact old-vs-new reconciliation.
- Future pipe/duct/wall penetration automation where detection, placement, and QC must be chained safely.
- Any long-running mutation task that benefits from pair-based schedule review instead of only global counts.


---

## 2026-03-16 - Claude review adjudication + phase-1 safety hardening

**Module**: PipeServerHostedService, ApprovalService, FileLifecycleService, MutationFileAndDomainToolModule

**Files touched**:
- MOD: `src/BIM765T.Revit.Agent/Config/AgentSettings.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/AgentHost.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Bridge/PipeServerHostedService.cs`
- NEW: `src/BIM765T.Revit.Agent/Infrastructure/Bridge/RequestRateLimiter.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/MutationFileAndDomainToolModule.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/GenericMutationServices.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/PlatformServices.cs`
- NEW: `src/BIM765T.Revit.Agent/Services/Platform/ApprovalTokenStore.cs`
- MOD: `src/BIM765T.Revit.Contracts/Common/StatusCodes.cs`
- MOD: `src/BIM765T.Revit.Contracts/Platform/FileLifecycleDtos.cs`
- MOD: `src/BIM765T.Revit.Contracts/Platform/MutationDtos.cs`

**What was reviewed (truth-first)**:
- Claude review was directionally right on **safety gaps** and **workflow intelligence still being hardcoded**, but not every proposed fix was valid as written.
- Confirmed in codebase before fixing:
  - no request-level rate limiter
  - approval tokens were in-memory only
  - `element.delete_safe` did not preflight dependent-delete blast radius
  - `worksharing.synchronize_with_central` defaulted to `RelinquishOptions(true)` (too aggressive)
- Important nuance / correction:
  - `SynchronizeWithCentral()` is **not** something we should pretend is safely wrapped by a normal model `TransactionGroup`; the better fix is conservative preflight + safer payload semantics + explicit relinquish policy.

**What was built**:
- Added **request rate limiting** at pipe ingress, before requests hit the Revit UI queue.
- Added **approval token persistence to disk** so preview/execute chains can survive agent restarts within TTL.
- Hardened **sync semantics** so relinquish-all is no longer the hidden default; it must be requested explicitly.
- Added **delete dependency preflight** so delete now previews exact collateral impact and blocks execute unless caller explicitly allows dependent deletes.

**Problems hit & fixes**:
- Problem: Flooding the pipe with requests could still queue too much work onto the Revit UI thread.
  - Fix: Injected `RequestRateLimiter` into `PipeServerHostedService` and reject over-limit calls early with `RATE_LIMITED`.
  - Why it works: denial happens before enqueue + before `ExternalEvent`, so UI thread stays protected.
- Problem: Approval tokens vanished on restart because they only lived in `ConcurrentDictionary`.
  - Fix: Persisted token records to `%APPDATA%\BIM765T.Revit.Agent\pending-approvals.dat` and reload valid tokens at startup.
  - Why it works: preview/execute context remains bound by fingerprint/caller/session/TTL, but does not die just because the add-in restarted.
- Problem: `delete_safe` could cascade into unexpected dependent deletions without explicit user intent.
  - Fix: Dry-run now simulates delete in a rollback transaction, computes exact delete impact, and execute is blocked with `DELETE_DEPENDENCY_BLOCKED` unless `AllowDependentDeletes=true`.
  - Why it works: delete preview now surfaces the real blast radius from Revit itself, not a guess.
- Problem: sync default was overly aggressive (`RelinquishOptions(true)`).
  - Fix: Added explicit payload flags (`RelinquishAllAfterSync`, `CompactCentral`) and changed default behavior to conservative relinquish semantics.
  - Why it works: high-risk worksharing behavior must be opt-in, not hidden in default code.

**Revit API gotchas**:
- `doc.Delete(...)` inside a rollback transaction is a practical way to preview exact delete impact, including implicit dependents, without leaving model residue.
- `SynchronizeWithCentral()` is a worksharing lifecycle operation, not a normal mutable modeling transaction; treat it as a guarded file/worksharing action with strict preconditions, not as a transactional model edit you can rollback like geometry.
- Safer worksharing automation comes more from **preflight + explicit options + approval discipline** than from pretending sync behaves like a normal mutation batch.

**Pattern decisions**:
- **Ingress protection over downstream cleanup** -> rate-limit at pipe ingress, not after queueing.
- **Persist narrow state, not everything** -> only preview approval tokens are persisted, and TTL/caller/session bindings still gate replay.
- **Exact delete preview over heuristic dependency guessing** -> use rollback delete simulation instead of trying to infer dependency trees manually.
- **Truth over cosmetic fixes** -> rejected the idea of "wrap sync in TransactionGroup" because that would sound safer than it really is.

**Reusable for**:
- Any future high-risk tool (`export.*`, `family.load_safe`, `print.*`) should follow the same ingress-rate-limit + explicit-preflight + conservative-defaults pattern.
- Delete-impact preview pattern can be reused for purge/cleanup/replace workflows.
- Token persistence pattern applies to multi-step workflows where restart-resilience matters more than ephemeral session convenience.

---

## 2026-03-17 - Phase 2 supervised fix loop + Phase 3 delivery ops wave 1

**Module**: FixLoopService, DeliveryOpsService, ToolRegistry wiring, contracts/playbooks/presets

**Files touched**:
- NEW: `src/BIM765T.Revit.Contracts/Platform/FixLoopDtos.cs`
- NEW: `src/BIM765T.Revit.Contracts/Platform/DeliveryOpsDtos.cs`
- MOD: `src/BIM765T.Revit.Contracts/Bridge/ToolNames.cs`
- MOD: `src/BIM765T.Revit.Contracts/Common/StatusCodes.cs`
- MOD: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.cs`
- NEW: `src/BIM765T.Revit.Agent/Services/Platform/FixLoopService.cs`
- NEW: `src/BIM765T.Revit.Agent/Services/Platform/DeliveryOpsService.cs`
- NEW: `src/BIM765T.Revit.Agent/Services/Bridge/FixLoopToolModule.cs`
- NEW: `src/BIM765T.Revit.Agent/Services/Bridge/DeliveryOpsToolModule.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolModuleContext.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolRegistry.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/AgentHost.cs`
- NEW: `docs/agent/playbooks/default.fix_loop_v1.json`
- NEW: `docs/agent/presets/delivery_ops.json`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/DtoSerializationTests.cs`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/ToolNamesTests.cs`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/ToolPayloadValidatorExtendedTests.cs`

**What was built**:
- Added a parallel **supervised fix loop** layer without replacing the 5 stable built-in workflows.
- Added 4 fix-loop tools:
  - `review.fix_candidates`
  - `workflow.fix_loop_plan`
  - `workflow.fix_loop_apply`
  - `workflow.fix_loop_verify`
- Shipped 3 v1 scenarios:
  - `parameter_hygiene`
  - `safe_cleanup`
  - `view_template_compliance_assist`
- Added Phase 3 delivery ops tools:
  - `family.list_library_roots`
  - `family.load_safe`
  - `schedule.preview_create`
  - `schedule.create_safe`
  - `export.list_presets`
  - `export.ifc_safe`
  - `export.dwg_safe`
  - `sheet.print_pdf_safe`
  - `storage.validate_output_target`
- Added project-governed playbook/preset resolution:
  - `%APPDATA%\\BIM765T.Revit.Agent\\playbooks\\*.json`
  - `%APPDATA%\\BIM765T.Revit.Agent\\presets\\delivery_ops.json`
  - repo fallbacks under `docs/agent/...`
- Build result:
  - `dotnet build BIM765T.Revit.Agent.sln -c Release` -> pass
  - `dotnet test ...Contracts.Tests -c Release --no-build` -> `99/99` pass

**Problems hit & fixes**:
- Problem: Replacing existing workflows would create too much regression risk.
  - Fix: Added fix-loop planning/apply/verify as a parallel orchestration layer and left the legacy workflows intact.
  - Why it works: stable production flows keep working while new supervised intelligence can iterate independently.
- Problem: Generic schedule creation needs field validation, but `SchedulableField` data depends on a real schedule context.
  - Fix: Created a temporary model schedule inside a rollback transaction to discover valid schedulable fields safely.
  - Why it works: Revit gives the real field surface for the target category without leaving schedule residue in the model.
- Problem: Export/load/print tools become dangerous if they accept arbitrary paths or free-form preset payloads.
  - Fix: Added allowlisted roots, named preset catalogs, path containment validation, and explicit overwrite policy handling.
  - Why it works: every file operation is bound to a reviewed root/preset instead of arbitrary filesystem access.
- Problem: Live verification can be overstated after build/install if Revit is not actually serving the bridge.
  - Fix: Verified with `check_bridge_health.ps1` and recorded the truth: current bridge status was `BRIDGE_UNAVAILABLE` at verification time.
  - Why it works: delivery/fix-loop code can be claimed built and installed, but not live-verified until Revit is actually online.

**Revit API gotchas**:
- `ViewSchedule.CreateSchedule(...)` plus `ScheduleDefinition.GetSchedulableFields()` is the reliable way to inspect valid schedule fields for a category. If used for preview, do it inside a transaction and rollback.
- `DWGExportOptions.GetPredefinedOptions(doc, name)` can resolve native DWG setups, but callers still need an allowlisted output root and an explicit preset name.
- `Document.Export(folder, viewIdsOrSheetIds, PDFExportOptions)` is safer than exposing raw `PrintManager` for batch PDF in this wave.
- Opening a family file just to inspect type names is acceptable for preview, but runtime should still resolve against allowlisted library roots only.

**Pattern decisions**:
- **Parallel intelligence layer over workflow rewrite** -> safer rollout, easier rollback, lower regression risk.
- **Playbook JSON + repo fallback** -> project-specific behavior can change without recompiling, but code still has safe defaults.
- **Preset-governed delivery ops** -> better than raw payload freedom for export/print/load because the platform remains policy-driven.
- **Closed-loop evidence bundle** -> every fix-loop run stores issues, proposals, execution, expected delta, actual delta, residual issues, and blocked reasons.
- **Truth-first live status** -> build/install success is not the same as bridge-online success.

**Reusable for**:
- Future auto-fix chains such as naming remediation, standards enforcement, or parameter normalization.
- Any new file-producing workflow that needs root allowlists and named presets.
- Any scenario where AI should propose action sets but still stay inside a deterministic preview/approval/verify loop.

---

## 2026-03-17 - Phase 2/3 live verification rerun + single-item array payload fix

**Module**: 	ools/verify_phase23_live.ps1, 	ools/check_bridge_health.ps1, handoff/docs refresh

**Files touched**:
- MOD: 	ools/verify_phase23_live.ps1
- MOD: ROUND_TASK_HANDOFF.md
- MOD: ASSISTANT.md
- MOD: 	ools/invoke_external_ai_agent.ps1
- MOD: docs/agent/LESSONS_LEARNED.md

**What was built**:
- Fixed the Phase 2/3 live verification script so DWG/PDF preview payloads keep SheetIds and SheetNumbers as arrays.
- Reran live verification successfully against SR_QQ-T_LOD400_test_truc.huynhN5DTV.
- Confirmed bridge/runtime truth: BridgeOnline=true, RuntimeToolCount=109, SourceToolCount=109, required Phase 2/3 tools satisfied.
- Confirmed dry-run preview success for:
  - schedule.create_safe
  - amily.load_safe (expected missing-family diagnostic for smoke path)
  - export.ifc_safe
  - export.dwg_safe
  - sheet.print_pdf_safe
- Captured final artifact bundle at rtifacts/phase23-live-verify/20260317-021406.

**Problems hit & fixes**:
- Problem: export.dwg_safe and sheet.print_pdf_safe were failing with INVALID_PAYLOAD_JSON even though the live tools were actually healthy.
  - Fix: Forced typed arrays before ConvertTo-Json for single-item SheetIds / SheetNumbers in erify_phase23_live.ps1.
  - Why it works: PowerShell collapses if (...) { @(value) } else { @() } to a scalar when only one item is emitted, so the JSON stopped matching DTO list fields until arrays were forced explicitly.
- Problem: project docs still claimed 114+ tools / 90 tests after the new platform wave.
  - Fix: refreshed repo docs/prompts to the verified current counts (109 tools, 99 tests).
  - Why it works: the handoff now matches the actual source/runtime state and reduces future false debugging.

**Pattern decisions**:
- **Verify script payload shape, not only runtime tool behavior** -> a smoke script can create false negatives if JSON shape drifts from DTO contracts.
- **Count truth must come from source + runtime together** -> doc text should follow verified ToolNames.cs count and live session.list_tools, not stale marketing numbers.

**Reusable for**:
- Any PowerShell smoke/verification script that serializes DTOs with single-item arrays.
- Any future bridge-live acceptance run that needs a final truth artifact instead of a build-only claim.

---

## 2026-03-17 - Round externalization rerun on live workshared test file (95 replaced, QC'd, saved)

**Module**: `tools/run_round_externalization_plan.ps1`, `tools/build_round_project_wrappers.ps1`, `tools/externalize_round_from_plan.ps1`, `tools/reconcile_round_wrapper_qc.ps1`

**Files touched**:
- MOD: `tools/externalize_round_from_plan.ps1`
- MOD: `ROUND_TASK_HANDOFF.md`
- MOD: `docs/agent/BUILD_LOG.md`
- MOD: `docs/agent/LESSONS_LEARNED.md`
- Artifacts:
  - `artifacts/round-externalization-plan-run/20260317-022646`
  - `artifacts/round-wrapper-build/20260317-022655`
  - `artifacts/round-externalization-execute/20260317-023616`
  - `artifacts/round-externalization-execute/20260317-024252`
  - `artifacts/round-externalization-execute/20260317-025404`
  - `artifacts/round-wrapper-qc/20260317-025429`

**What was built**:
- Executed the Round externalization task for real on the active Revit file `SR_QQ-T_LOD400_test_truc.huynhN5DTV`.
- Planned `95` eligible nested `Round` instances, rebuilt/loaded size-specific `Round_Project` wrappers, externalized all `95`, wrote review comments, created wrapper review schedule, ran wrapper QC, and saved the local workshared file.
- Final verified truth in this file:
  - target family/type match: `95/95`
  - position match: `95/95`
  - size match: `95/95`
  - geometry-axis match: `95/95`
  - strict project-axis aligned: `79/95`
  - strict project-axis `ROTATED_IN_VIEW`: `16/95`

**Problems hit & fixes**:
- Problem: the current live file did **not** actually contain the previously claimed finished state; precheck showed `Round_Project = 0`.
  - Fix: reran the full workflow on the active file instead of trusting old artifacts/docs.
  - Why it works: acceptance is tied to the live file state, not to historical success on another machine/file.
- Problem: the first live execute hit `RATE_LIMITED`, resulting in `69 created / 26 failed`.
  - Fix: patched `externalize_round_from_plan.ps1` so mutation preview/execute loops parse `retry after Ns` and retry automatically; then reran **only** the failed old `Round` ids.
  - Why it works: the batch stops behaving like a false hard-fail and becomes resumable without duplicating already-created wrappers.
- Problem: one orphan wrapper remained after a partial create + later `parameter.set_safe` context mismatch.
  - Fix: compared live wrapper inventory vs successful created-id maps, identified orphan `152620785`, and deleted it safely before final QC.
  - Why it works: final wrapper count now matches the merged success map exactly, so QC reads a clean model state.
- Problem: QC expects one authoritative `results.json`, but success came from two execute runs.
  - Fix: merged the two success runs into `artifacts/round-externalization-execute/20260317-025404` after validating no duplicate old/new ids and exact match with live inventory.
  - Why it works: downstream QC/pair reconciliation can run deterministically from one artifact bundle.

**Revit API / runtime gotchas**:
- Long bridge mutation loops can partially mutate the live model before the batch ends; a failed batch is not the same thing as a rolled-back batch.
- Host-face fallback placement can still produce correct target type/position/size while leaving `FamilyAxisAuditService` to classify some instances as `ROTATED_IN_VIEW`.
- For workshared local files, `file.save_document` is acceptable to persist the finished local state; `SynchronizeWithCentral` should still remain an explicit separate decision.

**Pattern decisions**:
- **Never rerun the whole batch after partial create** -> rerun only failed source ids after reconciling live inventory.
- **Merge execute artifacts before QC** -> pair reconciliation should read one clean success map, not infer across multiple partial runs.
- **Truth-first handoff over optimistic claims** -> this session records `79/95` strict project-axis alignment, not the older `95/95` claim.

**Reusable for**:
- Any bridge-driven Revit batch create/update workflow where rate limiting can split a live run into multiple resumable passes.
- Any "extract nested family content into project-level clean families" flow that needs exact old -> new pairing plus post-run cleanup of orphaned partial creates.

---

## 2026-03-17 - Round externalization final strict-axis repair (95/95 fully matched)

**Module**: `tools/externalize_round_from_plan.ps1`, `tools/reconcile_round_wrapper_qc.ps1`, final merged execute/QC artifacts

**Files touched**:
- MOD: `ROUND_TASK_HANDOFF.md`
- MOD: `docs/agent/BUILD_LOG.md`
- MOD: `docs/agent/LESSONS_LEARNED.md`
- Artifacts:
  - `artifacts/round-externalization-execute/20260317-031712`
  - `artifacts/round-externalization-execute/20260317-031909`
  - `artifacts/round-externalization-execute/20260317-032027`
  - `artifacts/round-wrapper-qc/20260317-032043`

**What was built**:
- Took the intermediate `79/95` strict-axis result and repaired the remaining `16` `ROTATED_IN_VIEW` wrappers without rerunning the whole 95-item batch.
- Verified final acceptance on the live file:
  - `Position = 95/95`
  - `Size = 95/95`
  - `GeometryAxis = 95/95`
  - `ProjectAxis = 95/95`
  - `FullyMatched = 95/95`
- Saved the local workshared file again after final QC so `IsModified = False`.

**Problems hit & fixes**:
- Problem: the first strict QC still found 16 wrappers with correct family/type/position/size but wrong instance transform (`ROTATED_IN_VIEW`).
  - Fix: deleted only those 16 wrappers, reran only their source `RoundElementId` values, then rebuilt one authoritative merged `results.json`.
  - Why it works: the old nested `Round` instances remain as the deterministic source of truth, so selective recreation is safer than trying to massage a bad instance transform in place.
- Problem: a direct retry of the first repair batch through an external PowerShell process produced unreliable argument passing for `int[] RoundElementIds`.
  - Fix: reran via same-session splatted parameters into `externalize_round_from_plan.ps1`.
  - Why it works: PowerShell binds the `int[]` parameter deterministically, avoiding command-line array parsing edge cases.

**Revit API / runtime gotchas**:
- A wrapper instance can have the right type and right location but still fail strict transform QC; family type naming alone is not enough to prove axis correctness.
- Recreating a bad `FamilyInstance` is often safer than trying to retroactively rotate/fix it when there is no dedicated safe rotate tool surface.

**Pattern decisions**:
- **Selective delete + selective recreate over full rerun** -> lower risk, faster, and preserves already-good instances.
- **Acceptance artifact must be re-merged after every selective repair** -> QC and handoff should point to the final authoritative execute map, not a partial plus mental patching.

**Reusable for**:
- Any batch family migration where a small residual set fails only transform/orientation checks after the main pass.

---

## 2026-03-17 - Phase 2/3 live verification rerun + single-item array payload fix

**Module**: `tools/verify_phase23_live.ps1`, `tools/check_bridge_health.ps1`, handoff/docs refresh

**Files touched**:
- MOD: `tools/verify_phase23_live.ps1`
- MOD: `ROUND_TASK_HANDOFF.md`
- MOD: `ASSISTANT.md`
- MOD: `tools/invoke_external_ai_agent.ps1`
- MOD: `docs/agent/LESSONS_LEARNED.md`

**What was built**:
- Fixed the Phase 2/3 live verification script so DWG/PDF preview payloads keep `SheetIds` and `SheetNumbers` as arrays.
- Reran live verification successfully against `SR_QQ-T_LOD400_test_truc.huynhN5DTV`.
- Confirmed bridge/runtime truth: `BridgeOnline=true`, `RuntimeToolCount=109`, `SourceToolCount=109`, required Phase 2/3 tools satisfied.
- Confirmed dry-run preview success for:
  - `schedule.create_safe`
  - `family.load_safe` (expected missing-family diagnostic for smoke path)
  - `export.ifc_safe`
  - `export.dwg_safe`
  - `sheet.print_pdf_safe`
- Captured final artifact bundle at `artifacts/phase23-live-verify/20260317-021406`.

**Problems hit & fixes**:
- Problem: `export.dwg_safe` and `sheet.print_pdf_safe` were failing with `INVALID_PAYLOAD_JSON` even though the live tools were actually healthy.
  - Fix: Forced typed arrays before `ConvertTo-Json` for single-item `SheetIds` / `SheetNumbers` in `verify_phase23_live.ps1`.
  - Why it works: PowerShell collapses `if (...) { @(value) } else { @() }` to a scalar when only one item is emitted, so the JSON stopped matching DTO list fields until arrays were forced explicitly.
- Problem: project docs still claimed `114+ tools` / `90 tests` after the new platform wave.
  - Fix: refreshed repo docs/prompts to the verified current counts (`109` tools, `99` tests).
  - Why it works: the handoff now matches the actual source/runtime state and reduces future false debugging.

**Pattern decisions**:
- **Verify script payload shape, not only runtime tool behavior** -> a smoke script can create false negatives if JSON shape drifts from DTO contracts.
- **Count truth must come from source + runtime together** -> doc text should follow verified `ToolNames.cs` count and live `session.list_tools`, not stale marketing numbers.

**Reusable for**:
- Any PowerShell smoke/verification script that serializes DTOs with single-item arrays.
- Any future bridge-live acceptance run that needs a final truth artifact instead of a build-only claim.

---

## 2026-03-17 - Penetration replace blueprint + memory bootstrap for future sessions

**Module**: `docs/agent` memory system, task bootstrap docs

**Files touched**:
- MOD: `ASSISTANT.md`
- MOD: `docs/agent/README.md`
- MOD: `docs/agent/PROJECT_MEMORY.md`
- MOD: `ROUND_TASK_HANDOFF.md`
- NEW: `docs/agent/TASK_JOBDIRECTION_PENETRATION_REPLACE_CASE.md`

**What was built**:
- Added a stable blueprint doc for penetration/nested-family replacement jobs so a new AI session can bootstrap the correct flow immediately.
- Wired the blueprint into `ASSISTANT.md`, curated agent docs, project memory, and the active Round handoff.
- Standardized the expected sequence for these jobs: live verify -> plan -> build/load -> execute -> merge -> QC -> targeted repair -> export review -> save.

**Problems hit & fixes**:
- Problem: task knowledge for the successful Round case was too scattered across artifacts and chat history, making fresh sessions slow to start.
  - Fix: promoted the proven workflow into a dedicated blueprint file plus high-signal pointers in the normal startup docs.
  - Why it works: new sessions now have one obvious place to load the job pattern before touching the live model.

**Pattern decisions**:
- **Blueprint doc lives under `docs/agent/`** -> stable, curated, and easy to include in normal memory loading.
- **Done definition includes export-readiness** -> replacement is not considered complete until old/new review comments and schedule confirm `OK-Ready to export`.

**Reusable for**:
- Any future penetration/opening/sleeve/family replacement job that follows the same old->new deterministic mapping pattern.

---

## 2026-03-17 - Round export truth blueprint for IFC -> MiTek orientation

**Module**: `docs/agent` memory system, Round export workflow direction

**Files touched**:
- NEW: `docs/agent/TASK_JOBDIRECTION_ROUND_EXPORT_IFC_MITEK_CASE.md`
- MOD: `ASSISTANT.md`
- MOD: `docs/agent/README.md`
- MOD: `docs/agent/PROJECT_MEMORY.md`
- MOD: `docs/agent/TASK_JOBDIRECTION_PENETRATION_REPLACE_CASE.md`
- MOD: `docs/agent/LESSONS_LEARNED.md`
- MOD: `ROUND_TASK_HANDOFF.md`

**What was built**:
- Added a dedicated blueprint for the case where `Round_Project` already passes Revit QC but still fails orientation truth after `IFC -> MiTek` conversion.
- Promoted a new project rule: for this downstream path, **export truth overrides project-axis truth**.
- Wired the rule into startup docs, project memory, the generic penetration-replace blueprint, and the active Round handoff so future sessions bootstrap on the right acceptance model.

**Problems hit & fixes**:
- Problem: the existing Round handoff and review schedule could make a new session think `ProjectAxis=OK` meant the export job was fully closed.
  - Fix: split the task truth into 2 layers in docs: `model truth` vs `export truth`.
  - Why it works: the agent can now say "replacement done" without incorrectly saying "MiTek export done".
- Problem: the generic penetration-replace blueprint stopped at Revit QC and export-review comments, which is not enough for a downstream orientation-sensitive consumer.
  - Fix: added an explicit downstream override section plus extra acceptance gates for IFC / MiTek workflows.
  - Why it works: future similar jobs will load the stricter acceptance rule before they reuse the Round pattern.

**Pattern decisions**:
- **Do not mutate the whole model just to satisfy a downstream converter** -> keep `Round_Project` as model truth and prefer export mapping / export-shadow / export metadata for downstream-specific behavior.
- **Treat `AXIS_Z` as export-risk on the IFC -> MiTek path until a sample export proves otherwise** -> vertical cases should prefer a horizontal canonical export contract plus downstream pose such as `STAND_UP`.
- **Keep the rule discoverable in normal startup docs** -> `ASSISTANT.md`, agent README, project memory, handoff, and lessons all point to the new Round export blueprint.

**Reusable for**:
- Future Round export tasks where Revit QC passes but downstream fabrication/conversion software still interprets orientation incorrectly.
- Any BIM workflow where a downstream consumer has a different geometry/orientation contract than Revit's internal placement/audit logic.

---

## 2026-03-17 - Round IFC/MiTek spike bootstrap + IFC export transaction fix

**Module**: `DeliveryOpsService`, Round export spike tooling

**Files touched**:
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/DeliveryOpsService.cs`
- NEW: `tools/run_round_ifc_mitek_spike.ps1`
- MOD: `docs/agent/LESSONS_LEARNED.md`

**What was built**:
- Added a reproducible spike script to select 6 representative `Round_Project` samples from the final QC artifact and package them into a downstream-review matrix.
- Patched `export.ifc_safe` so execute uses a temporary rollback transaction wrapper around `doc.Export(..., IFCExportOptions)`.
- Captured spike artifacts:
  - sample/bootstrap only: `artifacts/round-ifc-mitek-spike/20260317-061034`
  - current-session execute attempt showing old-runtime failure: `artifacts/round-ifc-mitek-spike/20260317-061053`

**Problems hit & fixes**:
- Problem: there was no one-command way to pick a representative 6-sample matrix across `AXIS_X / AXIS_Y / AXIS_Z` and placement modes for the Round downstream export investigation.
  - Fix: created `tools/run_round_ifc_mitek_spike.ps1` to select live samples from the latest `round-wrapper-qc` artifact, query live elements, and emit CSV/JSON review files.
  - Why it works: future sessions can rerun the same spike set instead of manually hunting ids again.
- Problem: `export.ifc_safe` preview succeeded but execute crashed live with `ModificationOutsideTransactionException`.
  - Fix: wrapped the IFC export call in a temporary transaction and rolled it back after export.
  - Why it works: the exporter gets the transaction context it needs, while the model is intended to stay unchanged after the rollback wrapper.
- Problem: after patch/build/install, the open Revit session still ran the old export code.
  - Fix: recorded the truth explicitly: install succeeded, but live verification still needs a Revit restart because tool count stayed the same and the runtime cannot auto-reload the changed DLL.
  - Why it works: future debugging will not confuse "disk updated" with "runtime updated".

**Pattern decisions**:
- **Spike selection should come from final QC pairs, not ad-hoc picking in the model** -> keeps the export matrix deterministic and traceable.
- **Use rollback transaction wrapper for IFC export** -> safer than committing possible exporter-side document changes just to get a file out.
- **Truth-first runtime status** -> if a code patch changes behavior but not tool count, still assume the open Revit session is stale until an execute path proves otherwise.

**Reusable for**:
- Future Round IFC/MiTek investigations where the team needs a compact, representative sample set instead of the whole batch.
- Other export tools that appear file-only on paper but actually need a transaction context at runtime.

---

## 2026-03-17 - Round IFC/MiTek live spike rerun after Revit restart

**Module**: Round export investigation, IFC evidence capture

**Files touched**:
- MOD: `ROUND_TASK_HANDOFF.md`
- MOD: `docs/agent/TASK_JOBDIRECTION_ROUND_EXPORT_IFC_MITEK_CASE.md`
- MOD: `docs/agent/PROJECT_MEMORY.md`
- MOD: `docs/agent/LESSONS_LEARNED.md`
- Artifacts:
  - `artifacts/round-ifc-mitek-spike/20260317-064539`
  - `artifacts/round-ifc-mitek-spike/20260317-064539/ifc-orientation-evidence.json`

**What was built**:
- Reran the 6-sample Round IFC spike on a fresh Revit session.
- Confirmed `export.ifc_safe` live execute now succeeds after the transaction-wrapper fix.
- Exported a real IFC file and extracted orientation evidence from the IFC itself.

**Problems hit & fixes**:
- Problem: before restart, the open Revit session still ran the old export code and kept failing with `ModificationOutsideTransactionException`.
  - Fix: restarted Revit, verified runtime/source both `114`, then reran the spike.
  - Why it works: execute path now used the newly deployed add-in code.
- Problem: the team still needed stronger evidence to know whether the wrong standing behavior came from Revit IFC export or from downstream MiTek handling.
  - Fix: inspected the exported IFC type geometry for the 6 spike samples.
  - Why it works: the IFC proves the exporter preserves axis semantics instead of flattening everything.

**Pattern decisions**:
- **Read the IFC file, not only Revit/QC artifacts** -> for downstream disputes, the IFC itself is the source of truth for what left Revit.
- **Operational rule tightened** -> raw `AXIS_Z` is now treated as a downstream-risk contract on the MiTek path, not as an exporter bug.

**Reusable for**:
- Any future case where the team must separate "Revit exported wrong" from "consumer interpreted wrong".
- Any export-debug task where one small representative matrix is enough to prove the contract.

---

## 2026-03-17 - Round downstream rule tightened after team MiTek convert

**Module**: Round export reasoning, downstream contract model

**Files touched**:
- MOD: `ROUND_TASK_HANDOFF.md`
- MOD: `docs/agent/TASK_JOBDIRECTION_ROUND_EXPORT_IFC_MITEK_CASE.md`
- MOD: `docs/agent/PROJECT_MEMORY.md`
- MOD: `docs/agent/LESSONS_LEARNED.md`

**What was learned**:
- Team structure convert showed the painful truth: only the `AXIS_X` cases came through correctly in MiTek.
- `AXIS_Y` and `AXIS_Z` both failed downstream even though the IFC spike proved Revit exported their geometry orientation correctly.

**Problems hit & fixes**:
- Problem: the previous operating rule still left room to think `AXIS_Y` might be safe because it is horizontal.
  - Fix: tightened the downstream rule to focus on **local X / red axis**, not "horizontal vs vertical".
  - Why it works: the consumer behavior now matches the user's visual test in MiTek more closely than the older `AXIS_Z-only risk` theory.

**Pattern decisions**:
- **For MiTek, the real export-safe contract is `PENETRATION_AXIS = LOCAL_X`** -> if the red axis does not align with the penetration center axis after IFC import, treat the case as downstream-risk.
- **Do not equate "IFC geometry is correct" with "MiTek will interpret it correctly"** -> geometry and consumer semantics are separate layers.

**Reusable for**:
- Any downstream system that visually exposes a "red axis" / local X axis and uses it as the semantic longitudinal direction.
- Future export-surrogate design for penetrations where instance local axis matters more than the raw geometric orientation stored in IFC.

---

## 2026-03-17 - Round local-X canonical spike for IFC/MiTek

**Module**: Round export investigation, canonical `AXIS_X` instance-orientation spike

**Files touched**:
- ADD: `tools/run_round_ifc_localx_spike.ps1`
- Artifacts:
  - `artifacts/round-ifc-localx-spike/20260317-072903`
  - `artifacts/round-ifc-localx-spike/20260317-072903/ifc-localx-evidence.json`

**What was built**:
- Created 3 temporary live `Round_Project` samples, all using the same type `AXIS_X__L1792__D1088`.
- Applied 3 orientation intents:
  - `LOCALX_GLOBALX`
  - `LOCALX_GLOBALY`
  - `LOCALX_GLOBALZ`
- Exported a real IFC, extracted per-instance placement evidence by `IfcGUID`, then cleaned the 3 temporary elements back out of the model.

**Problems hit & fixes**:
- Problem: the first script pass missed the export file path because `export.ifc_safe` returned `Output=...`, not `OutputPath=...`.
  - Fix: normalized the script to accept both artifact prefixes before parsing the IFC.
  - Why it works: the evidence extractor can now follow the real exported file deterministically.
- Problem: there was still no proof whether "all-X + rotate instance" would survive IFC in a way MiTek is likely to use.
  - Fix: compared the 3 cases directly in `IFCLOCALPLACEMENT` / `IFCAXIS2PLACEMENT3D`.
  - Why it works: this isolates instance-orientation behavior from family-type geometry behavior.

**Pattern decisions**:
- **Y can be canonicalized through an AXIS_X instance rotation** -> the `LOCALX_GLOBALY` sample exported with `RefDirection=(0,1,0)` at the instance placement level.
- **Z cannot be trusted through simple 3D rotate on the current OneLevelBased family** -> the `LOCALX_GLOBALZ` sample collapsed back to the same placement contract as the unrotated X sample in IFC.

**Reusable for**:
- Future MiTek-facing export spikes where the team needs to know whether a transform survives as instance local axis in IFC.
- Any decision about whether to build a new export surrogate family versus reusing the current family with only instance rotation.

---

## 2026-03-17 - Round MiTek export contract report formalized

**Module**: Round export planning, X/Y-vs-Z contract split

**Files touched**:
- ADD: `tools/build_round_mitek_export_contract.ps1`
- Artifacts:
  - `artifacts/round-mitek-export-contract/20260317-073621`

**What was built**:
- Read final wrapper QC pairs and converted them into a MiTek-facing export contract report.
- Classified all 95 `Round_Project` wrappers into 3 buckets:
  - `25` = `SAFE_AS_IS_LOCAL_X`
  - `31` = `CANONICALIZE_TO_AXIS_X_IN_PLANE`
  - `39` = `BLOCKED_NEEDS_Z_SURROGATE`

**Pattern decisions**:
- **X/Y can now be split from Z operationally** -> do not keep treating the whole Round batch as one implementation problem.
- **Z remains a separate research/build track** -> the report makes that visible at pair level so future sessions do not waste time trying to push all 95 through the same export logic.

---

## 2026-03-17 - Round downstream spike validated all 3 local-X cases

**Module**: Round export reasoning correction after consumer validation

**What changed**:
- Team structure checked the 3 local-X spike cases and confirmed all 3 are correct in MiTek.
- This invalidated the temporary conclusion that `Z` needed to stay blocked.
- Regenerated the contract report as:
  - `artifacts/round-mitek-export-contract/20260317-083713`

**Updated contract**:
- `25` = `SAFE_AS_IS_LOCAL_X`
- `31` = `CANONICALIZE_TO_AXIS_X_IN_PLANE`
- `39` = `CANONICALIZE_TO_AXIS_X_3D`

**Pattern decision**:
- For Round, the current best export strategy is now a single local-X canonical path with instance rotation for all X/Y/Z directions.

---

## 2026-03-17 - Round full canonical IFC export for MiTek executed on all 95 pairs

**Module**: `tools/export_round_ifc_mitek_canonical.ps1`, Round downstream export workflow

**Files touched**:
- MOD: `tools/export_round_ifc_mitek_canonical.ps1`
- MOD: `ROUND_TASK_HANDOFF.md`
- MOD: `docs/agent/BUILD_LOG.md`
- MOD: `docs/agent/LESSONS_LEARNED.md`
- MOD: `docs/agent/PROJECT_MEMORY.md`
- MOD: `docs/agent/TASK_JOBDIRECTION_ROUND_EXPORT_IFC_MITEK_CASE.md`
- Artifacts:
  - failed first pass: `artifacts/round-ifc-mitek-canonical-full/20260317-102038`
  - successful full export: `artifacts/round-ifc-mitek-canonical-full/20260317-102803`
  - contract report used: `artifacts/round-mitek-export-contract/20260317-102015`

**What was built**:
- Executed the full Round canonical IFC export for all `95` wrapper pairs using the now-validated local-X strategy.
- Exported the final IFC file:
  - `C:\Users\truc.huynh\Documents\BIM765T\Exports\round-ifc-mitek-canonical-full\20260317-102803\round-mitek-canonical-localx-full.ifc`
- Confirmed cleanup removed all 95 temporary surrogate instances after export.

**Problems hit & fixes**:
- Problem: the first full export pass failed before IFC export with `Argument types do not match` while sending `parameter.set_safe` for surrogate Comments/Mark.
  - Fix: changed the payload shape from `Changes = @($changes)` to `Changes = @($changes.ToArray())`.
  - Why it works: the tool now receives an actual JSON array of change objects instead of one wrapped generic list object.
- Problem: the export script only queried type names containing `AXIS_X__`, so it would miss the alias canonical types `AXIS_XY__...` and `AXIS_XZ__...` loaded into `Round_Project`.
  - Fix: widened the catalog query to `NameContains = 'AXIS_'` and filtered exact type names locally.
  - Why it works: all three canonical families of Round types are now available to the export mapper.
- Problem: earlier in the same session, two separate Revit processes were running the add-in on the same named pipe, causing bridge reads to jump between `SR_QQ...` and `SR_ST-R...`.
  - Fix: closed the extra Revit instance and reran only after `check_bridge_health.ps1`, `session.list_open_documents`, and `document.get_active` all agreed on `SR_QQ-T_LOD400_test_truc.huynhN5DTV`.
  - Why it works: live mutation/export is deterministic again when only one Revit model owns the pipe.

**Pattern decisions**:
- **For Round, production export path is now unified** -> `AXIS_X` as-is, `AXIS_Y` via `AXIS_XY__...` + in-plane rotate, `AXIS_Z` via `AXIS_XZ__...` + 3D rotate.
- **Always isolate one Revit process before bridge mutation/export** -> if more than one process is open on the same pipe, treat the session as unsafe.
- **When serializing bridge DTO arrays from PowerShell generic lists, materialize with `.ToArray()`** -> do not rely on `@($list)`.

**Reusable for**:
- Future penetration export flows that need alias-type canonicalization before IFC.
- Any bridge PowerShell script that builds DTO arrays from `System.Collections.Generic.List[object]`.
- Any live export/mutation task on a workstation where multiple Revit instances may be open.

## 2026-03-17 - Production hardening wave: correlation, JSON logging, timeout policy, protected approval store

**Module**: bridge/runtime hardening for observability + execution safety

**Files touched**:
- MOD: `src/BIM765T.Revit.Contracts/Bridge/ToolMessages.cs`
- MOD: `src/BIM765T.Revit.Contracts/Platform/SessionObservabilityDtos.cs`
- MOD: `src/BIM765T.Revit.Contracts/Platform/ToolManifestDtos.cs`
- MOD: `src/BIM765T.Revit.Agent/Config/AgentSettings.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/AgentHost.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Bridge/PendingToolInvocation.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Bridge/PipeServerHostedService.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Bridge/ToolExternalEventHandler.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Logging/IAgentLogger.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Logging/FileLogger.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/FixLoopToolModule.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/MutationFileAndDomainToolModule.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolExecutor.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolRegistry.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolResponseHelpers.cs`
- ADD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolExecutionTimeoutPolicy.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/ApprovalTokenStore.cs`
- MOD: `src/BIM765T.Revit.Agent/UI/InternalToolClient.cs`
- MOD: `src/BIM765T.Revit.Agent/UI/Tabs/ActivityTab.cs`
- MOD: `src/BIM765T.Revit.Bridge/Program.cs`
- MOD: `src/BIM765T.Revit.McpHost/Program.cs`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/ToolMessagesSerializationTests.cs`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/DtoSerializationTests.cs`
- ADD: `tests/BIM765T.Revit.Agent.Core.Tests/*`

**What was built**:
- Added end-to-end `CorrelationId` propagation across Bridge CLI -> MCP host -> pipe server -> executor -> response -> operation journal.
- Upgraded file logging to scoped JSONL with `AsyncLocal` context (`correlationId`, `toolName`, `source`) and retained plain-text fallback behind settings.
- Added manifest-level `ExecutionTimeoutMs` plus a central timeout policy so long-running delivery ops get more realistic limits without rewriting the existing timeout pipeline.
- Hardened persisted approval tokens at rest with protected storage while keeping backward-compatible legacy plaintext load.
- Added a new non-Revit core test project so runtime-hardening logic can be regression-tested without booting Revit.

**Problems hit & fixes**:
- Problem: the original hardening plan assumed timeout did not exist yet.
  - Fix: reused the current request timeout infrastructure and added per-tool override via `ToolManifest.ExecutionTimeoutMs` + `ToolExecutionTimeoutPolicy`.
  - Why it works: safer rollout, less churn, faster execution path.
- Problem: a naive HMAC-per-process design would have broken persisted approval tokens after Revit restart.
  - Fix: kept server-side approval tokens and protected the persisted store at rest instead of switching to stateless tokens.
  - Why it works: restart survival stays intact while disk exposure is reduced.
- Problem: plain text logs were cheap to write but weak for cross-layer traceability.
  - Fix: moved to JSON Lines with `BeginScope(...)` so pipe/executor/external-event logs can share the same correlation metadata.
  - Why it works: tool-call troubleshooting becomes deterministic and machine-parsable.

**Revit API / platform gotchas**:
- Do not try to cancel live Revit API execution mid-flight; timeout must stay at the pipe/pending-invocation boundary.
- Avoid global static mutable logging context; `AsyncLocal` is the minimum safe choice when bridge, queue, and external-event hops all exist.
- Approval-token persistence must survive restart, so any cryptographic hardening has to preserve a stable decryption path.

**Pattern decisions**:
- **Correlation is now first-class runtime metadata** -> request/response/journal/log must all carry the same trace key.
- **Timeout policy belongs to tool manifests** -> callers can inspect tool budgets and the runtime can stay fast for cheap tools.
- **Protected persisted store beats fake stateless crypto** -> truth-first hardening over security theater.
- **Test pure runtime seams separately from Revit-bound code** -> better coverage without forcing a premature facade refactor.

**Reusable for**:
- Any future bridge or MCP feature that needs traceability across layers.
- Delivery-op tools whose runtime budget differs materially from read/review tools.
- Hardening future persisted state files without losing backward compatibility.

## 2026-03-17 - Phase 2 intelligence upgrade: smarter fix-loop rule matching, recommended actions, selected-action verification

**Module**: supervised fix-loop / decision engine

**Files touched**:
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/FixLoopService.cs`
- ADD: `src/BIM765T.Revit.Agent/Services/Platform/FixLoopDecisionEngine.cs`
- MOD: `src/BIM765T.Revit.Contracts/Platform/FixLoopDtos.cs`
- MOD: `docs/agent/playbooks/default.fix_loop_v1.json`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/DtoSerializationTests.cs`
- MOD: `tests/BIM765T.Revit.Agent.Core.Tests/BIM765T.Revit.Agent.Core.Tests.csproj`
- ADD: `tests/BIM765T.Revit.Agent.Core.Tests/FixLoopDecisionEngineTests.cs`

**What was built**:
- Parameter hygiene rules now resolve by specificity (`parameter + category + family + element-name`) instead of only parameter name.
- View/template compliance rules now resolve with richer context (`view type + view name + current template + sheet prefix`).
- Candidate actions now carry `Priority`, `IsRecommended`, and `DecisionReason`.
- Plan/review responses now surface `RecommendedActionIds` and evidence tracks selected vs recommended action sets.
- Verification now measures expected delta from the **selected actions**, not from the full candidate universe.
- Playbook loading now supports project-specific override candidates under `%APPDATA%\\BIM765T.Revit.Agent\\playbooks\\projects\\...` and repo `docs/agent/playbooks/projects/...`, with last-write cache.

**Problems hit & fixes**:
- Problem: the original fix-loop logic grouped every missing parameter only by parameter name, so a category-specific rule could be diluted by unrelated elements.
  - Fix: added `FixLoopDecisionEngine.ResolveParameterRule(...)` and grouped actions by strategy/risk/recommendation while matching rules with category/family/element specificity.
  - Why it works: the planner now proposes actions closer to how a BIM lead actually scopes cleanup.
- Problem: verification expected delta was tied to all candidate actions, which overstated success when the operator only approved a subset.
  - Fix: verification now recomputes expected issue delta from `Evidence.SelectedActionIds` and compares residual issues against that narrower intent.
  - Why it works: post-apply reporting stays truthful for supervised execution.
- Problem: playbook override lookup only checked one global JSON path.
  - Fix: added project-key candidate generation + cache keyed by full path and last-write time.
  - Why it works: per-project rules can override defaults without slowing every run with repeated disk deserialization.

**Pattern decisions**:
- **Default action selection must prefer recommended executable actions** -> the fix-loop should reduce operator burden without auto-executing everything.
- **Decision logic belongs in pure helpers** -> `FixLoopDecisionEngine` stays Revit-free so it can be tested fast and often.
- **Verification must follow approved intent** -> compare reality to the selected action subset, not the full proposal set.
- **Project playbooks should override by naming convention, not ad-hoc branching in service code** -> keeps extension points explicit and predictable.

**Reusable for**:
- Future fix-loop scenarios that need project-specific rule resolution.
- Any supervised workflow where default selection/recommendation order matters.
- Any feature that needs truthful expected-vs-actual delta reporting after partial execution.

## 2026-03-17 - Phase 2 live execute truth check: small parameter_hygiene case closed-loop pass

**Module**: supervised fix-loop / live Revit verification

**Files touched**:
- MOD: `src/BIM765T.Revit.Agent/UI/Theme/AnimationHelper.cs`
- ADD: `artifacts/phase2-live-execute/20260317-152525/*`

**What was verified live**:
- Ran a real `parameter_hygiene` execution on active model `SR_QQ-T_LOD400_truc.huynhN5DTV`.
- Scope was intentionally bounded to **one element**: `ElementId=1039862`.
- Selected and executed only one recommended action: `param_fill:Comments:1`.
- Runtime changed exactly one element and filled `Comments` from empty string to `REVIEW_REQUIRED`.
- Closed-loop verify passed with truthful selected-action reporting:
  - `SelectedActionIds = ["param_fill:Comments:1"]`
  - `ExpectedIssueDelta = 1`
  - `ActualIssueDelta = 1`
  - `VerificationStatus = pass`
- Post-check for `review.parameter_completeness` on `Comments` returned `IssueCount = 0`.

**Problems hit & fixes**:
- Problem: first execute attempt was rejected with `HIGH_RISK_REQUIRES_CONTEXT`.
  - Fix: propagate `ExpectedContextJson` from plan into execute payload for the real mutation step.
  - Why it works: high-risk fix-loop execution stays bound to the reviewed document/view epoch and cannot drift silently.
- Problem: the smoke script crashed after successful execute because it assumed `review.parameter_completeness` had a `Summary` property.
  - Fix: truth source for this review tool is `IssueCount`/`Issues`, not a synthetic summary object.
  - Why it works: reporting now reads the actual DTO contract instead of guessing shape from other review tools.
- Problem: runtime parity by tool count previously hid stale logic.
  - Fix: full rebuild, latest shadow install, then live verify on metadata fields (`RecommendedActionIds`, `DecisionReason`, `Priority`, `IsRecommended`) before doing the real write.
  - Why it works: behavior truth now comes from runtime payloads, not only from source/build assumptions.

**Pattern decisions**:
- **Bound the first live execute to one element and one action** -> fastest truthful check with minimal model risk.
- **Verify selected-action delta, not scenario delta** -> proves supervised fix-loop can report what was actually approved and changed.
- **Treat artifact payloads as source-of-truth** -> if wrapper scripts fail late, execution truth still lives in saved JSON payloads.

**Reusable for**:
- Any future first-write validation for new fix-loop scenarios.
- High-risk workflow scripts that need exact `expected_context` propagation.
- Regression checks for `review.fix_candidates -> plan -> apply -> verify` closed loops.

## 2026-03-17 - vNext copilot foundation slice: durable task runtime + context broker + state graph

**Module**: copilot runtime foundation / context-efficient agent control plane

**Files touched**:
- ADD: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskDtos.cs`
- MOD: `src/BIM765T.Revit.Contracts/Bridge/ToolNames.cs`
- MOD: `src/BIM765T.Revit.Contracts/Common/StatusCodes.cs`
- MOD: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.cs`
- ADD: `src/BIM765T.Revit.Copilot.Core/BIM765T.Revit.Copilot.Core.csproj`
- ADD: `src/BIM765T.Revit.Copilot.Core/CopilotStatePaths.cs`
- ADD: `src/BIM765T.Revit.Copilot.Core/CopilotTaskRunStore.cs`
- ADD: `src/BIM765T.Revit.Copilot.Core/ContextAnchorService.cs`
- ADD: `src/BIM765T.Revit.Copilot.Core/ArtifactSummaryService.cs`
- ADD: `src/BIM765T.Revit.Copilot.Core/TaskMetricsService.cs`
- ADD: `src/BIM765T.Revit.Copilot.Core/ToolCapabilitySearchService.cs`
- ADD: `src/BIM765T.Revit.Agent/Services/Platform/ModelStateGraphService.cs`
- ADD: `src/BIM765T.Revit.Agent/Services/Platform/CopilotTaskService.cs`
- ADD: `src/BIM765T.Revit.Agent/Services/Bridge/CopilotTaskToolModule.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/AgentHost.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Bridge/ToolInvocationQueue.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/Bridge/ToolExternalEventHandler.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolModuleContext.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/ToolRegistry.cs`
- ADD: `tools/verify_copilot_runtime_live.ps1`
- MOD: `tools/check_bridge_health.ps1`
- ADD: `docs/agent/COPILOT_RUNTIME_FOUNDATION.md`
- ADD: `tests/BIM765T.Revit.Contracts.Tests/CopilotTaskDtoTests.cs`
- ADD: `tests/BIM765T.Revit.Contracts.Tests/CopilotTaskValidatorTests.cs`
- ADD: `tests/BIM765T.Revit.Agent.Core.Tests/CopilotCoreServicesTests.cs`

**What was built**:
- Added a first durable `task.*` API surface over existing `workflow` and `fix_loop` engines.
- Added a file-backed local run store under `%APPDATA%\\BIM765T.Revit.Agent\\state` so task state survives process restarts.
- Added `context.*`, `artifact.summarize`, `memory.find_similar_runs`, and `tool.find_by_capability` to stop overloading the agent with raw file/tool dumps.
- Added `session.get_runtime_health` and `session.get_queue_state` for runtime observability.
- Added `ModelStateGraphService` to produce a lightweight hot graph snapshot from document cache + selected/recent element graph.
- Split non-Revit durable/context logic into new shared project `BIM765T.Revit.Copilot.Core` so the future net8 runtime split has a real seam.
- Added live-smoke script support for the new copilot foundation.

**Problems hit & fixes**:
- Problem: the grand vNext plan was too large to ship honestly in one pass.
  - Fix: implemented a foundation slice that preserves the current safety kernel and adds durable task/context primitives first.
  - Why it works: this creates an upgrade path without destabilizing proven Revit execution lanes.
- Problem: runtime/tool-count truth can lag source truth when Revit still runs an older shadow build.
  - Fix: `check_bridge_health.ps1` now has a `copilot` profile so new runtime surfaces can be checked explicitly.
  - Why it works: copilot readiness is validated by required task/context APIs, not by vague source assumptions.
- Problem: context anchors and bundle resolution can accidentally re-add duplicate items from hot + warm sources.
  - Fix: bundle resolution now de-duplicates by `AnchorId` and keeps the highest-score copy.
  - Why it works: agent context stays smaller and more deterministic.
- Problem: fix-loop task plans were not carrying artifact keys into durable task summaries.
  - Fix: `MapFromFixLoop(...)` now copies `Evidence.ArtifactKeys` into the durable `TaskRun`.
  - Why it works: downstream summarization and context retrieval now see the same evidence the planner produced.

**Pattern decisions**:
- **Do not rewrite the safety kernel** -> build the copilot runtime above guarded Revit execution, not through a risky platform rewrite.
- **Task API before more raw tools** -> AI should prefer durable `task.*` and `context.*` surfaces instead of ad-hoc multi-tool chains for every bounded job.
- **Hot/warm/cold retrieval over raw docs** -> context should be resolved through anchors, bundles, and summaries, not by loading large files into the prompt.
- **File-backed durable store first, SQLite later** -> enough to prove durable task-state behavior now without blocking on the full metadata database split.
- **Pure-core extraction only where it pays off** -> new non-Revit runtime logic lives in `BIM765T.Revit.Copilot.Core`; Revit-bound execution remains inside Agent.

**Reusable for**:
- Future net8 local copilot runtime split.
- Resume/recovery-capable task orchestration.
- Context-broker retrieval patterns for long or complex BIM tasks.
- Operator-app surfaces that need durable task summaries, queue state, and artifact summaries.

## 2026-03-17 - durable checkpoint + resume/recovery for copilot task runtime

**Module**: copilot task runtime / durable resume-recovery

**Files touched**:
- MOD: `src/BIM765T.Revit.Contracts/Platform/CopilotTaskDtos.cs`
- MOD: `src/BIM765T.Revit.Contracts/Common/StatusCodes.cs`
- MOD: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.cs`
- ADD: `src/BIM765T.Revit.Copilot.Core/TaskRecoveryPlanner.cs`
- MOD: `src/BIM765T.Revit.Agent/Infrastructure/AgentHost.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/CopilotTaskService.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Bridge/CopilotTaskToolModule.cs`
- MOD: `tools/verify_copilot_runtime_live.ps1`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/CopilotTaskDtoTests.cs`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/CopilotTaskValidatorTests.cs`
- ADD: `tests/BIM765T.Revit.Agent.Core.Tests/TaskRecoveryPlannerTests.cs`

**What was built**:
- Added durable `TaskCheckpointRecord` and `TaskRecoveryBranch` surfaces to `TaskRun`.
- Added `LastErrorCode` / `LastErrorMessage` so blocked runs keep truthful failure state instead of only returning an exception to the caller.
- Added `TaskRecoveryPlanner` in `BIM765T.Revit.Copilot.Core` to infer next action and recovery branches from persisted task state.
- `task.resume` now chooses a recovery branch instead of blindly re-executing the task.
- Runtime now persists checkpoints after plan / preview / approve / execute / verify / summarize and after blocked recoverable failures.
- Task summary and residual views now expose checkpoint/resume metadata for compact agent loops and operator review.
- Live smoke script now reports checkpoint/recovery capability once the new runtime is loaded.

**Problems hit & fixes**:
- Problem: the first copilot slice persisted task state but still behaved too much like a stateless wrapper around `workflow`/`fix_loop`.
  - Fix: added checkpoint records + recovery branch inference and persisted them on every meaningful state transition.
  - Why it works: resume/recovery is now driven by durable task truth, not by guesswork from the latest tool call.
- Problem: `task.resume` originally bypassed step intent and mostly jumped straight into execute.
  - Fix: `TaskRecoveryPlanner` now chooses between preview / approve / execute / verify / summary branches, with manual-only vs auto-resumable distinction.
  - Why it works: the runtime can now pause on approval, recover from context/approval drift, and continue the correct next step.
- Problem: recoverable failures like `CONTEXT_MISMATCH` or approval drift were lost once the tool call returned an exception.
  - Fix: `CopilotTaskService.PersistFailure(...)` now records last error, blocked checkpoint, and refreshed recovery branches before rethrowing.
  - Why it works: after a failed call, `task.get_run` still contains truthful resume guidance.
- Problem: copilot smoke script only proved planning/context surfaces, not recovery durability.
  - Fix: smoke summary now includes checkpoint count, recovery branch count, and top branch ids.
  - Why it works: live verification can now distinguish "task surface exists" from "resume/recovery state is really there".

**Pattern decisions**:
- **Recovery logic belongs in shared pure-core code** -> `TaskRecoveryPlanner` stays Revit-free and testable.
- **Failures must be persisted before they are thrown** -> otherwise durable task APIs lie after a blocked mutation.
- **Resume should select a branch, not just retry** -> important for approval-bound and context-bound BIM tasks.
- **Checkpoint metadata must stay compact** -> enough for agent/app reasoning without dumping raw payloads back into prompt context.

**Reusable for**:
- Future workflow graph checkpointing and crash-resume work.
- Long-running delivery ops that need pause/resume semantics.
- Operator app surfaces like task inbox / approval center / recovery suggestions.

**Live truth after install (before restart)**:
- `check_bridge_health.ps1 -Profile copilot` still showed tool-count parity (`134/134`) because this wave changed **behavior**, not tool names.
- `verify_copilot_runtime_live.ps1` proved the current Revit session was still stale:
  - `SupportsCheckpointRecovery = false`
  - `CheckpointCount = 0`
  - `RecoveryBranchCount = 0`
- Conclusion: latest checkpoint/recovery code is built, tested, and installed, but Revit must restart once to load shadow build `Release-0.0.0.0-20260317-235013`.

## 2026-03-18 - Hot-state crash on legacy sparse durable runs; normalize store reads and make copilot smoke truth-preserving

**Scope**: durable checkpoint/recovery live verification after Revit restart

**Files touched**:
- `src/BIM765T.Revit.Copilot.Core/CopilotTaskRunStore.cs`
- `src/BIM765T.Revit.Agent/Services/Platform/CopilotTaskService.cs`
- `tools/verify_copilot_runtime_live.ps1`
- `tests/BIM765T.Revit.Agent.Core.Tests/CopilotCoreServicesTests.cs`

**What changed**:
- Normalized `TaskRun` objects on store `Save`, `TryGet`, and `List` so legacy/sparse JSON files cannot surface null collections into the runtime.
- Added defensive `EnsureTaskRunCollections(...)` in `CopilotTaskService` before summary/recovery logic.
- Updated live smoke script to:
  - prefer canonical `DocumentKey`
  - survive `context.get_hot_state` failure without hiding checkpoint/recovery truth
  - include hot-state error text in summary instead of crashing the whole smoke
- Added a regression test for legacy sparse task-run files.

**Problem hit & fix**:
- Problem: after restart, `context.get_hot_state` still crashed even though checkpoint/recovery was actually live.
  - Root cause from structured logs: `CopilotTaskService.BuildSummary(TaskRun run)` hit a `NullReferenceException` while iterating pending durable runs; some older persisted `TaskRun` JSON files were missing newly added list fields.
  - Fix: normalize legacy task runs on read and before summary/recovery operations.
  - Why it works: old durable state no longer poisons hot-state/runtime summaries when the schema grows additively.

**Truth after fix/build/install**:
- Current live session proves:
  - `SupportsCheckpointRecovery = true`
  - `CheckpointCount = 1`
  - `RecoveryBranchCount = 1`
- `context.get_hot_state` in the current session still returns `INTERNAL_ERROR` because Revit has not yet loaded the just-installed normalization build.
- New shadow build installed to:
  - `C:\\Users\\ADMIN\\AppData\\Local\\BIM765T.Revit.Agent\\shadow\\2024\\Release-0.0.0.0-20260318-063125\\BIM765T.Revit.Agent.dll`
- Therefore:
  - checkpoint/recovery is live and verified
  - hot-state crash is fixed in source/build/install
  - one more Revit restart is required to verify the hot-state lane with the patched runtime

---

## 2026-03-18 - Copilot hot-state final verified live after checkpoint/recovery hardening

**Module**: CopilotTaskService, CopilotTaskRunStore, verify_copilot_runtime_live workflow

**Truth captured**:
- `context.get_hot_state` now passes live in the current Revit session.
- Proof artifact: `artifacts/copilot-runtime-live/20260318-065619`
- Runtime truth in that artifact:
  - `HotState.StatusCode = READ_SUCCEEDED`
  - `SupportsCheckpointRecovery = true`
  - `CheckpointCount = 1`
  - `RecoveryBranchCount = 1`
  - `GraphNodeCount = 2`
  - `GraphEdgeCount = 1`
- No further restart was required after this verification pass for the current wave.

---

## 2026-03-18 - Copy live instance parameters from `Penetration Alpha M` sang `Penetration Alpha`

**Module**: penetration replace support / live data migration

**Files touched**:
- `tools/copy_penetration_family_type_parameters.ps1`
- `tools/copy_penetration_instance_parameters.ps1`

**What changed**:
- Built a live helper to inspect type-level family parameter copy feasibility.
- Proved the request is **not** a simple type-to-type copy:
  - `Penetration Alpha M` has `59` types
  - `Penetration Alpha` has `3` types
- Added an instance-level copy workflow that:
  - inventories both families live (`95` vs `95`)
  - pairs source/target instances by penetration signature + spatial ordering
  - copies shared writable data parameters onto target instances via `parameter.set_safe`
  - verifies target values after execute

**Live truth**:
- Active file: `SR_QQ-T_LOD400_test_truc.huynhN5DTV`
- Source family: `Penetration Alpha M`
- Target family: `Penetration Alpha`
- Pair count: `95`
- Effective parameter scope:
  - `Comments`
  - `Mark`
  - overlapping writable `Mii_*` instance parameters
- Execute artifact:
  - `artifacts/penetration-parameter-copy/20260318-062934`
- Execute status:
  - `EXECUTE_SUCCEEDED`
- Verification:
  - `269 / 269` changed rows matched source after execute

**Pattern decisions**:
- For penetration replace follow-up data migration, treat the task as **instance data copy**, not family type copy, when old/new family type catalogs are structurally different.
- Inventory count parity plus paired live instances is a stronger truth source than family/type naming similarity.
- Use guarded `parameter.set_safe` batch writes for many paired instance updates instead of many small mutation calls.

**Why it matters**:
- This closes the gap between "checkpoint/recovery is live" and "hot-state/context lane is live".
- Copilot runtime foundation is now verified on all key surfaces in one live session: runtime health, hot state, durable task run, bundle lookup, similar runs, and capability lookup.

---

## 2026-03-18 - Sync nested `Penetration Alpha` types inside `Penetration Alpha M`

**Module**: penetration family-doc workflow / nested family type control

**Files touched**:
- `src/BIM765T.Revit.Contracts/Bridge/ToolNames.cs`
- `src/BIM765T.Revit.Contracts/Platform/PenetrationShadowDtos.cs`
- `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.cs`
- `src/BIM765T.Revit.Agent/Services/Bridge/PenetrationWorkflowToolModule.cs`
- `src/BIM765T.Revit.Agent/Services/Platform/PenetrationShadowService.cs`
- `tools/sync_penetration_alpha_nested_types.ps1`

**What changed**:
- Added mutation tool `family.sync_penetration_alpha_nested_types_safe` to work on the family document `Penetration Alpha M.rfa`.
- Tool inventories parent `FamilyManager.Types`, finds the nested `Penetration Alpha` instance, creates missing child types matching each parent type name, and reloads the edited family back into the open project.
- Added live helper script to preview / execute / verify this nested-type sync flow and capture artifacts.

**Live truth so far**:
- Runtime new tool loaded successfully after restart (`136/136`).
- First live execute artifact: `artifacts/penetration-alpha-nested-type-sync/20260318-144047`
- First execute proved:
  - `ParentTypeCount = 59`
  - `CreatedNestedTypeCount = 59`
  - `VerifyMissingChildTypeCount = 0`
  - but `VerifyAssignCount = 58`
- Conclusion: type creation was correct, but the initial assignment strategy was incomplete.

**Problems & fixes**:
- Initial guard wrongly relied only on `doc.OwnerFamily?.Name`; on the active family doc this came back empty, so the tool falsely raised `REVIT_CONTEXT_MISSING`.
- Fixed by recognizing the family doc via fallback chain: `OwnerFamily.Name` -> `doc.Title` without extension -> `doc.PathName` without extension.
- More important: assigning nested child type by `nestedInstance.ChangeTypeId(...)` while switching `FamilyManager.CurrentType` did **not** persist a separate value per parent family type.
- Fixed design by switching to a proper **family type control parameter** pattern:
  - resolve the nested instance `Type` element parameter,
  - create/reuse a family type parameter bound to the nested family category,
  - associate the element parameter to that family parameter,
  - then `FamilyManager.Set(controlParameter, targetSymbol.Id)` for each parent type.

**Pattern decisions**:
- For nested family type mapping inside a parent family, direct `ChangeTypeId` is not enough when the goal is per-parent-type persistence.
- The stable pattern is: **associate nested Type parameter -> family type parameter -> set per `FamilyManager.CurrentType`**.
- When the family editor doc comes from `Edit Family` and has no path, document identity checks must not depend only on `OwnerFamily`.

**Reusable for**:
- nested penetration / opening / sleeve families where child family type must follow parent family type name
- any family-doc workflow that needs type-specific nested family control and reload back into a live project

---

## 2026-03-18 - Docs/instructions sync review after Round export and nested family waves

**Module**: repo operating docs, current memory, handoff consistency

**Files touched**:
- MOD: `ASSISTANT.md`
- MOD: `ASSISTANT.md`
- MOD: `README.BIM765T.Revit.Agent.md`
- MOD: `README.md`
- MOD: `docs/agent/README.md`
- MOD: `docs/agent/PROJECT_MEMORY.md`
- MOD: `docs/agent/TASK_JOBDIRECTION_PENETRATION_REPLACE_CASE.md`
- MOD: `docs/agent/TASK_JOBDIRECTION_ROUND_EXPORT_IFC_MITEK_CASE.md`
- MOD: `ROUND_TASK_HANDOFF.md`

**What changed**:
- Removed stale hardcoded tool/test counts from operating docs and switched those docs to dynamic/source-of-truth wording.
- Synced repo memory so current truth now clearly separates:
  - historical build chronology
  - current stable memory
  - active job handoff truth
- Corrected Round export guidance so docs no longer promote superseded conclusions such as `Z blocked` or alias-based `AXIS_XY__...` / `AXIS_XZ__...` production truth.
- Added explicit family-doc lane guidance for nested type sync cases like `Penetration Alpha M -> Penetration Alpha`.

**Pattern decisions**:
- `BUILD_LOG.md` is chronology, not current truth.
- Current truth must live in `PROJECT_MEMORY.md` and the active handoff/task docs.
- If a downstream/export hypothesis gets invalidated later, mark it as superseded instead of letting it coexist silently with newer truth.

**Reusable for**:
- future doc-sync rounds after a large debug/export wave
- any project where multiple historical hypotheses can otherwise leak into new sessions as if they were current truth


## 2026-03-21 - BIM /init + Context Engine V1 bootstrap

**Module**: Contracts (project context DTOs/validation), Copilot.Core, WorkerHost, Worker/Tool surface, local web workspace

**Files touched**:
- NEW: `src/BIM765T.Revit.Contracts/Platform/ProjectContextDtos.cs`
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.ProjectInit.cs`
- NEW: `src/BIM765T.Revit.Copilot.Core/ProjectContextServices.cs`
- NEW: `src/BIM765T.Revit.WorkerHost/Projects/ProjectInitHostService.cs`
- NEW: `src/BIM765T.Revit.WorkerHost/Projects/ProjectHttpEndpoints.cs`
- NEW: `tests/BIM765T.Revit.Contracts.Tests/ProjectContextDtoTests.cs`
- NEW: `tests/BIM765T.Revit.Contracts.Tests/ProjectInitValidatorTests.cs`
- NEW: `tests/BIM765T.Revit.WorkerHost.Tests/ProjectInitServicesTests.cs`
- NEW: `docs/agent/templates/firm-overlay/*`
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/CopilotTaskService.cs`
- MOD: `src/BIM765T.Revit.Agent/Services/Platform/WorkerService.cs`
- MOD: `BIM765T-Revit-WebPage/src/lib/projectTypes.ts`
- MOD: `BIM765T-Revit-WebPage/src/lib/projectApi.ts`
- MOD: `BIM765T-Revit-WebPage/src/pages/workspace/WorkspacePage.tsx`

**What was built**:
- Added `project.init_preview`, `project.init_apply`, `project.get_manifest`, and `project.get_context_bundle`.
- Introduced curated workspace bootstrap files for project overlay instead of overloading `workspace.json`.
- Added localhost HTTP wrappers in WorkerHost so the web workspace can consume init/context flows directly.
- Kept context precedence explicit: `core safety > firm doctrine > project overlay > session/run memory`.

**Problems hit & fixes**:
- Problem: project bootstrap can easily become a dumping ground for source binaries and raw report text.
  - Fix: store only manifest metadata, curated summaries, brief markdown, and refs in workspace.
  - Why it works: repo/workspace stays small, reviewable, and reusable across sessions.
- Problem: primary model selection becomes ambiguous when a source folder contains many `.rvt` files.
  - Fix: only auto-pick when there is exactly one candidate; otherwise `project.init_apply` requires explicit `PrimaryRevitFilePath`.
  - Why it works: bootstrap stays deterministic and honest about what the project brain is grounded on.

**Pattern decisions**:
- `/init` is a bootstrap/context operation first, not a deep scan.
- WorkerHost owns orchestration; Revit kernel is optional for seed summaries.
- Firm doctrine is consumed from packs, not generated ad-hoc during init.

**Reusable for**:
- Any future workspace bootstrap flow that needs project-specific overlays without polluting runtime policy files.
- Future firm/project onboarding flows for multi-project BIM environments.

---

## 2026-03-21 - Project Brain Deep Scan Wave 2

**Module**: Contracts, Copilot.Core, WorkerHost project services, project context composer, local web workspace

**Files touched**:
- NEW: `src/BIM765T.Revit.Contracts/Platform/ProjectDeepScanDtos.cs`
- NEW: `src/BIM765T.Revit.Contracts/Validation/ToolPayloadValidator.ProjectDeepScan.cs`
- NEW: `src/BIM765T.Revit.Copilot.Core/ProjectDeepScanService.cs`
- NEW: `src/BIM765T.Revit.WorkerHost/Projects/ProjectDeepScanHostService.cs`
- MOD: `src/BIM765T.Revit.Contracts/Bridge/ToolNames.cs`
- MOD: `src/BIM765T.Revit.Contracts/Common/StatusCodes.cs`
- MOD: `src/BIM765T.Revit.Copilot.Core/ProjectContextServices.cs`
- MOD: `src/BIM765T.Revit.WorkerHost/Program.cs`
- MOD: `src/BIM765T.Revit.WorkerHost/Projects/ProjectHttpEndpoints.cs`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/ProjectContextDtoTests.cs`
- MOD: `tests/BIM765T.Revit.Contracts.Tests/ProjectInitValidatorTests.cs`
- MOD: `tests/BIM765T.Revit.WorkerHost.Tests/ProjectInitServicesTests.cs`
- MOD: `BIM765T-Revit-WebPage/src/lib/projectTypes.ts`
- MOD: `BIM765T-Revit-WebPage/src/lib/projectApi.ts`
- MOD: `BIM765T-Revit-WebPage/src/pages/workspace/WorkspacePage.tsx`

**What was built**:
- Added `project.deep_scan` / `project.get_deep_scan` and corresponding HTTP endpoints.
- Built a read-only WorkerHost orchestration that reuses kernel-safe tools for model health, links, worksets, sheets, schedules, and smart QC.
- Persisted deep scan outputs as curated report JSON + markdown summary and surfaced them back through `project.get_context_bundle`.
- Extended the web workspace with a dedicated `Project Brain Deep Scan` tab.

**Problems hit & fixes**:
- Problem: there was a high risk of creating one giant Revit-bound scanner service that duplicated existing tool logic.
  - Fix: compose the scan from existing read/QC tools and keep orchestration in WorkerHost.
  - Why it works: preserves kernel safety boundaries and reduces duplicated code paths.
- Problem: a raw deep scan payload can become too big and too noisy for worker/web consumers.
  - Fix: persist the full curated report to disk, then expose only summary, counts, and top refs in context bundles and UI surfaces.
  - Why it works: keeps prompts/UI small while preserving an auditable artifact trail.

**Pattern decisions**:
- Read-only by default; no mutation paths in Wave 2.
- Quota-driven scan knobs in the web form to keep runtime predictable.
- Context bundle remains the main consumer contract; raw reports are for evidence/explorer flows.

**Reusable for**:
- Future project-level review pipelines such as standards-gap analysis, PDF-grounded QA, and delta scan compare.
- Any long-running BIM analysis flow that should summarize through curated reports rather than inline blobs.
