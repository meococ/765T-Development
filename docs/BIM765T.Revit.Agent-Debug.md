# 765T Revit Agent Debug Notes

## Log location
`%APPDATA%\\BIM765T.Revit.Agent\\logs\\yyyyMMdd.log`

## Smoke tests
1. Mở Revit 2024 + load add-in
2. Chạy `Show Agent`
3. Từ bridge CLI gọi:
   - `document.get_active`
   - `session.list_tools`
   - `session.get_capabilities`
   - `session.list_open_documents`
   - `review.model_warnings`
   - `session.get_recent_events`

## Debug priorities
- active document/view null
- named pipe không connect được
- external event không được accepted
- approval token mismatch/expired
- save/sync bị block bởi policy
- context drift giữa dry-run và execute

## Không nên làm ngay
- mở write tools trước khi validation/rollback xong
- expose command chạy code tự do
- quét toàn model không có scope rõ ràng
