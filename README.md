# 765T Agentic BIM OS

`BIM765T-Revit-Agent` is the repo for an AI agent that operates inside Autodesk Revit through a guarded local architecture.

## Current Product Slice

- Revit UI: chat-first single worker shell with one assistant surface
- Execution boundary: `BIM765T.Revit.Agent` is the only layer that touches the Revit API
- Control plane: `BIM765T.Revit.WorkerHost` handles orchestration, memory projection, and external AI access
- Interaction model: rule-first workflows with tiered mutation safety
- Durable truth: SQLite
- Vector layer today: Qdrant with hash embeddings and lexical fallback, not true semantic memory yet

For current runtime truth, read `docs/assistant/BASELINE.md`.

## Architecture At A Glance

```text
Client / CLI / MCP / external AI
            |
            v
      BIM765T.Revit.WorkerHost
      - control plane
      - routing and orchestration
      - memory projection
      - external AI gateway
            |
            v
      named-pipe kernel channel
            |
            v
      BIM765T.Revit.Agent
      - execution kernel
      - Revit API boundary
      - WPF worker shell
      - preview / approval / execute / verify path
```

## Projects

| Project | Framework | Responsibility |
| --- | --- | --- |
| `BIM765T.Revit.Agent` | net48 / WPF | Revit add-in, Revit API execution, worker shell |
| `BIM765T.Revit.WorkerHost` | net8.0 | Control plane, orchestration, memory projection, external AI gateway |
| `BIM765T.Revit.Bridge` | net8.0 | CLI bridge over named pipes |
| `BIM765T.Revit.McpHost` | net8.0 | MCP stdio adapter |
| `BIM765T.Revit.Copilot.Core` | netstandard2.0 | Shared AI, pack, and routing services |
| `BIM765T.Revit.Contracts*` | netstandard2.0 | Shared DTO and contract layer |
| `BIM765T.Revit.Agent.Core` | netstandard2.0 | Core agent logic without Revit API dependency |

## Current Capabilities

- WPF dockable pane running as a single worker shell
- WorkerHost HTTP, gRPC, and SSE surfaces
- Named-pipe bridge between WorkerHost and Revit.Agent
- Rule-first tool execution and bounded planning
- Preview, approval, execute, and verify flow for high-impact mutations
- Session and memory projection backed by SQLite

## Mutation Quality Flow

The current UX is tiered, not one-size-fits-all:

- Read-only or harmless actions can use a quick path.
- Deterministic mutations use preview or light confirm based on policy.
- High-impact mutations use preview, approval, execute, and verify.

For the exact pattern, read `docs/PATTERNS.md` and `docs/assistant/BASELINE.md`.

## Memory And AI

- SQLite is the durable event and memory projection layer.
- Qdrant is currently a vector layer backed by hash embeddings.
- If Qdrant is unavailable, the system can fall back to lexical-only behavior.
- Provider selection is environment-driven.
- The runtime can be pinned with `BIM765T_LLM_PROVIDER`.
- Current repo examples and tests commonly use the MiniMax lane, but provider choice is still configuration, not product identity.

## Build And Test

```powershell
dotnet build BIM765T.Revit.Agent.sln -c Release
dotnet test BIM765T.Revit.Agent.sln -c Release
powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1
```

## Documentation Lanes

- `CLAUDE.md` - repo-specific critical notes and latest working guidance
- `AGENTS.md` - operating constitution
- `ASSISTANT.md` - repo adapter and assistant baseline
- `docs/765T_PRODUCT_VISION.md` - product direction and target state
- `docs/ARCHITECTURE.md` - system boundary and ownership model
- `docs/PATTERNS.md` - implementation patterns and safety flow
- `docs/assistant/BASELINE.md` - current runtime truth
- `docs/assistant/CONFIG_MATRIX.md` - config ownership
- `docs/INDEX.md` - documentation navigator

## Read Order

1. `CLAUDE.md`
2. `README.md` or `README.en.md`
3. `AGENTS.md`
4. `ASSISTANT.md`
5. `docs/765T_PRODUCT_VISION.md`
6. `docs/ARCHITECTURE.md`
7. `docs/PATTERNS.md`
8. `docs/assistant/BASELINE.md`
9. `docs/assistant/CONFIG_MATRIX.md`

## License

Proprietary. Built by the MII development team.
