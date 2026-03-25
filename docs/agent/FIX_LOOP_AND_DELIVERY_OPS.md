# Fix Loop and Delivery Ops

## Purpose

This note explains the Phase 2 and Phase 3 additions that move the platform from
"guarded remote control" toward a supervised BIM assistant.

Use this file when you need to:
- add a new fix-loop scenario
- tune playbooks for a project
- add or debug family load / schedule create / IFC / DWG / PDF jobs
- understand why these capabilities were implemented as policy-governed services

---

## Phase 2: supervised fix loop

### Tool surface

- `review.fix_candidates`
- `workflow.fix_loop_plan`
- `workflow.fix_loop_apply`
- `workflow.fix_loop_verify`

### Current scenarios

- `parameter_hygiene`
- `safe_cleanup`
- `view_template_compliance_assist`

### Closed loop contract

Every run is expected to follow:

1. detect
2. classify
3. propose
4. dry-run
5. execute
6. verify
7. report

### Evidence bundle

Each run should preserve:
- issues found
- candidate actions
- actions applied
- expected delta
- actual delta
- residual issues
- blocked reasons

### Playbook resolution

Fix-loop rules resolve in this order:

1. `%APPDATA%\BIM765T.Revit.Agent\playbooks\<name>.json`
2. `docs/agent/playbooks/<name>.json`
3. code fallback in `FixLoopService`

### Design rule

Do not replace stable built-in workflows when adding fix-loop intelligence.
Add new supervised logic in parallel first. Merge only after the pattern is proven.

---

## Phase 3: delivery ops wave 1

### Tool surface

- `family.list_library_roots`
- `family.load_safe`
- `schedule.preview_create`
- `schedule.create_safe`
- `export.list_presets`
- `export.ifc_safe`
- `export.dwg_safe`
- `sheet.print_pdf_safe`
- `storage.validate_output_target`

### Policy model

Delivery operations are intentionally restricted:
- family load only from allowlisted library roots
- output jobs only into allowlisted output roots
- export/print run from named presets
- preview required before execute
- overwrite/conflict policy must be explicit

### Preset resolution

Delivery-op presets resolve in this order:

1. `%APPDATA%\BIM765T.Revit.Agent\presets\delivery_ops.json`
2. `docs/agent/presets/delivery_ops.json`
3. code fallback in `DeliveryOpsService`

### Current scope limits

- `schedule.create_safe` is model-schedule oriented in v1
- IFC/DWG/PDF use named presets, not unrestricted raw option payloads
- file jobs are bounded by root containment checks

---

## Runtime truth checklist

Before claiming success:

1. `dotnet build BIM765T.Revit.Agent.sln -c Release`
2. `dotnet test tests\BIM765T.Revit.Contracts.Tests\BIM765T.Revit.Contracts.Tests.csproj -c Release --no-build`
3. install add-in
4. run `tools\check_bridge_health.ps1`
5. verify tool list includes the new Phase 2/3 tools
6. perform at least one dry-run on each new capability in a live Revit session

If bridge is offline, you may claim build/test/install complete, but not live verification.

---

## Safe extension pattern

When adding a new fix-loop scenario:
- define issue class + verification rule
- add playbook hook
- keep scope bounded
- keep high-risk apply paths approval-gated
- add evidence bundle fields before shipping

When adding a new delivery-op tool:
- define DTO + validator
- define preset/root resolution
- add preview summary first
- enforce path containment
- wire execute through existing approval semantics

---

## Related files

- `src/BIM765T.Revit.Agent/Services/Platform/FixLoopService.cs`
- `src/BIM765T.Revit.Agent/Services/Platform/DeliveryOpsService.cs`
- `src/BIM765T.Revit.Agent/Services/Bridge/FixLoopToolModule.cs`
- `src/BIM765T.Revit.Agent/Services/Bridge/DeliveryOpsToolModule.cs`
- `docs/agent/playbooks/default.fix_loop_v1.json`
- `docs/agent/presets/delivery_ops.json`
