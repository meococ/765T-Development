# Repo .assistant

Đây là repo-local static assistant support layer.

## Read order cho repo này

Với các assistant dưới `.assistant/`, startup read order luôn bắt đầu từ `CLAUDE.md`.

- `CLAUDE.md` — repo-specific critical notes và latest working guidance
- `AGENTS.md` — constitution và boundary rules
- `ASSISTANT.md` và `docs/assistant/*` — adapter/runtime truth

Rule này là repo-local cho `.assistant` lane trong repository này, không phải machine-global setting.

## Canonical parts cần giữ lại

- `commands/`
- `config/`
- `agents/`
- `souls/`
- `schemas/`

## Không còn canonical

- runtime relay
- runs
- temp memory/cache

Sau khi đọc `CLAUDE.md`, source of truth vẫn là `README.md`, `AGENTS.md`, `ASSISTANT.md`, và `docs/assistant/*`.
