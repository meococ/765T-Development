# Delegate to external AI

Dùng khi cần nhờ một AI/client bên ngoài review, critique, hoặc hỗ trợ plan thông qua **external AI broker** của WorkerHost.

## Nguyên tắc

- Với repo này, đọc `CLAUDE.md` trước rồi mới theo canonical truth ở `AGENTS.md`, `ASSISTANT.md`, `docs/ARCHITECTURE.md`, `docs/PATTERNS.md`.
- Nếu cần AI ngoài, ưu tiên gọi WorkerHost `/api/external-ai/*` thay vì phụ thuộc vendor-specific CLI.
- Không lấy runtime cache cũ làm current truth.
- Model baseline hiện tại cho OpenAI-compatible lane: `gpt-5.4`.
