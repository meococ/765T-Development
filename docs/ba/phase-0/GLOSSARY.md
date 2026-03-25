# Glossary — BA Phase 0

| Field | Value |
|-------|-------|
| **Purpose** | Normalize product vocabulary used across vision docs, architecture docs, and BA pack. |
| **Inputs** | Blueprint, current truth docs, BA pack. |
| **Outputs** | Stable terms to avoid ambiguous product conversations. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `SOURCE_OF_TRUTH_MAP.md`, `CURRENT_TRUTH_VS_VISION.md`. |
| **Last updated** | 2026-03-24 |

| Term | Definition |
|------|------------|
| 765T Worker | The primary Revit-side assistant surface used by end users. |
| 765T Hub | Machine-local runtime/project workspace state, not a team-shared cloud system in current truth. |
| 765T Scan | Read-only project understanding and compression flow. In current truth this maps to project init + deep scan orchestration. |
| 765T Flow | User-visible stream of what the assistant is doing, planning, or waiting for. |
| 765T Connect | Integration layer for external systems/connectors. Vision is broad; MVP must stay narrow. |
| 765T Audit | Quality and standards review capability. Current truth is closer to smart QC + evidence-backed inspection. |
| 765T Scripts | Script/code-oriented capability layer. In MVP this is not a core promise. |
| Persona | Tone/expertise preset. It must not override safety policy. |
| Current truth | What current repo/runtime docs explicitly support today. |
| Assumption | Claim not yet backed by evidence or implementation proof. |
| MVP slice | A bounded, pilotable package of capability with defined safety and acceptance. |
| Read path | The recommended set of docs humans/agents should read first. |
