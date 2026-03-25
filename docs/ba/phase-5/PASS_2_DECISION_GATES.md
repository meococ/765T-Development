# Pass 2 Decision Gates ? BA Phase 5

| Field | Value |
|-------|-------|
| **Purpose** | Define the exact decisions that must be revisited after Pass 2 evidence arrives. |
| **Inputs** | `PASS_2_EXECUTION_PLAN.md`, `ASSUMPTION_REGISTER.md`, `EVIDENCE_LOG.md`, `PILOT_INSTRUMENTATION_PLAN.md`. |
| **Outputs** | Decision checklist for re-confirming MVP, pilot, and roadmap after evidence collection. |
| **Status** | Ready for use. |
| **Owner** | Product + Engineering |
| **Source refs** | `DECISION_LOG.md`, `MVP_DEFINITION.md`, `ROADMAP_90D.md`, `KPI_TREE.md`. |
| **Last updated** | 2026-03-24 |

## Gate 1 ? Persona confirmation

Required evidence:
- at least 2 sessions for each primary persona
- at least 1 contradiction captured and resolved or left explicit

Decision options:
- confirm current ICP
- narrow ICP
- split user vs buyer story more explicitly

## Gate 2 ? Wedge problem confirmation

Required evidence:
- one pain point clearly outranks the rest in frequency x severity x willingness-to-pay impact
- current workaround is materially worse than proposed product path

Decision options:
- keep current 3-slice MVP focus
- narrow to 2 slices
- change onboarding narrative but keep same core capabilities

## Gate 3 ? Trust threshold confirmation

Required evidence:
- preview/confirm lane is used in practice
- users can name what they trust and what they block
- at least one rejection reason pattern is documented

Decision options:
- keep current approval policy
- add stronger approval for some actions
- remove a controlled action from MVP if trust stays too low

## Gate 4 ? KPI and pilot confirmation

Required evidence:
- at least one baseline for time-to-value
- at least one weekly-return signal
- outcome proxy defined for review/QC usefulness

Decision options:
- continue pilot as scoped
- adjust instrumentation but keep scope
- pause expansion until metrics are measurable

## Gate 5 ? Roadmap discipline

Required evidence:
- later bets do not outperform MVP wedge in near-term demand proof
- high-risk items remain unsupported by trust/safety evidence

Decision options:
- keep roadmap discipline
- promote one later bet to pilot candidate
- explicitly remove a bet from next 90D window

