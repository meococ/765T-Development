# Competitive Matrix — BA Phase 1

| Field | Value |
|-------|-------|
| **Purpose** | Give Product + Engineering a usable competitive baseline without mixing fact and internal inference. |
| **Inputs** | `PRODUCT_REVIEW.md`, `765T_BLUEPRINT.md`, `docs/ARCHITECTURE.md`, `docs/assistant/USE_CASE_MATRIX.md`. |
| **Outputs** | Directness assessment, positioning implications, and clearly separated fact vs inference. |
| **Status** | Pass 1 complete; requires quarterly refresh. |
| **Owner** | Product Strategy |
| **Source refs** | Public marketing/docs at time of analysis + internal repo truth. |
| **Last updated** | 2026-03-24 |

## Fact-backed baseline

| Competitor | Primary category | Operates inside Revit | Production BIM mutation focus | Notes |
|------------|------------------|-----------------------|-------------------------------|-------|
| Hypar | generative design / early design | No | No | Strong at concept and configuration, not live production Revit ops |
| TestFit | feasibility planning | No | No | Strong at site/unit planning, not model operations |
| Snaptrude | browser BIM authoring | No | Limited | Strong at lightweight collaborative authoring |
| Autodesk Forma | cloud planning / analysis | No | No | Strong ecosystem threat, but not direct current production-BIM execution competitor |
| pyRevit / Dynamo | scripting / automation | Yes | Yes | Closest overlap in practical automation surface |
| Cursor / Copilot | code assistant | No | No | Helps developers create tools, not end users operate live models |

## Directness of competition

| Competitor | Directness | Why |
|------------|-----------|-----|
| pyRevit / Dynamo | High | Same runtime domain, same operational user moments, different UX and governance model |
| Autodesk Forma | Medium | Strategic threat because of distribution, but not the same production workflow today |
| Cursor / Copilot | Medium | Competes for the “AI helps with BIM work” mindshare, but not in-product execution |
| Hypar / TestFit / Snaptrude | Low | Mostly earlier-phase design/planning alternatives rather than operational BIM tools |

## Internal inferences (explicitly not market facts)

| Inference | Confidence | Why it matters |
|-----------|------------|----------------|
| 765T can position as “safer than scripting, closer to work than generic AI” | Medium | This is the most plausible near-term wedge against pyRevit + generic AI combinations |
| Safety/approval flow can differentiate in production BIM teams | Medium | Needs pilot proof, but aligns with current architecture truth |
| Broad connector breadth is not required to win initial pilots | Medium | Current truth is stronger in review/context than in ecosystem breadth |
| Generative design is not a good first battlefield | High | Too far from current product truth and outside the strongest wedge |

## BA implication

The competitive takeaway for MVP is:
1. differentiate on **project understanding + review/QC + controlled action**
2. do not over-claim platform breadth or connector coverage
3. treat pyRevit/Dynamo and generic AI as the practical comparison set for early pilots
