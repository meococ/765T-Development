# Filtering & Performance

- Ưu tiên `FilteredElementCollector` với class/category filter trước.
- Tránh query full model khi chỉ cần active view hoặc selection scope.
- Cache tên parameter/type/category nếu dùng nhiều trong loop.
- Với sheet/view audit, chỉ lấy số lượng hoặc sample trước khi kéo full payload.
