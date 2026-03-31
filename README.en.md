# 765T Agentic BIM OS

**[Phien ban tieng Viet → README.md](README.md)**

An AI agent that operates directly inside Autodesk Revit through a guarded local architecture.

![Build](https://img.shields.io/badge/build-passing-brightgreen)
![Tests](https://img.shields.io/badge/tests-250%20passed-brightgreen)
![.NET](https://img.shields.io/badge/.NET-4.8%20%7C%208.0-blue)
![Revit](https://img.shields.io/badge/Revit-2024%20%7C%202026-orange)
![License](https://img.shields.io/badge/license-MIT-green)

## Overview

`765T Agentic BIM OS` is an AI agent system that runs inside Autodesk Revit, allowing AI assistants to interact directly with BIM models through guarded, auditable tools. The entire system runs locally — no model data leaves the machine.

**Primary demo use-case:** An AI agent (Claude Code, Cursor, or any MCP-compatible client) connects to Revit 2026 via named pipes and MCP protocol, reads models, analyzes data, and executes BIM tasks — all controlled from the IDE.

## Architecture

```text
IDE (Claude Code / Cursor / VS Code)
          |
          v
    MCP Protocol (stdio)
          |
          v
    BIM765T.Revit.McpHost ─── JSON-RPC bridge
          |
          v
    BIM765T.Revit.WorkerHost ─── Control plane (net8.0)
    - AI orchestration          - HTTP / gRPC / SSE
    - Memory projection         - SQLite + Qdrant
    - External AI gateway       - LLM routing
          |
          v
    Named-pipe kernel channel
          |
          v
    BIM765T.Revit.Agent ─── Execution kernel (net48)
    - Revit API boundary        - 237 guarded tools
    - ExternalEvent scheduler   - Preview/Approve/Execute flow
    - WPF dockable pane         - Operation journal
```

**Critical boundary:** Only `BIM765T.Revit.Agent` is allowed to call the Revit API. This constraint is enforced automatically by Architecture Tests.

## Projects

| Project | Framework | Role |
| --- | --- | --- |
| `BIM765T.Revit.Agent` | net48 / WPF | Revit add-in, execution kernel, 237 guarded tools |
| `BIM765T.Revit.WorkerHost` | net8.0 | Control plane, AI orchestration, memory, external AI gateway |
| `BIM765T.Revit.Bridge` | net8.0 | CLI bridge over named pipes |
| `BIM765T.Revit.McpHost` | net8.0 | MCP stdio adapter — lets any IDE connect to Revit |
| `BIM765T.Revit.Copilot.Core` | netstandard2.0 | AI services, LLM routing, pack management |
| `BIM765T.Revit.Contracts` | netstandard2.0 | Shared DTOs and contracts |
| `BIM765T.Revit.Agent.Core` | netstandard2.0 | Core logic decoupled from Revit API |

## Key Features

- **237 guarded tools** across 14 specialist packs — read, modify, analyze BIM models
- **Mutation safety flow:** Preview -> Approval -> Execute -> Verify for every dangerous operation
- **LLM provider cascade:** OpenRouter -> MiniMax -> OpenAI -> Anthropic (first-found-wins)
- **Conversational fast-path:** 7 intent categories respond in 1-3s instead of 5-18s
- **Semantic memory:** SQLite (durable) + Qdrant (vector search) + Ollama fallback
- **MCP integration:** Any AI IDE that supports MCP can connect to Revit
- **Centralized timeout config:** `LlmTimeoutProfile` unifies timeout/token settings across the entire codebase

## Demo: AI Agent Interacting With Revit 2026

This system lets you:

1. **Open Revit 2026** with any BIM model
2. **Open your IDE** (VS Code + Claude Code, Cursor, or terminal)
3. **Connect via MCP** — the IDE auto-detects Revit context
4. **Issue natural language commands:**
   - "List all Walls in the current view"
   - "Check the model for warnings"
   - "Create a new Floor Plan for level 2"
   - "Analyze material statistics"

The AI agent reads Revit context, plans the approach, requests approval (when needed), and executes — all from inside the IDE.

## Prerequisites

| Requirement | Version |
| --- | --- |
| **Windows** | 10 / 11 (64-bit) |
| **.NET SDK** | 8.0+ |
| **.NET Framework** | 4.8 (for Revit add-in) |
| **Autodesk Revit** | 2024 or 2026 (licensed) |
| Docker + Qdrant | Optional — falls back to lexical search if unavailable |
| Ollama | Optional — falls back to hash embeddings if unavailable |

> **Note:** This system is Windows-only because Revit is a Windows application.

## Quick Start

### 1. Build

```powershell
dotnet build BIM765T.Revit.Agent.sln -c Release
```

### 2. Set Up AI Provider

```powershell
# Interactive setup (recommended)
.\tools\infra\setup_ai_providers.ps1

# Or pass key directly
.\tools\infra\setup_ai_providers.ps1 -Provider openrouter -OpenRouterKey "sk-or-..."
```

### 3. Deploy Revit Add-in

```powershell
powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1
```

### 4. Start WorkerHost

```powershell
dotnet run --project src/BIM765T.Revit.WorkerHost -c Release
```

### 5. Connect From IDE (Claude Code / Cursor)

Add to your IDE's MCP config (Claude Code: `~/.claude.json`, Cursor: `.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "revit-agent": {
      "command": "<REPO_ROOT>/src/BIM765T.Revit.McpHost/bin/Release/net8.0/BIM765T.Revit.McpHost.exe"
    }
  }
}
```

Replace `<REPO_ROOT>` with the actual path to the repo on your machine.

> **Startup order:** Revit (with add-in loaded) → WorkerHost → IDE MCP client.

See detailed guide: [`docs/QUICKSTART_CLAUDE_CODE.md`](docs/QUICKSTART_CLAUDE_CODE.md)

## Testing

```powershell
# Run all tests
dotnet test BIM765T.Revit.Agent.sln -c Release

# Result: 250 passed, 0 failed
```

## Current Status

| Component | Status |
| --- | --- |
| Kernel + 237 tools | Production-ready |
| Named pipe IPC | Production-ready |
| WorkerHost (HTTP/gRPC/SSE) | Production-ready |
| MCP bridge | Production-ready |
| CLI bridge | Production-ready |
| Conversational fast-path | Shipped |
| Semantic memory (Qdrant) | Shipped |
| Centralized config | Shipped |
| WPF chat pane | Alpha (disabled by default — `EnableUiPane = false`) |
| Standalone chat (no Revit) | In progress |
| Role-based tool filtering | Planned |

## Documentation

| Document | Content |
| --- | --- |
| `CLAUDE.md` | Guidance for AI agents working with this repo |
| `AGENTS.md` | Operating constitution |
| `docs/ARCHITECTURE.md` | System architecture and boundaries |
| `docs/PATTERNS.md` | Implementation patterns and mutation safety flow |
| `docs/assistant/BASELINE.md` | Current runtime truth |
| `docs/assistant/CONFIG_MATRIX.md` | Configuration ownership matrix |
| `docs/QUICKSTART_AI_TESTING.md` | End-to-end AI testing guide |
| `docs/INDEX.md` | Full documentation navigator |

## License

MIT License. Copyright (c) 2026 Meo Coc (meococ). See [LICENSE](LICENSE) for details.
