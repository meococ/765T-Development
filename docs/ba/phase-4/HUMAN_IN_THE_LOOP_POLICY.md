# Human in the Loop Policy — BA Phase 4

| Field | Value |
|-------|-------|
| **Purpose** | Define exactly when the system may act automatically and when a human must approve. |
| **Inputs** | Architecture/patterns docs, requirements backlog, trust/safety matrix. |
| **Outputs** | Decision rules for automation vs approval. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `AI_TRUST_AND_SAFETY_MATRIX.md`, `REQUIREMENTS_BACKLOG.md`. |
| **Last updated** | 2026-03-24 |

| Action class | Rule | Example |
|-------------|------|---------|
| Read-only | auto-run allowed | query, inspect, context summary, QC review |
| Deterministic low-risk mutation | preview required, light confirm required, verify required | rename, parameter fill, bounded sheet/view layout ops |
| High-impact / destructive | not in MVP; if later, requires stronger approval and verify | purge, delete broad sets, central-sensitive operations |
| Unsafe / unbounded | disallowed | arbitrary code execution in Revit |

## Policy notes

- Persona/tone does not change safety class.
- “Fast path” must still respect current trust classification.
- If preview is ambiguous, the action is not eligible for MVP automation.
