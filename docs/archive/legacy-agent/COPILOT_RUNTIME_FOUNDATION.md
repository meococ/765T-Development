# Copilot Runtime Foundation (vNext bootstrap)

> Historical reference only. Current startup/runtime truth lives in `../assistant/*`, `../ARCHITECTURE.md`, and `../PATTERNS.md`.


## Truth-first status
This is **not** the full multi-process copilot runtime yet.

What exists now is a **foundation slice** that keeps the current Revit safety kernel intact and adds the first durable/task-context layer on top of it.

## What was added
- Durable task-run store under `%APPDATA%\\BIM765T.Revit.Agent\\state`
- Task-level APIs that wrap existing `workflow` and `fix_loop` execution
- Context-broker primitives:
  - hot state
  - anchor search
  - bounded bundle resolution
  - artifact summary
  - similar-run lookup
  - tool lookup by capability
- Lightweight document state graph snapshot for hot context
- Runtime health / queue-state surfaces for observability
- Pure `BIM765T.Revit.Copilot.Core` project for non-Revit durable/context logic

## Current architecture (implemented)

```text
Codex / Claude / CLI / MCP
          |
          v
   BIM765T task/context APIs
          |
          v
Agent (net48, Revit add-in)
  - safety kernel
  - Revit execution worker
  - task runtime adapter
  - model state graph snapshot
          |
          v
Revit API / ExternalEvent / UI thread
```

## Important limitations
- Durable store is currently **file-backed JSON**, not SQLite yet.
- The new runtime is still **embedded in the Agent** for now; it is not yet split into a separate net8 process.
- `task.*` currently supports only:
  - `workflow`
  - `fix_loop`
- The model state graph is a **hot snapshot**, not a continuously maintained world model yet.
- Context anchors currently come from:
  - stored task runs
  - promoted memory records
  They do not yet index full docs/logs/artifact corpora.

## Why this slice matters
This slice creates the durable/task-centric seam needed for the bigger architecture without rewriting the current guarded tool substrate.

It moves the system from:
- raw tool calling + ad-hoc workflow state

toward:
- resumable task runs
- bounded context retrieval
- verifier-friendly run history
- context-efficient AI control loops

## Next recommended steps
1. Split planner/verifier/context-broker orchestration into a separate net8 local runtime process.
2. Replace file-backed run indexing with SQLite metadata + JSONL/file artifacts.
3. Promote model state graph from hot snapshot to incremental document graph.
4. Route more agent work through `task.*` instead of raw tool chains.
5. Add durable step checkpoints and resume/recovery branches.

## Live verification target
Use:
- `tools/check_bridge_health.ps1 -Profile copilot`
- `tools/verify_copilot_runtime_live.ps1`

after rebuilding and restarting Revit so runtime tool count matches source.


## Latest live verification
- Proof artifact: `artifacts/copilot-runtime-live/20260318-065619`
- Verified together in one session:
  - `session.get_runtime_health -> SupportsCheckpointRecovery = true`
  - `context.get_hot_state -> READ_SUCCEEDED`
  - durable task summary shows non-zero checkpoint/recovery counts
- For this wave, no further restart is pending after the above verification.

## Checkpoint / recovery status
- Durable task runs now persist Checkpoints, RecoveryBranches, and LastErrorCode / LastErrorMessage.
- 	ask.resume now follows recovery branches instead of blind retry.
- This is still a task-runtime-level checkpoint model, not yet a full workflow-graph crash-replay engine.
