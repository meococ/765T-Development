# 765T Revit Tooling

Product-facing PowerShell helpers for install, startup, verification, and smoke testing.

## Categories

- `tools/`: health, catalog, and operational helpers
- `tools/infra/`: install, startup, service, and packaging helpers
- `tools/testing/`: smoke and verification helpers
- `tools/round/`: domain-specific round/penetration workflows
- `tools/dev/`: local utilities that do not define product behavior

Internal workflow orchestration and planning helpers are intentionally excluded from this repo.

## Core Scripts

| Script | Purpose |
| --- | --- |
| `check_bridge_health.ps1` | Verify bridge/runtime health, active document, and single-session safety |
| `check_workerhost_health.ps1` | Verify WorkerHost health and degraded mode |
| `check_ai_readiness.ps1` | Verify provider configuration for AI-backed flows |
| `check_tool_registry.ps1` | Audit live tool manifests for missing metadata |
| `generate_tool_catalog.ps1` | Export the live tool catalog |
| `run_revit_mvp_manual_smoke.ps1` | Create a manual smoke bundle for the current machine/runtime |

## Infrastructure

| Script | Purpose |
| --- | --- |
| `infra/setup_ai_providers.ps1` | Configure provider environment variables |
| `infra/start_workerhost.ps1` | Start WorkerHost and capture logs |
| `infra/restart_revit_and_trust_addin.ps1` | Launch a target model deterministically and wait for bridge confirmation |
| `infra/install_workerhost_service.ps1` | Install WorkerHost as a Windows service |
| `infra/package_revit_bridge_build.ps1` | Package a deployment build |

## Testing

| Script | Purpose |
| --- | --- |
| `testing/test_bridge_smoke.ps1` | Bridge smoke test |
| `testing/test_mcp_smoke.ps1` | MCP smoke test |
| `testing/test_ai_chat_e2e.ps1` | End-to-end AI chat smoke |
| `testing/verify_copilot_runtime_live.ps1` | Live runtime verification |
| `testing/run_revit_mvp_manual_smoke.ps1` | Generate the manual smoke checklist bundle |

## Operational Rules

- Prefer one `Revit.exe` for live mutation/export work.
- Verify `RevitSessionIsolated = true` before mutating a live model.
- Treat `McpHost -> WorkerHost -> kernel` as the canonical IDE path.
