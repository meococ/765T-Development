# 765T Assistant Platform - Architecture

## 1. System Boundary

### Public Lanes

- Web / CLI / MCP / external AI clients
- WorkerHost HTTP + gRPC/named-pipe surfaces

### Private Lane

- `BIM765T.Revit.Agent` runs inside Revit and is the **only** place allowed to mutate the model via Revit API
- In the current migration slice, Agent still hosts the embedded `worker.*` session/reasoning lane

## 2. Control-Plane Model

```
Client -> WorkerHost -> Kernel request -> Revit.Agent -> verify/evidence -> WorkerHost
```

**WorkerHost responsibilities:**

- mission orchestration facade
- capability/pack/workspace resolution
- event store / memory projection
- external AI broker
- reporting / evidence lookup
- bounded planning facade: rule-first intent gate → policy-filtered tool candidates → bounded LLM planner
- current autonomy posture default = `ship`: candidate builder still bootstraps context/rule intent, but planner gets a broader candidate pool, more realistic thresholds, and autonomy/grounding/tool-sequence metadata surfaced to mission/UI for faster product shipping

**Revit.Agent responsibilities:**

- leaf tool execution
- document/view/selection-aware mutation safety
- UI shell inside Revit
- current embedded worker lane in the migration window

## 3. UI Shell

The Revit pane is currently a **chat-first worker shell** centered on a single assistant surface.

- Chat / Commands / Evidence / Activity are important surface logic
- But the current runtime should not be described as 4 dedicated top-level tabs
- Quick actions go through command atlas / command palette
- Large workflows go through planning + preview + approval + verify
- Top bar and dashboard must keep ambient document/view context from Revit; onboarding state must not write "No active model" when a model is open
- WorkerHost is still a sidecar process, but the Revit pane must auto-start hidden, auto-reconnect, and not require the user to open a separate console/tool
- Theme toggle must not lose transcript/session rail; shell chrome can rebuild but worker surface must keep state and rerender to the new theme
- Theme toggle must update chrome cache, footer badges, and transient surfaces like toast/section headers synchronously; dark/light must not diverge for session rail, top bar, or inspector
- Mission stream / toast / progress updates in the pane must prioritize background-friendly dispatcher scheduling to avoid causing Revit to show "processing" for extended periods due to UI rerendering
- WorkerHost mission stream keeps the current SSE contract, but the backend prioritizes push-bus from event append before fallback replay/poll for reconnect
- Timeline render in the worker pane should not force `InvalidateVisual/InvalidateMeasure` on every state update; WPF natural layout + coalesced render is sufficient for transcript/theme refresh
- Read-first research in the pane prioritizes grounded loop: `live Revit context → workspace context bundle → deep scan/artifact/standards/memory evidence`
- Onboarding V1 for grounded AI is: detect → one-click `project.init_preview → project.init_apply`, then `project.deep_scan`
- Current sidebar shell surfaces `recent sessions + project brief + workspace readiness + quick actions` so users can see model/workspace state even when transcript is empty

## 4. Capability Stack

Capabilities are distributed via:

- `packs/`
- `workspaces/`
- `catalog/`

**Workspace root policy for MVP:**

- `workspaces/default/workspace.json` in repo = seed/default manifest for dev/test
- Installed runtime project workspaces default = `%APPDATA%\BIM765T.Revit.Agent\workspaces\`
- Dev/test can override workspace root via env var `BIM765T_PROJECT_WORKSPACE_ROOT` or repo-root detection
- Project init / deep scan / context bundle must read/write to this machine-local root unless there's an explicit override

**Main pack types:**

- standards
- skills
- playbooks
- agents/specialists
- commands
- scripts
- connectors/plugins

## 5. Memory Architecture

### Durable Truth

- WorkerHost event/memory projection = SQLite + lexical fallback
- Agent runtime still has JSON task/queue/episodic state in the migration window
- Embedding layer has extracted `IEmbeddingProvider` from the hash fallback to allow upgrading semantic provider later without changing routing contracts

### Vector Layer

- Qdrant-backed namespaces currently use hash embeddings / non-semantic vectors
- Current namespaces:
  - `atlas-native-commands`
  - `atlas-custom-tools`
  - `atlas-curated-scripts`
  - `playbooks-policies`
  - `project-runtime-memory`
  - `evidence-lessons`

**If Qdrant unavailable:**

- System is still **Ready** if SQLite is operational
- Health must report `lexical_only`

**If Qdrant is reachable with current hash-embedding lane:**

- Health should report `vector_available_non_semantic`

## 6. External AI Broker

WorkerHost is the standard port for external AI. External AI must not call Revit API directly.

All mutation-capable flows must still go through preview/approval/verify.

**`Ship mode`** currently means:

- Reduced friction in planning/UI
- Prioritize direct product-shipping tool chain, script/tool authoring hints, project init/deep-scan, and quick-plan
- Surface `autonomy_mode + chosen_tool_sequence + planner_trace`
- Still keep boundary WorkerHost → private Revit kernel, no open raw code path from planner

**Note:** Public broker lane does not mean the embedded worker lane in Agent has disappeared; both lanes still coexist in the current migration slice.

## 7. Canonical Interaction Loops

1. Atlas Fast Path
2. Workflow Compose
3. Delivery Loop
4. Lesson Promotion

## 8. Target-State Pack

Current runtime truth is in this file.

Target-state modernization/redline pack is at:

- `docs/architecture/README.md`
- `docs/architecture/ARCHITECTURE_REDLINE_2026Q2.md`
- `docs/architecture/adr/*`
