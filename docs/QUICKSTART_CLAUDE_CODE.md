# Quick Start: Connect AI Agents to Revit

This guide gets Claude Code, Cursor, or any MCP-compatible AI agent talking to a live Revit model in under 15 minutes.

## How It Works

```text
IDE (Claude Code / Cursor / VS Code)
        |
        | MCP (JSON-RPC 2.0 over stdio)
        v
  BIM765T.Revit.McpHost          <-- thin adapter, translates MCP to HTTP
        |
        | HTTP (localhost:50765)
        v
  BIM765T.Revit.WorkerHost       <-- AI orchestration, memory, routing
        |
        | Named Pipe
        v
  BIM765T.Revit.Agent            <-- Revit API execution kernel (inside Revit)
```

The AI agent never touches the Revit API directly. Every request goes through a guarded pipeline with preview, approval, and verification steps for mutations.

## Prerequisites

- Windows 10/11 (64-bit)
- .NET 8.0 SDK
- .NET Framework 4.8
- Autodesk Revit 2024 or 2026 (licensed, installed)
- An LLM API key (OpenRouter recommended)

## Step 1: Build

```powershell
git clone https://github.com/meococ/765T-Development.git
cd 765T-Development
dotnet build BIM765T.Revit.Agent.sln -c Release
```

> If you see errors about `RevitAPI.dll`, check that Revit is installed at `C:\Program Files\Autodesk\Revit 2024`. To change the path, edit `Directory.Build.props` and update `Revit2024InstallDir`.

## Step 2: Set Up AI Provider

```powershell
# Interactive setup (recommended)
powershell -ExecutionPolicy Bypass -File .\tools\infra\setup_ai_providers.ps1

# Or set directly (OpenRouter example)
[Environment]::SetEnvironmentVariable('OPENROUTER_API_KEY', 'your-key-here', 'User')
```

Provider priority: OpenRouter > MiniMax > OpenAI > Anthropic. The first key found wins.

## Step 3: Install the Revit Add-in

```powershell
powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1
```

## Step 4: Start WorkerHost

Open a separate terminal and keep it running:

```powershell
cd src\BIM765T.Revit.WorkerHost
dotnet run -c Release
```

Verify it is running:

```powershell
curl http://localhost:50765/health
# Expected: {"status":"ok", ...}
```

## Step 5: Launch Revit Deterministically

From the repo root, launch Revit with the target model through the helper script:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\restart_revit_and_trust_addin.ps1 `
  -ModelPath "C:\path\to\YourModel.rvt" `
  -AutoTrustUnsignedAddin
```

This script:

- closes stray `Revit.exe` processes
- opens the exact target model
- trusts the unsigned add-in prompt when requested
- waits until the bridge reports that exact model path as the active session

Before opening the IDE, verify the live session:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check_bridge_health.ps1 -AsJson
```

Minimum expected state:

- `BridgeOnline = true`
- `ActiveDocument = <your model title>`
- `RevitSessionIsolated = true`

## Step 6: Configure Your IDE

### Claude Code

Add to your Claude Code MCP settings (`~/.claude.json` or via `/mcp add`):

```json
{
  "mcpServers": {
    "revit-agent": {
      "command": "C:/path/to/765T-Development/src/BIM765T.Revit.McpHost/bin/Release/net8.0/BIM765T.Revit.McpHost.exe"
    }
  }
}
```

### Cursor

Create or edit `.cursor/mcp.json` in your project root:

```json
{
  "mcpServers": {
    "revit-agent": {
      "command": "C:/path/to/765T-Development/src/BIM765T.Revit.McpHost/bin/Release/net8.0/BIM765T.Revit.McpHost.exe"
    }
  }
}
```

### Other MCP Clients

Any client that supports MCP stdio transport can connect using the same `BIM765T.Revit.McpHost.exe` binary.

> Replace `C:/path/to/765T-Development` with your actual repo path.

## Step 7: Verify Connection

In your IDE, ask the AI agent:

```text
List all available Revit tools
```

The agent should return a list of available tools. If it does, the connection is working.

## Example Prompts

Once connected, try these:

### Read-Only (Safe)

```text
Show me the current Revit model info - document title, view, element counts.
```

```text
List all walls in the current view with their types and lengths.
```

```text
Get all warnings in the current model.
```

### Analysis

```text
Analyze the current model structure - how many levels, views, sheets?
```

```text
Check all pipe penetrations and show which ones are missing fire-stop families.
```

### Mutations (Preview + Approval Flow)

```text
Create a new floor plan view for Level 2.
```

```text
Place a fire-stop family at all unmarked penetration points.
```

> Mutations go through a `preview -> approval -> execute -> verify` flow. The agent will show you what it plans to do and ask for confirmation before making changes.

## Available Tool Categories

| Category | Examples | Count |
| --- | --- | --- |
| Document & Views | Get document info, list views, active view | ~20 |
| Elements | List elements, filter by category/parameter | ~30 |
| Model Query | Warnings, statistics, spatial queries | ~25 |
| Family Authoring | Create/modify families, parameters | ~15 |
| Mutations | Create/modify/delete elements | ~40 |
| Penetration | Hull analysis, fire-stop workflows | ~20 |
| Session & Memory | Session management, context tracking | ~15 |
| Review & QC | Quality checks, model validation | ~20 |
| Worker | Mission control, planning, orchestration | ~50 |

Total: 237 tools across 14 specialist packs.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| "McpHost.exe not found" | Wrong path in MCP config | Use full absolute path to the built exe |
| "Connection refused" on port 50765 | WorkerHost not running | Start WorkerHost: `cd src\BIM765T.Revit.WorkerHost && dotnet run -c Release` |
| "Kernel pipe unavailable" | Revit not running, add-in not loaded, or the target model is not attached to the bridge yet | Run `.\tools\restart_revit_and_trust_addin.ps1 -ModelPath "C:\path\to\YourModel.rvt" -AutoTrustUnsignedAddin` |
| Tools return empty data | No model open in Revit or the wrong Revit session is active | Run `.\tools\check_bridge_health.ps1 -AsJson` and confirm `ActiveDocument` is correct |
| Reads/mutations attach to the wrong model | Multiple `Revit.exe` processes are open | Close extra Revit sessions or rerun `restart_revit_and_trust_addin.ps1`; verify `RevitSessionIsolated = true` |
| LLM timeout or no response | API key not set or invalid | Run `.\tools\check_ai_readiness.ps1` to verify |
| Build fails on RevitAPI.dll | Revit not installed at expected path | Edit `Directory.Build.props`, update `Revit2024InstallDir` |

## Startup Order

Always start in this order:

1. Install the add-in.
2. Start WorkerHost in a separate terminal.
3. Launch Revit through `restart_revit_and_trust_addin.ps1` with the exact model path.
4. Verify `check_bridge_health.ps1 -AsJson` reports `RevitSessionIsolated = true`.
5. Open the IDE with MCP config pointing to `BIM765T.Revit.McpHost.exe`.

For live mutation/export work, keep exactly one `Revit.exe` open. Multiple Revit sessions can make bridge routing unsafe.

## Architecture

For deeper understanding:

- [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) - system boundaries and ownership model
- [`docs/PATTERNS.md`](PATTERNS.md) - mutation flow and safety patterns
- [`docs/QUICKSTART_AI_TESTING.md`](QUICKSTART_AI_TESTING.md) - HTTP API testing without an IDE
