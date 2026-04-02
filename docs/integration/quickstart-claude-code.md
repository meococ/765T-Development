# Quick Start: Claude Code / MCP

Use this guide to connect Claude Code or another MCP client to a live Revit session.

## Canonical Path

```text
IDE/MCP -> BIM765T.Revit.McpHost -> BIM765T.Revit.WorkerHost -> BIM765T.Revit.Agent -> Revit API
```

## Prerequisites

- Revit add-in installed
- WorkerHost available on the machine
- Revit 2024 or 2026 installed
- One target model path ready
- AI provider credentials configured if you want AI-backed planning

## 1. Configure AI Providers

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\infra\setup_ai_providers.ps1
```

## 2. Start WorkerHost

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\infra\start_workerhost.ps1
```

## 3. Launch Revit Deterministically

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\restart_revit_and_trust_addin.ps1 `
  -ModelPath "C:\path\to\YourModel.rvt" `
  -AutoTrustUnsignedAddin
```

This helper closes stray Revit processes, opens the target model, and waits until the bridge reports that exact model as the active session.

## 4. Verify Runtime Health

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check_bridge_health.ps1 -AsJson
```

Minimum expected state:

- `BridgeOnline = true`
- `ActiveDocument` matches your target model
- `RevitSessionIsolated = true`

## 5. Configure MCP

Use the example at [examples/mcp.json.example](examples/mcp.json.example).

Minimal entry:

```json
{
  "mcpServers": {
    "revit-agent": {
      "command": "%REPO_ROOT%/src/BIM765T.Revit.McpHost/bin/Release/net8.0/BIM765T.Revit.McpHost.exe"
    }
  }
}
```

Replace `%REPO_ROOT%` with the actual repo root or with the location of the shipped binary bundle on your machine.

## 6. Smoke Prompt

Ask the IDE:

```text
List the active Revit document, active view, and available tools.
```

If the model title and tool list come back correctly, the MCP path is ready.

## Notes

- Keep exactly one `Revit.exe` for live mutation/export work.
- For mutation flows, expect `preview -> approval -> execute -> verify`.
- If your MCP client shows no tools, fix runtime health first before editing config again.

## Related Docs

- [quickstart-ai-testing.md](quickstart-ai-testing.md)
- [../reference/mcphost.md](../reference/mcphost.md)
- [../troubleshooting/revit-agent-debug.md](../troubleshooting/revit-agent-debug.md)
