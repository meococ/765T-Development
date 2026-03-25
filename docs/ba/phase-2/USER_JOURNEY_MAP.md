# User Journey Map — BA Phase 2

| Field | Value |
|-------|-------|
| **Purpose** | Model the core end-to-end journeys that the MVP must support. |
| **Inputs** | JTBD map, use-case matrix, blueprint onboarding/daily loop. |
| **Outputs** | Journey steps, pain points, trust moments, and capability implications. |
| **Status** | Pass 1 drafted. |
| **Owner** | Product + Engineering |
| **Source refs** | `STORY_MAP.md`, `MVP_DEFINITION.md`. |
| **Last updated** | 2026-03-24 |

| Journey | Actor | Steps | Pain point | Trust moment | Capability implication |
|--------|-------|-------|------------|--------------|------------------------|
| Understand project | BIM Manager / BIMer | open project -> bootstrap context -> inspect findings -> choose next action | too much raw data, poor narrative context | AI summary must point back to evidence | project init, deep scan, context bundle |
| Review / QC before issue | BIM Manager | choose review lane -> run QC -> inspect findings -> assign/fix later | manual checking is slow and inconsistent | score alone is insufficient; evidence is required | smart QC, sheet/family/schedule inspection |
| Controlled action | BIMer | select target -> preview action -> confirm -> verify result | fear of accidental damage | preview + explicit confirm must be clear | query/filter/highlight + safe ops |
| Follow-up / reporting | BIM Manager | collect evidence -> summarize -> share/report | outputs are fragmented | evidence must be reusable | evidence artifact and summary pattern |
| Extension planning | Pro Developer | inspect current capability -> locate gap -> decide build/defer | unclear what is already supported | capability boundary must be explicit | feature inventory + backlog traceability |
