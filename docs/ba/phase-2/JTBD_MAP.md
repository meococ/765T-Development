# JTBD Map — BA Phase 2

| Field | Value |
|-------|-------|
| **Purpose** | Translate personas into concrete jobs-to-be-done that can drive MVP and pilot scope. |
| **Inputs** | ICP, problem tree, use-case matrix, current truth docs. |
| **Outputs** | JTBD map with outcome, trigger, current workaround, and trust barrier. |
| **Status** | Pass 1 drafted; Pass 2 interview validation pending. |
| **Owner** | Product + Engineering |
| **Source refs** | `ICP_AND_POSITIONING.md`, `USER_JOURNEY_MAP.md`, `REQUIREMENTS_BACKLOG.md`. |
| **Last updated** | 2026-03-24 |

| Persona | Job to be done | Trigger | Desired outcome | Current workaround | Trust barrier | MVP relevance |
|---------|----------------|---------|-----------------|-------------------|---------------|---------------|
| BIM Manager / Coordinator | Understand current project status without opening dozens of views | start of day, before review meeting, before issue | high-confidence picture of model health and status | manual view review, schedules, team chat | AI summary may feel subjective unless backed by evidence | Slice 1 + 2 |
| BIM Manager / Coordinator | Run pre-issue QA/QC fast and consistently | before issue package or coordination meeting | find naming, standards, sheet, warning, and completeness gaps quickly | manual checklist and fragmented schedules | AI score without evidence is low-trust | Slice 2 |
| BIMer / Drafter | Find the right context or elements quickly | task starts, model handoff, inherited model | answer “what/where/why” quickly | search views manually, inspect elements one by one | trust in model context and query accuracy | Slice 1 + 3 |
| BIMer / Drafter | Execute bounded repetitive actions safely | rename/fill/update/layout tasks | save time without fear of damaging model | manual edits or brittle scripts | mutation without preview is not trusted | Slice 3 |
| Pro Developer | Build on top of a safe runtime and capability model | extension or automation request | add capability without breaking safety posture | custom scripts, ad hoc tools | unclear boundary between supported and unsupported surfaces | Pilot-supporting |
