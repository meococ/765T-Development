# Research Plan ? BA Phase 2

| Field | Value |
|-------|-------|
| **Purpose** | Operationalize Pass 2 user/problem validation with explicit hypotheses, sample plan, and evidence capture rules. |
| **Inputs** | `JTBD_MAP.md`, `PERSONA_EVIDENCE.md`, `PAIN_POINT_PRIORITIZATION.md`, `ASSUMPTION_REGISTER.md`. |
| **Outputs** | Interview sample plan, research hypotheses, and update protocol for user evidence artifacts. |
| **Status** | Ready for execution. |
| **Owner** | Product + UX Research |
| **Source refs** | `phase-4/interviews/INTERVIEW_GUIDE.md`, `phase-4/interviews/INTERVIEW_NOTES_TEMPLATE.md`, `phase-4/interviews/OBSERVATION_TEMPLATE.md`. |
| **Last updated** | 2026-03-24 |

## Research objective

Validate whether the chosen ICP and wedge problems match how Revit-heavy BIM teams actually work under production pressure.

## Priority hypotheses

| ID | Hypothesis | Why it matters | Source status |
|----|------------|----------------|---------------|
| HYP-001 | BIM Managers value evidence-backed review/QC more than generalized AI chat | determines wedge and buyer narrative | assumption |
| HYP-002 | BIMers prefer fast grounded answers and bounded actions over scripting-heavy workflows | shapes onboarding and UX | assumption |
| HYP-003 | Preview/confirm is acceptable friction for low-risk action | determines activation of Slice 3 | assumption |
| HYP-004 | Project understanding is the fastest path to time-to-first-value | informs onboarding and pilot setup | partially validated |
| HYP-005 | Buyers will frame value in delivery risk reduction and time savings, not ?AI adoption? | shapes pilot and packaging | assumption |

## Sample plan

| Segment | Target | Must-have criteria | Nice-to-have criteria |
|--------|--------|-------------------|----------------------|
| BIM Manager / Coordinator | 3?4 | active in pre-issue/QC decisions | manages standards or governance |
| BIMer / Drafter | 3?4 | daily Revit production use | has used Dynamo/pyRevit before |
| Pro Developer / Automation lead | 1?2 | owns internal scripting/tooling | bridges BIM and engineering |
| Buyer / sponsor | 2 | can speak to budget/ROI/procurement | involved in pilot approval |

## Evidence rules

- Do not mark any hypothesis as `validated` from a single conversation.
- Prefer behavior evidence over opinion alone.
- Capture direct quotes only in raw interview notes; synthesis docs should summarize.
- Contradictions must be preserved, not averaged away.
- If a segment has fewer than 2 sessions, its confidence cannot exceed `Medium`.

## Required outputs after each session

1. create a raw note using `phase-4/interviews/INTERVIEW_NOTES_TEMPLATE.md`
2. add the session to `EVIDENCE_LOG.md`
3. update persona confidence if the session changes confidence materially
4. tag impacted assumptions and pain points

## Update matrix

| If interview reveals... | Update first | Then update |
|-------------------------|--------------|-------------|
| different top pain point | `PAIN_POINT_PRIORITIZATION.md` | `JTBD_MAP.md`, `DECISION_LOG.md` |
| different buyer/user split | `PERSONA_EVIDENCE.md` | `ICP_AND_POSITIONING.md`, `ASSUMPTION_REGISTER.md` |
| trust barrier for action lane | `AI_TRUST_AND_SAFETY_MATRIX.md` | `HUMAN_IN_THE_LOOP_POLICY.md`, `PILOT_PLAN.md` |
| pilot KPI mismatch | `KPI_TREE.md` | `PILOT_INSTRUMENTATION_PLAN.md` |

