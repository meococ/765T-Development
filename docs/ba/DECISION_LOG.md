# Decision Log — 765T BA Pack

| Field | Value |
|-------|-------| 
| **Purpose** | Record the BA decisions that are already locked so the pack stays decision-complete. |
| **Inputs** | Plan decisions, current repo truth, BA phase outputs. |
| **Outputs** | Stable decision history for Product + Engineering. |
| **Status** | Active control document. |
| **Owner** | Product + Engineering |
| **Source refs** | `README.md`, `TRACEABILITY_MATRIX.md`, phase docs. |
| **Last updated** | 2026-03-24 |

| ID | Date | Decision | Why | Impact |
|----|------|----------|-----|--------|
| BA-001 | 2026-03-24 | `docs/ba` is the BA final pack; root product docs remain source inputs. | Avoid mixing vision and BA execution truth. | New read path and governance baseline. |
| BA-002 | 2026-03-24 | Audience is Product + Engineering. | Need execution-grade decisions, not a sales deck. | Documents bias toward scope, acceptance, safety, roadmap. |
| BA-003 | 2026-03-24 | Method is two-pass. | Current repo/docs are enough for Pass 1, but not enough for evidence closure. | Pack can ship now as baseline, then harden later. |
| BA-004 | 2026-03-24 | MVP is limited to 3 slices: project understanding, review/QC, controlled low-risk action. | Best fit with current truth and trust constraints. | Later bets explicitly excluded from MVP. |
| BA-005 | 2026-03-24 | Dynamic code execution in Revit is not an MVP commitment. | Critical safety and platform risk. | Remains a later bet only. |
| BA-006 | 2026-03-24 | Multi-agent scan inside Revit is removed from active scope. | Contradicted by Revit single-threaded constraint. | Scan must be serialized extraction + off-kernel analysis. |
| BA-007 | 2026-03-24 | BA cleanup policy is archive-first, not hard delete. | Preserve history but remove confusion from read path. | `docs/ba/archive/` reserved for superseded BA drafts only. |
