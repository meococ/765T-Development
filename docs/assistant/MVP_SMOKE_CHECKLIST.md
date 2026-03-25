# MVP Revit Smoke Checklist

This document defines the manual smoke path for the MVP 765T shipping on the current stack:

- `BIM765T.Revit.Agent` (`net48`) inside Revit
- `BIM765T.Revit.WorkerHost` (`net8`) for control plane / broker
- Onboarding + project context + worker flow standardized in WPF shell

## Canonical Runtime Paths

- Runtime app root: `%APPDATA%\BIM765T.Revit.Agent\`
- Project workspace root: `%APPDATA%\BIM765T.Revit.Agent\workspaces\`
- WorkerHost state root: `%APPDATA%\BIM765T.Revit.Agent\workerhost\`
- Agent JSON runtime state root (migration window): `%APPDATA%\BIM765T.Revit.Agent\state\`
- Logs: `%APPDATA%\BIM765T.Revit.Agent\logs\`
- Repo workspace seed: `workspaces/default/workspace.json`

`workspaces/default/workspace.json` is only a repo seed/dev fixture. Runtime onboarding, `project.init_*`, `project.deep_scan`, context bundle, and resume session must default to read/write from `%APPDATA%\BIM765T.Revit.Agent\workspaces\`. Dev/test can override this root via env var `BIM765T_PROJECT_WORKSPACE_ROOT` or repo-root detection, but installed runtime default is still `%APPDATA%\BIM765T.Revit.Agent\workspaces\`.

## Preflight

1. Build and install add-in:

   ```powershell
   dotnet build .\BIM765T.Revit.Agent.sln -c Release
   powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1
   ```

2. Start WorkerHost via service or foreground dev mode:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\start_workerhost.ps1
   ```

3. Use a test model that can be safely mutated. Do not smoke mutation on a production central model.

4. To verify preview/approval for mutation, set `AllowWriteTools = true` in `%APPDATA%\BIM765T.Revit.Agent\settings.json`.

5. To rerun first-open onboarding, delete or archive the corresponding workspace under `%APPDATA%\BIM765T.Revit.Agent\workspaces\`.

## Smoke Bundle Helper

Use the helper to create artifact bundle + preflight snapshot before manual operations in Revit:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_revit_mvp_manual_smoke.ps1
```

Artifact bundle defaults:

- `artifacts/revit-mvp-manual-smoke/<timestamp>/context.json`
- `artifacts/revit-mvp-manual-smoke/<timestamp>/summary.json`
- `artifacts/revit-mvp-manual-smoke/<timestamp>/preflight.workerhost.json`
- `artifacts/revit-mvp-manual-smoke/<timestamp>/preflight.bridge.json`
- `artifacts/revit-mvp-manual-smoke/<timestamp>/checklist.md`
- `artifacts/revit-mvp-manual-smoke/<timestamp>/observations.md`

## Manual Scenarios

### 1. First-Open Onboarding

- Open Revit 2024 + test model when workspace for that model does not exist yet.
- Open 765T pane.
- Confirm UI does not show empty chat.

**Expected:**

- Onboarding / welcome card shows instead of empty shell
- Workspace badge no longer hardcoded to `default`
- Deep-scan badge reflects `NotStarted`, `Pending`, or corresponding onboarding state
- If sending first NL prompt, the first stage user sees in 765T Flow must be `Thinking`, not `Scan`

### 2. Init + Deep Scan

- Run onboarding → `project.init_preview` / `project.init_apply` following the UI flow.
- Continue with `project.deep_scan`.

**Expected:**

- Workspace created under `%APPDATA%\BIM765T.Revit.Agent\workspaces\`
- Has `workspace.json`, `project.context.json`, `reports/project-init.*`, `reports/project-brain.deep-scan.*`, `memory/project-brief.md`
- Deep-scan badge updates per actual state, not hardcoded values

### 3. Resume Session

- After having a workspace + session, close Revit completely.
- Reopen same model and re-open 765T pane.

**Expected:**

- UI recognizes existing workspace and shows resume card instead of first-open onboarding
- Session/workspace badge matches the created workspace, not `default`
- Pending approval or session context (if any) restored via `worker.list_sessions` / `worker.get_session`

### 4. Flow Streaming

- Send a read-only prompt, e.g. `review model health`.
- Send a safe mutation prompt, e.g. duplicate active view or create sheet on the test model.

**Expected:**

- Flow stage for read-only prompt starts with `Thinking`, then `Plan`, `Scan`, `Run`, `Verify`, `Done` if applicable
- Flow stage for mutation must go through `Preview` → `Approval` before `Run`
- `sheet.place_views_safe` must ask for more context if input is insufficient, not auto-execute

### 5. Mutation Safety

Must cover at minimum these commands:

- `sheet.create_safe`
- `sheet.renumber_safe`
- `view.duplicate_safe`
- `sheet.place_views_safe`

**Expected:**

- `sheet.create_safe`, `sheet.renumber_safe`, `view.duplicate_safe` must have dry-run/preview, not mutate before approve
- Reject/cancel must not leave half-changes behind
- Approve only then allows execute, then `Verify` must reflect actual results
- `sheet.place_views_safe` with sufficient context only previews; missing context means agent asks instead of running blindly

## Evidence Capture

- Screenshot onboarding card
- Screenshot resume card
- Screenshot flow stage starting with `Thinking`
- Screenshot preview + approval for at least 1 mutation
- File `summary.json` + `observations.md` from helper script
- If any fail, attach log at `%APPDATA%\BIM765T.Revit.Agent\logs\`
