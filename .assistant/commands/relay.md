Đọc và xử lý messages trong relay mailbox dành cho Claude.

## Workflow

1. Đọc tất cả messages pending trong `.assistant/relay/active/` addressed to `claude`
2. Với mỗi message, thực hiện theo role được gán:
   - **reviewer**: Review code/plan, nêu findings + risks + recommendations
   - **planner**: Lập implementation plan, chia steps, nêu risks
   - **debugger**: Phân tích root cause, đề xuất instrumentation
   - **critic**: Challenge assumptions, tìm weak points
   - **architect**: Đánh giá architecture, đề xuất patterns
   - **executor**: Thực thi plan, báo kết quả
3. Ghi reply vào relay

## Cách dùng

```
/relay                    → Xem và xử lý messages
/relay status             → Chỉ xem trạng thái, không xử lý
```

## Thực hiện

Khi có messages pending:

1. Chạy: `powershell -File tools/relay_receive.ps1 -For claude -AsMarkdown`
2. Đọc nội dung và xử lý theo role
3. Tạo response JSON file tại `.assistant/relay/temp-response.json`
4. Chạy: `powershell -File tools/relay_reply.ps1 -MessageId <id> -From claude -Content .assistant/relay/temp-response.json`

Khi không có messages: báo "Relay inbox trống — không có message nào cần xử lý."

## Quy tắc

- LUÔN đọc taskContext và revitContextPath nếu có
- Trả lời bằng structured content (summary, findings, risks, nextActions)
- Không tự mutate code khi role là reviewer/critic — chỉ nhận xét
- Ghi chú nếu thiếu context để agent kia bổ sung
