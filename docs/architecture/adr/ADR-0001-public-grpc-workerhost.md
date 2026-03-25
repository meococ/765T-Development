# ADR-0001 - Public control plane dùng gRPC trên WorkerHost

## Status
Accepted

## Decision
Public IPC cho CLI/MCP/future clients sẽ chạy qua `BIM765T.Revit.WorkerHost` bằng **gRPC over Named Pipes** trên `.NET 8`.

## Context
- Raw named-pipe byte protocol cũ scale kém và khó versioning.
- Add-in `net48` không phải boundary phù hợp để nhận public gRPC trực tiếp.
- Cần streaming, timeout, cancellation, contract-first codegen.

## Consequences
- Bridge và MCP Host trở thành façade gọi WorkerHost.
- Public contract được chuyển sang `.proto`.
- Add-in không còn là public pipe server.
