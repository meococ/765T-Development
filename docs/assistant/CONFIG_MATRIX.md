# Config Matrix

## Repo-Canonical

- `AGENTS.md` → constitution
- `ASSISTANT.md` → repo adapter / operating baseline
- `docs/ARCHITECTURE.md` → system boundary
- `docs/PATTERNS.md` → implementation patterns
- `docs/assistant/*` → current product/runtime truth

## Runtime Config in Repo (seed / dev baseline)

- `src/BIM765T.Revit.Agent/Config/AgentSettings.cs`
  - Revit-side shell/runtime/provider status

- `src/BIM765T.Revit.WorkerHost/Configuration/WorkerHostSettings.cs`
  - WorkerHost ports, state root, SQLite, and vector namespace settings

- `workspaces/default/workspace.json`
  - Seed workspace manifest in repo; installed runtime workspace default at `%APPDATA%\BIM765T.Revit.Agent\workspaces\`
  - Dev/test can override via env var `BIM765T_PROJECT_WORKSPACE_ROOT` or repo-root detection

- `.assistant/config/agent-bridge.json`
  - Repo-local assistant helper config; not the highest-priority product truth

## Machine-Local / Non-Canonical

- `%USERPROFILE%\.codex\config.toml`
- Env vars provider/auth
  - MiniMax should use `MINIMAX_*`; avoid using `OPENAI_BASE_URL` for MiniMax as it can conflict with other tools in the workspace
  - When workspace has multiple provider keys coexisting, set `BIM765T_LLM_PROVIDER=MINIMAX` to pin runtime to MiniMax and avoid drifting to OpenRouter (first-found-wins order: OpenRouter → MiniMax → OpenAI → Anthropic)

- `%APPDATA%\BIM765T.Revit.Agent\workspaces\`
- `%APPDATA%\BIM765T.Revit.Agent\settings.json`
- `%APPDATA%\BIM765T.Revit.Agent\policy.json`
- Revit AppData shadow deploy paths
- `.assistant/runs`, `.assistant/relay`, caches

## Memory-Related Truth

- WorkerHost durable event/memory projection = SQLite
- Agent runtime still carries JSON task/queue/episodic state in the migration window
- Qdrant = vector namespace layer backed by hash embeddings today; if unavailable, system degrades to lexical fallback
