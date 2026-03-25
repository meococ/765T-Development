# Requirements Backlog — BA Phase 3

| Field | Value |
|-------|-------|
| **Purpose** | Decision-complete requirement list for MVP and pilot. |
| **Inputs** | Story map, current truth docs, assumptions, feature inventory. |
| **Outputs** | Requirement backlog with actor, trigger, output, safety class, and release target. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `ACCEPTANCE_CRITERIA.md`, `NFR_AND_CONSTRAINTS.md`, `MVP_DEFINITION.md`. |
| **Last updated** | 2026-03-24 |

| ID | Requirement | Actor | Trigger | Expected output | Safety class | Release target |
|----|-------------|-------|---------|-----------------|-------------|----------------|
| RQ-001 | Build/retrieve project context bundle | BIM Manager / BIMer | open project or request context | evidence-backed project brief + refs | read-only | MVP Slice 1 |
| RQ-002 | Run deep scan on bounded scope | BIM Manager | explicit scan request | summary + report path + findings | read-only | MVP Slice 1 |
| RQ-003 | Query and inspect model entities | BIMer | ask/inspect request | grounded answer with refs | read-only | MVP Slice 1 |
| RQ-004 | Run smart QC review | BIM Manager | pre-issue or review session | prioritized findings with evidence | read-only | MVP Slice 2 |
| RQ-005 | Inspect sheet/family/schedule intelligence | BIM Manager / BIMer | follow-up review | detail evidence for finding | read-only | MVP Slice 2 |
| RQ-006 | Preview low-risk action before execution | BIMer | bounded action selected | preview delta + confirm gate | deterministic low-risk | MVP Slice 3 |
| RQ-007 | Execute only after explicit confirm for low-risk actions | BIMer | user confirms preview | applied change + verify result | deterministic low-risk | MVP Slice 3 |
| RQ-008 | Persist evidence and next-step summary | All | after scan/review/action | reusable evidence artifact | read-only | MVP Slice 1–3 |
| RQ-009 | Keep connector/reporting outside core MVP unless directly needed | Product + Eng | scope decision | backlog label and defer note | governance | later |
| RQ-010 | Exclude arbitrary code execution from MVP claims | Product + Eng | scope decision | explicit out-of-scope note | removed/high-risk | out |
