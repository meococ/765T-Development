# NFR and Constraints — BA Phase 3

| Field | Value |
|-------|-------|
| **Purpose** | Capture the non-functional requirements and platform constraints that directly shape scope. |
| **Inputs** | Architecture, patterns, technical research, critical review. |
| **Outputs** | NFR table and explicit constraints for MVP. |
| **Status** | Pass 1 complete. |
| **Owner** | Product + Engineering |
| **Source refs** | `docs/ARCHITECTURE.md`, `docs/PATTERNS.md`, `765T_TECHNICAL_RESEARCH.md`. |
| **Last updated** | 2026-03-24 |

| Area | Requirement / constraint | BA implication |
|------|--------------------------|----------------|
| Revit API threading | single-threaded, UI-thread-bound | remove in-Revit parallel scan claims |
| Mutation safety | preview -> approval -> execute -> verify | bounded action only for MVP |
| Observability | evidence and structured outputs matter | every MVP lane needs inspectable artifacts |
| Trust | score-only UX is insufficient | narrative must be grounded in evidence |
| Performance | scans must be quota-bound and honest about limits | no oversell on 15–30 sec universal promise |
| Runtime truth | assistant docs and architecture docs override aspirational vision | BA pack must mark vision vs current truth |
| Platform reach | current shape is local Revit-first, not SaaS-first | do not design MVP like a cloud product |
