# Pass 2 Execution Plan ? 765T BA Pack

| Field | Value |
|-------|-------|
| **Purpose** | Define the detailed evidence-hardening plan that turns the Pass 1 BA baseline into a pilot-ready decision pack. |
| **Inputs** | `BA_STATUS.md`, `ASSUMPTION_REGISTER.md`, `PERSONA_EVIDENCE.md`, `PILOT_PLAN.md`, `KPI_TREE.md`. |
| **Outputs** | Interview waves, evidence cadence, artifact update rules, and exit gates for Pass 2. |
| **Status** | Ready for execution. |
| **Owner** | Product + Engineering + UX Research |
| **Source refs** | `phase-2/RESEARCH_PLAN.md`, `phase-4/interviews/*`, `phase-5/PILOT_INSTRUMENTATION_PLAN.md`, `phase-5/PASS_2_DECISION_GATES.md`. |
| **Last updated** | 2026-03-24 |

## Pass 2 objective

Pass 2 exists to replace the largest remaining assumptions with direct evidence before pilot scale-up or commercial packaging decisions.

It should answer five questions:
1. Are the primary personas and buying center correct?
2. Which wedge problem creates repeat usage first?
3. Which actions are trusted enough for MVP adoption?
4. Which KPIs can actually be measured in pilot?
5. Which roadmap bets should be confirmed, deferred, or removed?

## Workstreams

| Workstream | Goal | Main owner | Output files |
|-----------|------|------------|--------------|
| User evidence | Validate personas, JTBD, pain ranking | Product + UX Research | `phase-2/PERSONA_EVIDENCE.md`, `phase-2/PAIN_POINT_PRIORITIZATION.md` |
| Workflow observation | Compare stated pain vs actual workflow | UX Research + PM | `phase-4/interviews/OBSERVATION_TEMPLATE.md`, synthesis files |
| Trust and safety validation | Confirm approval thresholds and blocked actions | Product + Engineering | `phase-4/AI_TRUST_AND_SAFETY_MATRIX.md`, `phase-4/HUMAN_IN_THE_LOOP_POLICY.md` |
| Pilot instrumentation | Measure behavior and KPI baselines | Engineering + Product Ops | `phase-5/PILOT_INSTRUMENTATION_PLAN.md`, `phase-5/KPI_TREE.md` |
| Commercial hardening | Re-score pricing and buyer evidence | Product + Sponsor | `ASSUMPTION_REGISTER.md`, `phase-5/PASS_2_DECISION_GATES.md` |

## Recommended cadence

| Week | Focus | Deliverable |
|------|-------|-------------|
| Week 1 | Recruit + schedule + baseline assumptions | evidence log seeded, interview calendar set |
| Week 2 | Run persona interviews + workflow observations | interview notes and first synthesis |
| Week 3 | Pilot instrumentation + trust validation | KPI baseline draft, updated safety matrix |
| Week 4 | Decision review | updated assumption scores, MVP confirmation, roadmap adjustment |

## Minimum sample target

| Segment | Minimum sessions | Preferred mix |
|--------|------------------|---------------|
| BIM Manager / Coordinator | 3 | different project sizes or disciplines |
| BIMer / Drafter | 3 | mix of power users and average users |
| Pro Developer / Automation lead | 1 | internal tooling owner |
| Buyer / sponsor | 2 | one delivery-side, one budget-side |
| Workflow observations | 3 | at least one pre-issue/QC session |

## Artifact update rule

After each evidence wave:
1. update raw note file first
2. update synthesis summary second
3. update the impacted BA artifact third
4. append decision change in `DECISION_LOG.md` if scope/policy changed
5. re-score impacted assumptions in `ASSUMPTION_REGISTER.md`

## Exit gates

Pass 2 is complete only if all items below are true:
- at least 8 evidence sessions completed
- top 10 assumptions re-scored with direct evidence or explicit defer reason
- primary persona confidence is at least `High` for one user lane
- one wedge problem is ranked clearly above the rest
- pilot KPI baseline exists for time-to-value, weekly return, and preview-to-confirm behavior
- any change to MVP scope is recorded in `DECISION_LOG.md`

## Failure conditions

Pause expansion if any of the following occurs:
- users prefer current manual/script workflow over 765T for the top wedge
- approval burden makes controlled action lane unused
- managers do not trust review/QC findings enough to act on them
- buyer cannot connect value to time/risk/cost outcome

