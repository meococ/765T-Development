# BIM765T.Revit.McpHost

`BIM765T.Revit.McpHost` is the MCP stdio adapter for the product path.

## Role

- receives MCP requests from an IDE
- forwards them to WorkerHost
- returns structured results back to the MCP client

`McpHost` does not call the Revit API directly.

## Runtime Path

```text
IDE/MCP -> McpHost -> WorkerHost -> kernel -> BIM765T.Revit.Agent -> Revit API
```

## Tools

- `tools/list` returns the WorkerHost-owned MCP catalog
- `tools/call` forwards a tool request to WorkerHost and returns:
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

## Common Arguments

The host understands these common fields when a tool needs them:

- `target_document`
- `target_view`
- `dry_run`
- `approval_token`
- `preview_run_id`
- `expected_context`
- `scope_descriptor`
- `correlation_id`
- `payload`

## Startup Expectation

`tools/list` and live Revit tools are expected to fail closed when Revit or the add-in is not attached.
That is correct behavior for the product path.

## Command-Line

```powershell
.\BIM765T.Revit.McpHost.exe
```

Optional:

```powershell
.\BIM765T.Revit.McpHost.exe --pipe BIM765T.Revit.WorkerHost
```

## Related Docs

- [../integration/quickstart-claude-code.md](../integration/quickstart-claude-code.md)
- [../troubleshooting/revit-agent-debug.md](../troubleshooting/revit-agent-debug.md)
