# Troubleshooting

Use this guide when the runtime stack is up but the IDE or scripts still cannot operate correctly.

## First Checks

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check_workerhost_health.ps1 -AsJson
powershell -ExecutionPolicy Bypass -File .\tools\check_bridge_health.ps1 -AsJson
```

## Common Problems

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| `BridgeOnline = false` | Revit or add-in is not attached | Relaunch with `restart_revit_and_trust_addin.ps1` |
| Wrong model is active | Multiple Revit sessions | Close extras and verify `RevitSessionIsolated = true` |
| MCP shows no tools | WorkerHost or add-in not ready | Fix runtime health first, then retry `tools/list` |
| Mutation cannot execute | Missing preview, approval token, or expected context | Rerun preview and approve from the latest state |
| AI chat is degraded | Provider not configured or provider unavailable | Run `check_ai_readiness.ps1` and inspect WorkerHost status |

## Useful Commands

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\testing\test_bridge_smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\testing\test_mcp_smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\testing\verify_copilot_runtime_live.ps1
```

## When To Stop

Do not continue with live mutations if any of these are true:

- `RevitSessionIsolated = false`
- `ActiveDocument` is not the intended model
- `BridgeOnline = false`
- the latest preview token or expected context is missing
