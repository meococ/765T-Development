# ASSISTANT.md

This file is the repo adapter and operating baseline for assistant lanes working in `BIM765T-Revit-Agent`.

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

## Source Of Truth Map

| Topic | Canonical file |
| --- | --- |
| Startup notes | `CLAUDE.md` |
| Operating constitution | `AGENTS.md` |
| Current runtime truth | `docs/assistant/BASELINE.md` |
| Config ownership | `docs/assistant/CONFIG_MATRIX.md` |
| Architecture boundary | `docs/ARCHITECTURE.md` |
| Implementation patterns | `docs/PATTERNS.md` |
| Product direction | `docs/765T_PRODUCT_VISION.md` |
| BA scope and pilot lane | `docs/ba/*` |
| Historical operational memory | `docs/agent/*` |

## Runtime Truths To Know

- `src/BIM765T.Revit.Agent/Config/AgentSettings.cs` is the Revit-side runtime configuration source.
- `src/BIM765T.Revit.WorkerHost/Configuration/WorkerHostSettings.cs` is the WorkerHost runtime configuration source.
- `workspaces/default/workspace.json` is the repo seed workspace manifest.
- Installed project workspaces default to `%APPDATA%\\BIM765T.Revit.Agent\\workspaces\\`.
- `BIM765T.Revit.Agent` is the execution kernel and the only approved Revit API caller.
- `BIM765T.Revit.WorkerHost` is the control plane and external AI gateway.
- Current UI posture is a single worker shell, not a guaranteed multi-tab shell.
- Current durable truth is SQLite plus lexical projection.
- Current Qdrant usage is hash-embedding / non-semantic vector support with lexical fallback.
- Embedded worker lane inside the Agent still exists during the migration window.

## Operational Posture

- Be proactive. Find the right canonical source before editing.
- Prefer grounded statements over polished but stale claims.
- Keep read-only paths fast and low-friction.
- Keep mutation paths tiered:
  - quick path for harmless actions
  - preview or light confirm for deterministic mutations
  - preview, approval, execute, verify for high-impact changes
- Treat provider choice as configuration, not as product identity.
- When in doubt, follow `docs/assistant/BASELINE.md` for current state and `docs/765T_PRODUCT_VISION.md` for direction.

## Not Startup Truth

Do not treat these as canonical startup truth unless a canonical doc explicitly sends you there:

- `.assistant/runs/*`
- `.assistant/relay/*`
- `.assistant/context/*.json`
- temporary handoff files
- generated reports under `workspaces/`
- scratch files under `tools/dev/scratch/`
- `docs/archive/*`

## Assistant Lane Rule

Assistant-specific prompts, commands, and overlays under `.assistant/*` or pack assets may extend the workflow, but they must inherit this repo truth. They do not get to redefine runtime truth, architecture truth, or the product posture on their own.
