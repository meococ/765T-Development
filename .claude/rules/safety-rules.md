# BIM765T — Quality & Performance Rules

> Những quy tắc này giúp code CHẠY TỐT, không phải để ngăn cản. Hiểu lý do → áp dụng tự nhiên.

## Mutation Flow — UX chất lượng

- `DRY_RUN -> APPROVAL -> EXECUTE` — user thấy trước, quyết định, rồi thực thi
- ApprovalToken 5 phút — đủ thời gian review, đủ ngắn để tránh stale state
- Context hash match giữa preview và execute — đảm bảo consistency
- Rollback strategy cho mỗi transaction — graceful recovery khi cần

## Security — Chuyên nghiệp cơ bản

- Không commit secrets: .env, *.key, *.pem, credentials.json, secrets.*
- Không expose API keys/tokens/passwords trong code hoặc logs
- Không force push, hard reset trên shared branches
- Không skip pre-commit hooks

## Performance — Tận dụng tối đa Revit API

- FilteredElementCollector: Quick filters TRƯỚC (OfCategory, OfClass, WhereElementIsNotElementType) → fast path
- Slow filters SAU (parameter filters, LINQ) → chỉ khi cần precision
- VirtualizingStackPanel cho lists > 50 items → smooth UI
- Lazy loading cho large datasets → responsive UX
- **Batch operations thay vì loop đơn lẻ** → hiệu năng gấp bội

## Thread Model — Hiểu đúng, code đúng

- Revit API single-threaded → gọi từ UI thread hoặc qua ExternalEvent
- ExternalEvent = gateway duy nhất cho mutation từ background thread
- Không `.Result` trên UI thread → deadlock guaranteed (đây là technical fact)
- `Dispatcher.InvokeAsync` cho mọi background-to-UI update
