# 765T Revit Bridge - Snapshot & Review Strategy

## Mục tiêu
Bridge cần snapshot theo 2 lớp để AI và người dùng đều kiểm tra được:

1. **Structured snapshot**
   - JSON `Snapshot` + `Elements`
   - phù hợp cho AI/rule engine/query/diff
2. **Visual snapshot**
   - PNG export từ view hoặc sheet
   - phù hợp để người dùng nhìn nhanh, attach evidence, so sánh review

## Tool liên quan
- `review.sheet_summary`
- `review.workset_health`
- `review.capture_snapshot`
- `review.run_rule_set`

## Workflow khuyến nghị

### 1) Review sheet
1. `review.sheet_summary`
2. `review.run_rule_set` với `sheet_qc_v1`
3. `review.capture_snapshot` với:
   - `Scope = "sheet"`
   - `ExportImage = true`

### 2) Review workset trước khi edit
1. `review.workset_health`
2. nếu có `ACTIVE_WORKSET_MISMATCH_SELECTION` hoặc `SELECTION_WORKSET_NOT_EDITABLE`
   - dừng write tool
   - đổi active workset / selection / ownership trước

### 3) Review active view hoặc selection
1. `review.active_view_summary`
2. `review.run_rule_set` với:
   - `active_view_v1`
   - `selection_v1`
   - `parameter_completeness_v1`
3. `review.capture_snapshot`
   - `Scope = "active_view"` hoặc `selection`

## Snapshot scope
- `active_view`
- `sheet`
- `selection`
- `element_ids`

## Best practices
- Luôn lấy **structured snapshot** trước; ảnh chỉ là lớp evidence bổ sung
- Với sheet, nên export PNG để nhìn layout/title block/viewport rõ hơn
- Với task AI write, nên chụp snapshot trước và sau operation để:
  - diff issue
  - diff warnings
  - attach artifacts vào journal/report

## Hạn chế hiện tại
- PNG export hiện ưu tiên **1 view/sheet mỗi lần**
- Chưa có semantic OCR trên ảnh; AI hiện chủ yếu phân tích phần structured snapshot
- Chưa có compare-image tự động; so sánh visual hiện là manual/evidence-first
