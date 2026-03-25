# 765T Revit Bridge - Review Rule Engine v1

## Muc tieu

Review Rule Engine v1 la lop review tong quat chay tren bridge runtime, khong hardcode vao Hull. Engine nay gom cac rule set dau tien de:

- review suc khoe document
- review active view
- review selection scope
- review parameter completeness

## Tool runtime

- Tool name: `review.run_rule_set`
- Permission: `Review`
- Approval: `None`
- Dry-run: `false`

## Rule sets hien co

### `document_health_v1`

Tong hop cac issue tu `review.model_health`, gom:

- document dang modified
- warnings dang ton tai
- links chua load

### `active_view_v1`

Review active/target view va tao issue khi:

- view la template
- view khong co element visible
- document co warnings
- selection dang rong

### `selection_v1`

Review selection hien tai hoac danh sach `ElementIds` dau vao:

- canh bao neu scope rong
- canh bao neu element id khong con ton tai
- neu co `RequiredParameterNames` thi goi tiep review parameter completeness

### `parameter_completeness_v1`

Kiem tra parameter bat buoc tren selection/input scope:

- scope rong
- parameter missing
- parameter empty

## Ghi chu rollout

- Code da duoc wire vao `ToolRegistry`.
- Runtime hien tai chi nhan tool moi sau khi Revit load build moi nhat.
- Sau moi lan deploy build moi, can rerun:
  - `tools/generate_tool_catalog.ps1`
  - `tools/sync_workflow_from_catalog.ps1`

## Huong nang cap phase sau

- rule severity mapping theo team policy
- rule packs theo domain: Hull / QC / Documentation / Model Health
- rule output co remediation hint
- baseline / compare mode giua hai snapshot
