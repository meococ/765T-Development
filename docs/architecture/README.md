# Architecture Target-State Pack

> Status: active planning pack
> Last updated: 2026-03-25

## Mục đích

Folder này giữ **target-state architecture docs** cho giai đoạn dọn kiến trúc trước khi modernize UI.

- `../ARCHITECTURE.md` và `../PATTERNS.md` vẫn là **current runtime truth**
- folder này là **redline + ADR pack cho target state**

## Read order

1. `../ARCHITECTURE.md`
2. `../PATTERNS.md`
3. `ARCHITECTURE_REDLINE_2026Q2.md`
4. `IMPLEMENTATION_SLICES_2026Q2.md`
5. `IMPLEMENTATION_BACKLOG_2026Q2.md`
6. `WORK_PACKAGES_2026Q2.md`
7. `EXECUTION_GATES_2026Q2.md`
8. `adr/README.md`

## Pack này chốt gì

- canonical IPC topology cho target state
- chiến lược UI đúng cho Revit 2024
- WorkerHost sidecar lifecycle theo UX integrated
- ưu tiên Flow shell trước Hub/Audit dashboard
- product hóa workspace thành 765T Hub
- boundary memory production vs fallback hiện tại

## Không phải current truth

Pack này **không có nghĩa là runtime hiện tại đã hoàn thành** các quyết định bên dưới.

Khi có xung đột:
- **current truth**: `../ARCHITECTURE.md`, `../PATTERNS.md`
- **target state**: pack này
