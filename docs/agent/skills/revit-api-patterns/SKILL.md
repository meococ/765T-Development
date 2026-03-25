# Revit API Patterns

## Khi nào load
- Khi viết/add tool mới
- Khi debug mutation flow
- Khi review transaction, collector, view/sheet/family logic

## Entry points
- `transactions.md`
- `external-event.md`
- `filtering-performance.md`
- `family-editing.md`
- `regeneration.md`

## Default advice
- Mutation phải chạy trong Revit-safe flow, không gọi API bừa ngoài `ExternalEvent`.
- Collector phải lọc class/category trước, tránh `ToElements()` quá sớm.
- Với family/sheet/view tools, luôn kiểm tra active doc/view context trước khi mutate.
