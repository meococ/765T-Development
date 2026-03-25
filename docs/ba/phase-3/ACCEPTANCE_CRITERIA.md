# Acceptance Criteria — BA Phase 3

| Field | Value |
|-------|-------|
| **Purpose** | Define what “done” means for the key MVP slices. |
| **Inputs** | Requirements backlog, story map, safety policy. |
| **Outputs** | High-signal acceptance checks for MVP and pilot readiness. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `REQUIREMENTS_BACKLOG.md`, `AI_TRUST_AND_SAFETY_MATRIX.md`. |
| **Last updated** | 2026-03-24 |

## Slice 1 — Project understanding
- user can request project context without reading raw model stats manually
- response includes summary + evidence/report refs
- no mutation path is triggered
- unsupported data is called out honestly as unknown

## Slice 2 — Review / QC
- user can run review and receive findings grouped by importance
- every major finding links to inspectable evidence
- system never presents score-only output without underlying explanation
- review lane remains read-only

## Slice 3 — Controlled low-risk action
- action cannot execute without preview
- preview is understandable enough for a BIM user to approve/reject
- execute path returns verify/evidence summary
- destructive or ambiguous actions are blocked or pushed out of slice
