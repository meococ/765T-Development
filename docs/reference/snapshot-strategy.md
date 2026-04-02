# Snapshot Strategy

The product exposes snapshot-style read flows so operators can inspect model state without mutating the document.

## Intended Use

- capture active-view context for review
- generate evidence for troubleshooting
- compare preview output and post-execution state
- preserve lightweight artifacts for handoff and QA

## Typical Artifacts

- JSON summaries
- image captures when supported
- review or diff summaries

## Operational Rule

Snapshots are read-side evidence.
They do not replace approval or verification for mutation workflows.

For mutation work, keep using:

```text
preview -> approval -> execute -> verify
```
