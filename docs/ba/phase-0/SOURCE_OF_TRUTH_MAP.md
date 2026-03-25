# Source of Truth Map — BA Phase 0

| Field | Value |
|-------|-------|
| **Purpose** | Separate vision docs, current-truth docs, critique docs, and BA outputs so the team does not mix them. |
| **Inputs** | Root product docs, assistant docs, architecture docs, BA docs. |
| **Outputs** | One map of document roles and allowed usage. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `docs/INDEX.md`, `docs/ba/README.md`. |
| **Last updated** | 2026-03-24 |

| Document / group | Role | Truth class | Use for | Do not use for |
|------------------|------|-------------|---------|----------------|
| `docs/assistant/*` | Current runtime truth | current truth | current product posture, runtime shape, config ownership | future strategy or market claims |
| `docs/ARCHITECTURE.md`, `docs/PATTERNS.md` | system truth | current truth | boundary, execution model, safety patterns | persona or pricing decisions |
| `docs/765T_BLUEPRINT.md` | product vision | source input | desired experience, ambition, wedge concepts | claiming current implementation |
| `docs/765T_CRITICAL_REVIEW.md` | critique | source input | identify illusions, risks, contradictions | final scope by itself |
| `docs/765T_TECHNICAL_RESEARCH.md` | evidence log | source input | feasibility, technical choices, external constraints | user demand proof |
| `docs/765T_TOOL_LIBRARY_BLUEPRINT.md` | tool strategy | source input | future capability expansion | MVP commitment |
| `docs/PRODUCT_REVIEW.md` | strategic synthesis | source input | market framing, framework comparison | sole basis for pricing or GTM |
| `docs/ba/*` | BA execution pack | BA final pack | scope, requirement, MVP, pilot, roadmap | low-level runtime truth unless traced back |
