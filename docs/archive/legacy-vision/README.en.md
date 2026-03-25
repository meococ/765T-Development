# 765T Agentic BIM OS

> *Ship AI Agent to you — BIM without limits.*

**BIM765T-Revit-Agent** is an intelligent BIM assistant that runs inside Autodesk Revit. It combines a chat-first interface with AI-powered automation to help architects, engineers, and BIM managers work faster and smarter.

---

## What It Does

- **Chat with your Revit model.** Ask questions, give commands, get results — all from a dockable pane inside Revit.
- **AI-powered automation.** Rename views, audit families, review standards, export sheets — multi-step workflows handled by AI agents.
- **Real-time activity streaming.** Watch the AI think, plan, scan, and execute step by step — full transparency.
- **Project memory.** The system learns your project context, naming conventions, and preferences over time.
- **Multi-LLM support.** Works with OpenRouter, OpenAI, Anthropic, MiniMax, or offline rule-based mode.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Revit 2024+                             │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  BIM765T.Revit.Agent (net48 / WPF)                     │   │
│  │  Execution kernel — only layer touching Revit API       │   │
│  │  Dockable pane UI · ExternalEvent scheduler             │   │
│  └─────────────────────────────────────────────────┬───────┘   │
│                                                    │           │
│                                     Named pipes    │           │
│                                                    ▼           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  BIM765T.Revit.WorkerHost (net8.0 / ASP.NET Core)      │   │
│  │  Control plane — orchestration, memory, AI routing      │   │
│  │  SQLite event store · Qdrant vector memory · SSE        │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐        │
│  │ Revit.Bridge │  │ Revit.McpHost│  │ Copilot.Core │        │
│  │ CLI client   │  │ MCP stdio    │  │ AI services  │        │
│  └──────────────┘  └──────────────┘  └──────────────┘        │
└─────────────────────────────────────────────────────────────────┘
```

## Projects

| Project | Framework | Role |
|---------|-----------|------|
| `BIM765T.Revit.Agent` | net48 / WPF | Revit add-in — all Revit API calls, dockable pane UI |
| `BIM765T.Revit.WorkerHost` | net8.0 | Orchestration — AI routing, event store, vector memory |
| `BIM765T.Revit.Bridge` | net8.0 | CLI bridge via named pipes |
| `BIM765T.Revit.McpHost` | net8.0 | MCP stdio JSON-RPC adapter |
| `BIM765T.Revit.Copilot.Core` | netstandard2.0 | AI services — LLM, memory, routing, packs |
| `BIM765T.Revit.Contracts*` | netstandard2.0 | Shared DTOs and proto contracts |
| `BIM765T.Revit.Agent.Core` | netstandard2.0 | Core agent services (no RevitAPI dependency) |

## Key Features

### Real-time Activity Streaming (765T Flow)

See exactly what the AI is doing, step by step:

```
[THINKING] Analyzing request...
[PLAN]     Finding MEP views on Level 1...
[SCAN]     234 views scanned
[CHECK]    Checking against naming standards...
[PREVIEW]  23 views to rename — awaiting your confirmation
[RUN]      Renaming view 1/23...
[DONE]     23 views renamed successfully
```

### Smart Onboarding

- Auto-detects project context on first open
- Scans Views, Sheets, Families, Worksets, Links
- Generates a 1-page Project Brief (not a data dump)
- Learns user role and preferences over time

### Project Memory (765T Hub)

- SQLite durable event store as source of truth
- Qdrant vector memory with namespace isolation
- Workspace stored at `%APPDATA%\BIM765T.Revit.Agent\workspaces\`
- Bilingual support: Vietnamese + English

### Mutation Workflow

Every model change goes through a quality flow:

1. **Preview** — dry-run showing exact changes before anything happens
2. **Approval** — token-based confirmation (5-min expiry)
3. **Execute** — runs on Revit UI thread via ExternalEvent
4. **Verify** — automatic post-execution confirmation

## Memory Architecture

```
User Message
    │
    ▼
┌─────────────────┐     ┌─────────────────┐
│ RetrieverAgent  │────▶│  Qdrant (live)  │
│ (top-3 hits)    │     │  vector search  │
└────────┬────────┘     └─────────────────┘
         │ fallback
         ▼
┌─────────────────┐
│ SQLite (durable)│
│ lexical search  │
└─────────────────┘
         │
         ▼
   Prompt context
```

Memory namespaces: `atlas-native-commands`, `atlas-custom-tools`, `atlas-curated-scripts`, `playbooks-policies`, `project-runtime-memory`, `evidence-lessons`

## LLM Provider Support

Configured via environment variables (first match wins):

| Provider | Env Vars |
|----------|----------|
| OpenRouter | `OPENROUTER_API_KEY`, `OPENROUTER_PRIMARY_MODEL` |
| MiniMax | `MINIMAX_API_KEY`, `MINIMAX_MODEL` |
| OpenAI | `OPENAI_API_KEY`, `OPENAI_MODEL` |
| Anthropic | `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_MODEL` |
| Rule-first (offline) | No key needed |

## Build & Test

```powershell
# Build all projects
dotnet build BIM765T.Revit.Agent.sln -c Release

# Run tests
dotnet test BIM765T.Revit.Agent.sln -c Release

# Deploy add-in to Revit
powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1
```

## Configuration

| Setting | Location |
|---------|----------|
| Repo defaults | `docs/assistant/CONFIG_MATRIX.md` |
| Revit runtime | `src/BIM765T.Revit.Agent/Config/AgentSettings.cs` |
| WorkerHost runtime | `src/BIM765T.Revit.WorkerHost/Configuration/WorkerHostSettings.cs` |
| Workspace seed | `workspaces/default/workspace.json` |
| User workspaces | `%APPDATA%\BIM765T.Revit.Agent\workspaces\` |

## External AI API

WorkerHost exposes HTTP endpoints for external AI clients:

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/external-ai/status` | Health check |
| POST | `/api/external-ai/chat` | Submit chat message |
| GET | `/api/external-ai/missions/{id}/events` | SSE event stream |
| POST | `/api/external-ai/missions/{id}/approve` | Approve mutation |
| POST | `/api/external-ai/missions/{id}/reject` | Reject mutation |
| POST | `/api/external-ai/missions/{id}/cancel` | Cancel mission |

## Documentation

| Doc | Purpose |
|-----|---------|
| `README.md` | Project overview (this file) |
| `AGENTS.md` | Operating constitution — how agents work in this repo |
| `ASSISTANT.md` | Assistant baseline and runtime truths |
| `docs/ARCHITECTURE.md` | 5-layer architecture deep dive |
| `docs/PATTERNS.md` | Implementation patterns and conventions |

## License

Proprietary. Built by the MII development team.
