# MVP Manual Smoke

Use the helper below to generate a machine-local smoke bundle:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_revit_mvp_manual_smoke.ps1
```

The bundle captures:

- runtime paths
- local settings snapshot
- WorkerHost health
- bridge health
- a checklist
- an observations template

## Manual Expectations

- first-open onboarding is visible on a clean workspace
- init and deep-scan create the expected workspace artifacts
- resume works after closing and reopening Revit
- read flows stream without mutating
- mutation flows follow `preview -> approval -> execute -> verify`

Use the generated `checklist.md` and `observations.md` in the artifact directory as the operator record.
