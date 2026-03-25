# 765T Revit MCP Host

## Current boundary truth
- `BIM765T.Revit.McpHost` l? **MCP stdio JSON-RPC facade**.
- MCP host **kh?ng g?i th?ng raw Revit API**.
- MCP host hi?n n?i chuy?n v?i **WorkerHost** qua gRPC over named pipes.
- Default public pipe name l?y t? `BridgeConstants.DefaultWorkerHostPipeName` = `BIM765T.Revit.WorkerHost`.
- Mutation-capable tools v?n ph?i t?n tr?ng `dry-run -> approval -> execute -> verify` ? downstream lane.

## What `tools/list` really does now
- `tools/list` g?i live `session.list_tools` qua bridge path.
- N?u bridge/catalog ch?a s?n s?ng, host **fail-closed** v?i l?i h??ng d?n b?t Revit + initialize agent.
- V? v?y kh?ng n?n claim `tools/list` lu?n ch?y ???c khi Revit ch?a m?.

## What `tools/call` does
- nh?n tool name + arguments t? MCP client
- chu?n h?a flattened args sang payload PascalCase khi c?n
- build `ToolRequestEnvelope`
- forward request sang WorkerHost pipe
- tr? structured result g?m:
  - `succeeded`
  - `statusCode`
  - `approvalToken`
  - `previewRunId`
  - `changedIds`
  - `payload`
  - `diffSummary`
  - `reviewSummary`
  - `diagnostics`
  - `artifacts`

## Runtime defaults
- `dry_run` m?c ??nh = `true` n?u caller kh?ng truy?n r?
- common args ???c host hi?u:
  - `target_document`
  - `target_view`
  - `dry_run`
  - `approval_token`
  - `expected_context`
  - `scope_descriptor`
  - `preview_run_id`
  - `correlation_id`
  - `payload`

## Build
```powershell
dotnet build BIM765T.Revit.Agent.sln -c Release
```

## Run host
```powershell
.\src\BIM765T.Revit.McpHost\bin\Release\net8.0\BIM765T.Revit.McpHost.exe
```

Ho?c ch? r? public pipe:
```powershell
.\src\BIM765T.Revit.McpHost\bin\Release\net8.0\BIM765T.Revit.McpHost.exe --pipe BIM765T.Revit.WorkerHost
```

## Usage guidance
- d?ng `tools/list` sau khi Revit + add-in + bridge lane ?? s?n s?ng
- v?i tool mutate/file-lifecycle, preview tr??c r?i m?i execute b?ng `approval_token`
- n?u c?n product/runtime truth cao h?n, ??c ti?p:
  - `docs/ARCHITECTURE.md`
  - `docs/PATTERNS.md`
  - `docs/assistant/BASELINE.md`
