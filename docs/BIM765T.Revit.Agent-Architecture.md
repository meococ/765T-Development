# 765T Revit Bridge Platform Architecture

## Current architecture truth
- Public control plane: `BIM765T.Revit.WorkerHost` (`net8.0`) qua **gRPC over Named Pipes**
- Private Revit boundary: `BIM765T.Revit.Agent` (`net48`) qua **protobuf kernel pipe**
- Safety kernel bất biến:
  - `ExternalEvent`
  - UI thread
  - `preview -> approval -> execute`
  - transaction safety

## Main layers
1. `BIM765T.Revit.Bridge`
   - CLI façade gọi WorkerHost
2. `BIM765T.Revit.McpHost`
   - MCP stdio façade gọi WorkerHost
3. `BIM765T.Revit.WorkerHost`
   - mission orchestration
   - event store / replay
   - streaming
   - memory search
4. `BIM765T.Revit.Agent`
   - execution kernel private trong Revit
5. `BIM765T.Revit.Contracts` + `BIM765T.Revit.Contracts.Proto`
   - DTO legacy + proto contracts v2

## Safety controls
- AI không được chạm raw Revit API ngoài kernel lane.
- Mutation phải qua approval token + expected context.
- WorkerHost chết không được làm add-in crash.
- Qdrant chết phải degrade về lexical fallback.

## Read next
- `docs/architecture/SDD_V2_ENTERPRISE_REWRITE.md`
- `docs/architecture/adr/ADR-0001-public-grpc-workerhost.md`
- `docs/architecture/adr/ADR-0002-private-protobuf-kernel-channel.md`
- `docs/architecture/adr/ADR-0003-sqlite-event-store.md`
- `docs/architecture/adr/ADR-0004-qdrant-promotion-only-memory.md`
