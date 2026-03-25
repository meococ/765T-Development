# SDD v2 - Enterprise rewrite control plane

## 1. Mục tiêu
- Nâng public control plane từ raw named-pipe protocol lên **gRPC over Named Pipes**.
- Giữ nguyên **Revit safety kernel**: `ExternalEvent`, UI thread, preview/approval/execute, transaction safety.
- Đổi durable state từ JSON file sang **SQLite event sourcing**.
- Tách intelligence/runtime khỏi add-in sang **WorkerHost .NET 8**.
- Đưa semantic retrieval sang **Qdrant local**, nhưng chỉ ingest dữ liệu đã promote.

## 2. Non-goals
- Không rewrite toàn bộ mutation logic của Revit add-in.
- Không cho LLM hoặc vector runtime chạy trực tiếp trong `net48` add-in.
- Không thay đổi invariant approval token + context fingerprint.

## 3. Topology
```text
Bridge CLI / MCP Host / future UI clients
    -> gRPC over Named Pipes
WorkerHost (.NET 8)
    -> Mission services
    -> Event store / projections / replay
    -> Planner / Retriever / Safety / Verifier seams
    -> Memory search (Qdrant + lexical fallback)
    -> private protobuf kernel pipe
Revit Agent (.NET Framework 4.8)
    -> ExternalEvent scheduler
    -> Tool registry + platform services
    -> preview / approval / execute
    -> transaction-safe mutation
```

## 4. Thành phần chính

### 4.1 `BIM765T.Revit.Contracts.Proto`
- Nguồn contract canonical cho v2.
- Chứa:
  - `MissionService`
  - `MissionStreamService`
  - `ContextService`
  - `CatalogService`
  - `CompatibilityService`
  - `KernelInvoke*` messages cho private kernel channel
- Rule versioning:
  - chỉ **additive**
  - không reuse field number
  - breaking change phải bump contract/version rõ ràng

### 4.2 `BIM765T.Revit.WorkerHost`
- Public sidecar `.NET 8`.
- Listen gRPC trên named pipe `BIM765T.Revit.WorkerHost`.
- Chức năng:
  - façade cho Bridge / MCP
  - mission orchestration
  - append event + snapshot
  - replay/read model
  - memory ingest/search
  - streaming mission events

### 4.3 `BIM765T.Revit.Agent`
- Giữ vai trò **Revit Execution Kernel** private.
- Không còn là public bridge server.
- Private kernel pipe `BIM765T.Revit.Agent.Kernel` dùng protobuf length-delimited.
- Chỉ chịu trách nhiệm:
  - resolve document/view/context
  - raise `ExternalEvent`
  - execute tool an toàn
  - approval token validation
  - context fingerprint enforcement
  - diagnostics

## 5. Luồng chính

### 5.1 Compatibility lane
1. Bridge/MCP gọi `CompatibilityService.InvokeTool`.
2. WorkerHost map request -> `KernelToolRequest`.
3. WorkerHost append `TaskStarted`.
4. WorkerHost gọi private kernel pipe.
5. Agent chạy tool trên UI thread.
6. WorkerHost append `PreviewGenerated` / `ApprovalRequested` hoặc `TaskCompleted`.

### 5.2 Mission lane
1. Client gọi `MissionService.SubmitMission`.
2. WorkerHost classify intent + resolve memory hits.
3. WorkerHost append `IntentClassified`, `ContextResolved`, `PlanBuilt`.
4. WorkerHost gửi `worker.message` qua private kernel pipe.
5. Kết quả trả về được persist thành event stream + snapshot.

### 5.3 Approval lane
1. Client gọi `ApproveMission`.
2. WorkerHost append `UserApproved`.
3. WorkerHost gửi command tiếp cho kernel lane.
4. Snapshot chuyển sang `Completed` hoặc `Blocked`.

## 6. Event sourcing
- SQLite là source of truth local.
- Bảng:
  - `events`
  - `snapshots`
  - `outbox`
  - `memory_projection`
- SQLite defaults:
  - `WAL`
  - `synchronous=FULL`
  - `foreign_keys=ON`
  - `busy_timeout=5000`
  - background `wal_checkpoint`
- Stream = mỗi mission/task một stream.
- Snapshot cập nhật cùng transaction với event append.

### 6.1 Event taxonomy tối thiểu
- `TaskStarted`
- `IntentClassified`
- `ContextResolved`
- `PlanBuilt`
- `PreviewGenerated`
- `ApprovalRequested`
- `UserApproved`
- `UserRejected`
- `ExecutionStarted`
- `RevitMutationApplied`
- `VerificationPassed`
- `VerificationFailed`
- `TaskCompleted`
- `TaskBlocked`
- `TaskCanceled`
- `MemoryPromoted`

## 7. Retrieval / memory
- `PROJECT_MEMORY.md` và `LESSONS_LEARNED.md` vẫn là nguồn human-curated.
- Runtime không dump nguyên markdown vào prompt.
- `MarkdownMemoryBootstrapper` chunk + promote vào:
  - SQLite projection
  - Qdrant local collection `runtime`
- Search policy:
  1. semantic search nếu Qdrant sẵn sàng
  2. lexical fallback từ SQLite nếu semantic fail/down
- Metadata filter:
  - `kind`
  - `document_key`
  - `event_type`
  - `run_id`
  - `promoted`
  - `created_utc`

## 8. Safety / policy
- Không mutation nào bypass approval token.
- WorkerHost chỉ là control plane; mutation truth vẫn ở Agent.
- Add-in không expose raw Revit API ra ngoài public network/process boundary.
- Nếu WorkerHost chết:
  - add-in không crash
  - bridge/MCP fail-closed
- Nếu Qdrant chết:
  - WorkerHost vẫn chạy với lexical fallback

## 9. Observability
- Mọi RPC/event phải carry:
  - `CorrelationId`
  - `CausationId`
  - `MissionId`
  - `ActorId`
  - `DocumentKey`
- `MissionStreamService` là lane progress mặc định cho UI/MCP streaming.

## 10. Rollout
### Phase đang implement
- dựng proto contracts
- dựng WorkerHost
- chuyển Bridge/MCP sang gRPC facade
- chuyển Agent sang private kernel pipe
- dựng SQLite event store + memory bootstrap

### Phase sau
- tăng độ giàu cho planner/retriever/safety/verifier
- thêm migration JSON state -> event stream bootstrap
- thêm health/deploy scripts cho WorkerHost + Qdrant companion

## 11. Acceptance gates
- Build pass cho:
  - Contracts.Proto
  - WorkerHost
  - Bridge
  - McpHost
  - Agent
- Test pass cho:
  - proto roundtrip
  - event append/snapshot replay
  - lexical fallback
  - architecture boundaries
