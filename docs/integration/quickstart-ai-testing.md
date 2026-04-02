# Quick Start: Runtime And AI Testing

Use this guide when you want to verify the runtime without attaching an IDE first.

## Scope

This guide checks:

- WorkerHost readiness
- bridge connectivity
- MCP connectivity
- AI-backed chat readiness

## 1. Start WorkerHost

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\infra\start_workerhost.ps1
```

## 2. Check WorkerHost Health

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check_workerhost_health.ps1 -AsJson
```

Expected minimum:

- `Ready = true`
- `StandaloneChatReady = true`

## 3. Attach Revit

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\restart_revit_and_trust_addin.ps1 `
  -ModelPath "C:\path\to\YourModel.rvt" `
  -AutoTrustUnsignedAddin
```

## 4. Check Bridge Health

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check_bridge_health.ps1 -AsJson
```

Expected minimum:

- `BridgeOnline = true`
- `ActiveDocument` is correct
- `RevitSessionIsolated = true`

## 5. Run MCP Smoke

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\testing\test_mcp_smoke.ps1
```

## 6. Run AI Chat Smoke

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\testing\test_ai_chat_e2e.ps1
```

## HTTP Checks

If you want to probe WorkerHost directly:

```powershell
curl http://localhost:50765/health
curl http://localhost:50765/api/external-ai/status
```

## Related Docs

- [quickstart-claude-code.md](quickstart-claude-code.md)
- [../troubleshooting/revit-agent-debug.md](../troubleshooting/revit-agent-debug.md)
