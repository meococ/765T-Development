# ADR-0003 - Durable runtime dùng SQLite event store

## Status
Accepted

## Decision
State durable của mission/task sẽ dùng **SQLite event sourcing** với snapshot + outbox, thay cho JSON file-backed state.

## Context
- JSON file dễ race-condition, dễ corrupt khi crash giữa chừng.
- Cần replay chính xác, audit trail, và projection/search cục bộ.
- Desktop companion không nên phụ thuộc database server ngoài.

## Consequences
- Mỗi mission là một stream.
- Replay/snapshot là default recovery path.
- WAL checkpoint background được bật để giữ local store ổn định lâu dài.
