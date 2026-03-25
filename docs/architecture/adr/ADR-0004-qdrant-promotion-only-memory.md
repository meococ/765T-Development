# ADR-0004 - Semantic memory dùng Qdrant local, promotion-only

## Status
Accepted

## Decision
Semantic retrieval chạy trên **Qdrant local loopback-only**, nhưng chỉ index dữ liệu **đã promote**.

## Context
- Runtime memory cần semantic search, nhưng không được biến thành self-learning black box.
- Markdown memory vẫn hữu ích cho con người, nhưng không nên nhét nguyên file vào prompt runtime.
- Khi Qdrant unavailable, hệ thống vẫn phải chạy an toàn.

## Consequences
- `PROJECT_MEMORY.md` / `LESSONS_LEARNED.md` được bootstrap thành chunks có metadata.
- WorkerHost có lexical fallback từ SQLite projection.
- Không có auto-self-learning; flow luôn là `candidate -> review -> promote`.
