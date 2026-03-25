# 765T Assistant Platform - Patterns

## 1. Tool Boundary

- Tool registration at `Services/Bridge/*ToolModule.cs`
- Tool module registers/orchestrates; does not contain heavy transaction/collector logic
- Revit mutation logic lives in the service layer within `BIM765T.Revit.Agent`

## 2. Approval Pattern

- preview creates summary + approval token + expected context
- execute must validate token/context
- verify must return evidence/residuals
- approval token cache on disk must follow encrypted-only policy; if DPAPI/protection unavailable, skip persist instead of writing plaintext fallback
- selection binding for mutation must not fall back to count-only when selection is non-empty; hash or exact selected ids must match

## 3. Pack/Workspace Pattern

- `pack.json` describes a capability unit
- `workspace.json` decides which packs are active
- canonical pack IDs are neutral, vendor-agnostic
- legacy IDs only used as forward aliases

## 4. Memory Pattern

- always upsert SQLite first
- semantic upsert/search is best-effort
- namespace must be explicit, not a vague single collection
- lexical fallback must stay usable when semantic is unavailable

## 5. External AI Pattern

- External AI talks to WorkerHost `/api/external-ai/*`
- Do not hardcode provider/vendor logic into canonical repo truth
- Provider adapters are just connectivity infrastructure, not product identity
- Bridge file-input safety must not have a bypass env var "unsafe"; if root expansion is needed, use an allowlist env/root policy instead of bypassing entirely
- Planner pattern current truth = rule-first intent gate → candidate builder → bounded LLM planner; LLM only selects/orders tools within the allowlist policy-filtered set, cannot invent tools or raw code paths
- Ship mode current truth = candidate builder bootstraps `candidate_tools + candidate_commands` more broadly (project init/deep scan, command atlas, script/tool authoring hints, review/audit lane) so LLM chooses the right path instead of being locked into rule summary
- Ship mode surfaces `autonomy_mode`, `planner_trace_summary`, `chosen_tool_sequence`, `grounding_level` in mission snapshot and UI trace so users can see what AI is planning to do

## 5b. Grounded Research Pattern

- Read-first pane chat must answer immediately from live Revit context; do not block just because project context hasn't been onboarded
- When workspace exists, prioritize `project.get_context_bundle`; when deep scan is available, also prioritize `project.get_deep_scan` / artifact summary / standards / scoped memory
- Response must state the grounding level: `live_context_only`, `workspace_grounded`, or `deep_scan_grounded`

## 5c. WorkerHost Sidecar UX Pattern

- WorkerHost is a mandatory sidecar for external AI lane, but Revit pane must auto-start hidden when local gateway is not yet up
- Retry/reconnect must be automatic for local loopback gateway; user only sees clear system turn/toast, not generic network error
- Event-stream reconnect only retries a few short times before surfacing a clear warning
- SSE public shape stays the same, but source event prioritizes push bus from event append; polling is only a catch-up lane for reconnect/idle timeout

## 5d. Revit Pane Rendering Pattern

- Theme toggle can rebuild shell chrome, but must not reset worker transcript/session state
- Theme refresh must update chrome cache and transient surfaces (toast, section headers, footer badges, toggle icon/label) synchronously; dark/light must not be stale for any surface currently visible on the pane
- When worker is busy/streaming AI, theme toggle must be deferred or blocked; must not toggle theme and force refresh async ambient/session simultaneously to avoid reentrancy crash in host Revit
- Stream event updates to pane should coalesce by timer and dispatch at appropriate background/render priority; avoid full rerender on every event
- Progress/toast updates must not use synchronous `Dispatcher.Invoke` on pane path; use `BeginInvoke/InvokeAsync` when possible
- Theme rerender and transcript rerender should prioritize WPF natural layout; avoid forcing `InvalidateMeasure/InvalidateVisual` on every mission tick as this can make Revit stutter/"processing"

## 5e. Hub State Shell Pattern

- When transcript has no real chat, pane should show `Project Brief`/`workspace readiness` instead of generic empty cards
- Sidebar quick actions only surface read-first or onboarding-safe actions (`init workspace`, `deep scan`, `project overview`, `smart qc`, `review model`)
- Project brief/readiness card can use heuristic scoring, but wording must state it is a readiness shell from available context bundle/deep scan; do not overclaim audit dashboard or semantic memory as production-ready

## 6. Docs Pattern

- canonical docs at root + `docs/assistant/*`
- `docs/agent/*` is historical/operational memory lane
- if docs and code conflict: fix canonical docs after verifying with code
- target-state architecture decisions recorded separately in `docs/architecture/*`; do not confuse target state with current runtime truth
