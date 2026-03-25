# AI Trust and Safety Matrix — BA Phase 4

| Field | Value |
|-------|-------|
| **Purpose** | Classify each core use case by trust level, allowed automation level, and required human control. |
| **Inputs** | Architecture, patterns, requirements backlog, critical review, technical research. |
| **Outputs** | Safety classes for MVP and pilot. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `REQUIREMENTS_BACKLOG.md`, `HUMAN_IN_THE_LOOP_POLICY.md`, `WORKFLOW_RISK_REGISTER.md`. |
| **Last updated** | 2026-03-24 |

| Use case | Trust class | Automation level | Approval need | Evidence need | MVP status |
|----------|-------------|------------------|---------------|---------------|-----------|
| Project context / deep scan | high-trust read-only | auto | none | summary + refs | in |
| Query / inspect / explain | high-trust read-only | auto | none | grounded answer | in |
| Review / QC audit | high-trust read-only | auto | none | findings + evidence | in |
| Controlled rename/fill/layout style actions | medium-trust deterministic | preview + light confirm | required | preview + verify | in |
| Destructive cleanup / purge / broad mutation | low-trust high-impact | blocked in MVP | explicit approval + stronger review | preview + verify + recovery | out of MVP |
| Arbitrary code execution in Revit | unacceptable | disallowed | n/a | n/a | out |
| Background proactive suggestions | low confidence | defer to opt-in | n/a | relevance proof required | later |
