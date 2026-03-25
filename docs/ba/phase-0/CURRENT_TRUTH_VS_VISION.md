# Current Truth vs Vision - BA Phase 0

| Field | Value |
|-------|-------|
| **Purpose** | Make the gap between vision and current truth explicit so scope decisions stay honest. |
| **Inputs** | `765T_BLUEPRINT.md`, `765T_CRITICAL_REVIEW.md`, `docs/assistant/*`, `docs/ARCHITECTURE.md`. |
| **Outputs** | A clean mapping of product components to `current truth`, `next`, `later`, or `removed`. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `SOURCE_OF_TRUTH_MAP.md`, `TRACEABILITY_MATRIX.md`. |
| **Last updated** | 2026-03-24 |
| Component | Vision statement | Current truth | BA label | Action |
|-----------|------------------|---------------|----------|--------|
| Worker | AI-first BIM copilot in Revit | Chat-first worker shell with a single assistant surface; Commands / Evidence / Activity are surfaced in flow cards, rail, and inspector rather than guaranteed dedicated tabs | current truth | keep in MVP |
| Hub | Full project brain | Machine-local project/runtime workspace root | current truth | keep but phrase honestly |
| Scan | 15-30 second intelligent scan | Project init + deep scan orchestration, still quota-bound and context-driven | current truth | keep with realistic constraints |
| Flow | Realtime stream | Fits current shell direction; implementation depth still partial | validated next | keep in MVP but do not oversell granularity |
| Connect | Broad MCP ecosystem | Connector ambition exists, but current scope is limited | deferred | later than MVP |
| Audit | Multi-agent quality scoring | Smart QC + inspection + evidence is current truth | current truth | frame as review/QC, not autonomous swarm |
| Scripts marketplace | Reusable scripts marketplace | No external ecosystem proof yet | deferred | later bet |
| Suggest | Proactive nudges while user works | High interruption risk, no evidence yet | deferred | pilot candidate only |
| Dynamic code execution | AI writes and runs code safely | High-risk and technically constrained | removed from MVP | keep as future research only |
| Multi-agent parallel scan in Revit | Agents scan model in parallel | Contradicted by Revit single-thread rule | removed | remove from active scope |
