# Pilot Instrumentation Plan ? BA Phase 5

| Field | Value |
|-------|-------|
| **Purpose** | Define what to measure during pilot so MVP decisions are evidence-based instead of anecdotal. |
| **Inputs** | `KPI_TREE.md`, `PILOT_PLAN.md`, `MVP_DEFINITION.md`, `AI_TRUST_AND_SAFETY_MATRIX.md`. |
| **Outputs** | Pilot event/metric map, baseline capture rules, and KPI ownership. |
| **Status** | Ready for engineering alignment. |
| **Owner** | Engineering + Product |
| **Source refs** | `PASS_2_DECISION_GATES.md`, `PASS_2_EXECUTION_PLAN.md`, `KPI_TREE.md`. |
| **Last updated** | 2026-03-24 |

## Metric layers

| Layer | Why it matters | Example metrics |
|------|----------------|-----------------|
| Adoption | proves recurring value | weekly active users, weekly active projects |
| Time-to-value | proves onboarding wedge | first successful context bundle, first completed review |
| Trust | proves safety/acceptance | preview-to-confirm rate, rejected preview reasons |
| Outcome | proves usefulness | findings acted on, time saved estimate, issue prevention confidence |

## Event skeleton

| Event | Trigger | Properties | KPI mapped |
|------|---------|------------|-----------|
| `project_context_requested` | user asks for project understanding | project type, user role, response time bucket | time-to-first-value |
| `deep_scan_completed` | scan finishes successfully | scope size, duration bucket, findings count | onboarding value |
| `review_qc_started` | QC flow begins | user role, project stage | adoption |
| `review_finding_opened` | user opens a finding detail | finding type, severity | usefulness |
| `preview_generated` | low-risk action preview shown | action type, scope size | trust |
| `preview_confirmed` | user confirms preview | action type, confirmation delay | trust |
| `preview_rejected` | user dismisses/rejects preview | rejection reason code | trust |
| `evidence_exported` | report/artifact is reused or exported | artifact type, consumer role | outcome |

## Baseline capture rules

- Capture a zero/blank baseline before pilot starts.
- Separate product metrics from interview sentiment.
- If a metric cannot be measured, mark it as `proxy` in `KPI_TREE.md`.
- Log rejection reasons for preview more carefully than acceptance reasons.

## Ownership

| KPI area | Owner | Update cadence |
|---------|-------|----------------|
| adoption | Product | weekly |
| time-to-value | Engineering + Product | weekly |
| trust | Product + Engineering | twice weekly during pilot |
| outcome proxies | Product | end of each pilot week |

