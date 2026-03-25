# ADR-0002 - Revit boundary dùng private protobuf kernel pipe

## Status
Accepted

## Decision
WorkerHost <-> Revit Agent dùng **private named pipe + protobuf delimited messages**, không ép gRPC trực tiếp vào add-in `net48`.

## Context
- Revit add-in bị khóa bởi `.NET Framework 4.8`.
- Boundary vào Revit phải nhỏ, deterministic, và dễ audit.
- Safety kernel hiện có (`ExternalEvent`, approval, context check) phải giữ nguyên.

## Consequences
- Agent chỉ giữ execution kernel.
- Public evolution nằm ở WorkerHost, không nằm trong add-in.
- Kernel messages bắt buộc carry correlation/causation/mission metadata.
