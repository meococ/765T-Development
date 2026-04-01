# 765T Revit Tooling

PowerShell automation scripts for development, testing, and operations.

## Directory Structure

```text
tools/
  *.ps1                   # Core operational scripts (health, relay, catalog, audit)
  Assistant.Common.ps1    # Shared helpers — path discovery, bridge resolution
  infra/                  # Install, start, setup scripts (WorkerHost, Qdrant, add-in)
  testing/                # Test, verify, smoke, benchmark scripts
  round/                  # Round/penetration workflow scripts (domain-specific)
  health/                 # Health check scripts (agent stack, pack/workspace)
  dev/                    # Development utilities
    scratch/              # Temporary investigation scripts (underscore-prefixed originals)
    export-pack-catalog.ps1
  workflows/              # Workflow documentation
  archive/                # Legacy/superseded scripts
    legacy-assistant/     # Old assistant-era automation
```

## Quick Reference

### Operations (root level)

| Script | Purpose |
|--------|---------|
| `check_bridge_health.ps1` | Validate bridge runtime, tool coverage, and whether routing is safe for a single isolated Revit session |
| `check_workerhost_health.ps1` | Validate WorkerHost health JSON |
| `check_ai_readiness.ps1` | Check AI provider availability |
| `check_tool_registry.ps1` | Audit tool manifests for missing metadata |
| `generate_tool_catalog.ps1` | Export live tool catalog to JSON/Markdown |
| `audit_agent_stack.ps1` | Repo/environment audit for stale docs, config drift |
| `invoke_external_ai_agent.ps1` | Call external AI broker through WorkerHost |
| `relay_*.ps1` | Relay send/receive/reply/status |

### Infrastructure (`infra/`)

| Script | Purpose |
|--------|---------|
| `install_workerhost_service.ps1` | Install WorkerHost as Windows service |
| `start_workerhost.ps1` | Start WorkerHost interactively |
| `install_qdrant_startup_task.ps1` | Scheduled task for local Qdrant |
| `start_qdrant_local.ps1` | Start local Qdrant |
| `setup_ai_providers.ps1` | Configure LLM provider env vars |
| `restart_revit_and_trust_addin.ps1` | Close stray Revit processes, open a target model, trust the add-in prompt, and wait for bridge confirmation on that exact model |
| `package_revit_bridge_build.ps1` | Package bridge build for deployment |

### Testing (`testing/`)

| Script | Purpose |
|--------|---------|
| `test_bridge_smoke.ps1` | Bridge smoke test |
| `test_mcp_smoke.ps1` | MCP smoke test |
| `test_ai_chat_e2e.ps1` | End-to-end AI chat test |
| `run_revit_mvp_manual_smoke.ps1` | Generate MVP manual smoke bundle |
| `run_family_authoring_benchmark.ps1` | Family authoring benchmark |
| `verify_copilot_runtime_live.ps1` | End-to-end runtime smoke |
| `check_coverage_thresholds.ps1` | Test coverage thresholds |

### Round/Penetration Workflows (`round/`)

Domain-specific scripts for round transition, penetration review, IFC export, and related workflows. See individual scripts for usage.

## Conventions

- Use `Assistant.Common.ps1` for path discovery and shared helpers
- Scripts should be idempotent where possible
- JSON output and stable exit codes for automation
- New scripts go in the appropriate subdirectory, not root
- For live mutation/export work, prefer a single `Revit.exe` session and verify `check_bridge_health.ps1` reports `RevitSessionIsolated = true`
