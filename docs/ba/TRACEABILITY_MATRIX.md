# Traceability Matrix — 765T BA Pack

| Field | Value |
|-------|-------|
| **Purpose** | Connect source inputs to problem statements, requirements, MVP slices, and pilot decisions. |
| **Inputs** | Product source docs, current truth docs, Phase 0–5 BA files. |
| **Outputs** | Single table showing why every MVP item exists and where it came from. |
| **Status** | Pass 1 baseline complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `README.md`, phase docs. |
| **Last updated** | 2026-03-24 |

| Theme | Source input | Problem / opportunity | Phase 2 artifact | Phase 3 requirement lane | Phase 4 safety lane | Phase 5 outcome |
|------|--------------|-----------------------|------------------|--------------------------|--------------------|-----------------|
| Project understanding | `765T_BLUEPRINT` onboarding + scan; current Project Init / Deep Scan truth | BIM teams need fast grounding on a project without reading raw stats | `JTBD_MAP`, `USER_JOURNEY_MAP` | Bootstrap, context bundle, summary requirements | read-only auto path | MVP Slice 1 |
| Review / QC | `USE_CASE_MATRIX` P0; `review.smart_qc` current truth | BIM Manager needs fast pre-issue confidence and evidence | `PAIN_POINT_PRIORITIZATION` | review audit backlog | read-only auto path + evidence retention | MVP Slice 2 |
| Controlled low-risk action | Blueprint quick actions + current safe mutation pattern | Team needs time savings without losing trust | `JTBD_MAP`, `PERSONA_EVIDENCE` | preview/approve/execute requirements | deterministic low-risk policy | MVP Slice 3 |
| Dynamic code gen | Blueprint + tool library blueprint | Attractive idea but unsafe/unproven for MVP | `ASSUMPTION_REGISTER` | explicitly out of MVP backlog | high-risk / removed | Later bet |
| Multi-agent scan in Revit | Blueprint + critical review contradiction | Technical illusion if kept as-is | `CURRENT_TRUTH_VS_VISION` | removed from active requirement set | removed | Not in MVP |
| Broad connector ecosystem | Blueprint Connect vision | Too large for early slice without demand proof | `PROBLEM_OPPORTUNITY_TREE` | later only | connector-specific review later | Later bet |
| Proactive suggest | Blueprint Suggest vision | High interruption risk before precision is proven | `PAIN_POINT_PRIORITIZATION` | deferred | opt-in only | Later / pilot candidate |
| Persona system | Blueprint persona + current lightweight personas | Users may need role presets, not deep persona config | `PERSONA_EVIDENCE` | lightweight persona support only | no policy override by persona | MVP-supporting but not differentiator |
