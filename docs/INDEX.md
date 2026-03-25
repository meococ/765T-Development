# docs/ — Navigation Index

> Last updated: 2026-03-25
> Restructured: old vision/review docs archived, unified into 765T_PRODUCT_VISION.md

---

## How to use this index

- **New to the project?** Read `765T_PRODUCT_VISION.md` first — it has everything: vision, research, implementation plan.
- **Need architecture details?** Then `ARCHITECTURE.md` and `PATTERNS.md`.
- **Need runtime truth?** `assistant/BASELINE.md` and `assistant/CONFIG_MATRIX.md`.
- **Historical context?** Check `archive/` folders — not startup truth.

---

## 1. Product Vision & Direction (START HERE)

| File | Description |
|------|-------------|
| **`765T_PRODUCT_VISION.md`** | **Single source of truth.** Vision, target market, technical research, implementation plan, what to build / not build. |
| `765T_TECHNICAL_RESEARCH.md` | Evidence-based decision log: Revit API constraints, .NET 8 migration, LLM pricing, storage options |

---

## 2. Architecture & System Design

| File | Description |
|------|-------------|
| `ARCHITECTURE.md` | 5-layer architecture, public/private boundary, control-plane model |
| `PATTERNS.md` | Approval, memory, pack/workspace, tool-boundary patterns |
| `architecture/ARCHITECTURE_REDLINE_2026Q2.md` | Target state for architecture cleanup |
| `architecture/IMPLEMENTATION_SLICES_2026Q2.md` | Slice-based roadmap: cleanup → Flow shell → Hub → dashboard |
| `architecture/IMPLEMENTATION_BACKLOG_2026Q2.md` | Prioritized backlog with dependencies |
| `architecture/WORK_PACKAGES_2026Q2.md` | Work packages ready for assignment |
| `architecture/EXECUTION_GATES_2026Q2.md` | Done definition per milestone M0-M3 |
| `architecture/SDD_V2_ENTERPRISE_REWRITE.md` | WorkerHost control plane spec |
| `architecture/adr/*` | Architecture Decision Records |

---

## 3. Assistant — Current Runtime Truth

> Read these for what the product IS today, not what it should be.

| File | Description |
|------|-------------|
| `assistant/BASELINE.md` | Current product shape, runtime ownership, memory stack |
| `assistant/CONFIG_MATRIX.md` | Config ownership and machine-local boundaries |
| `assistant/SPECIALISTS.md` | Internal routing/execution roles |
| `assistant/USE_CASE_MATRIX.md` | P0/P1/P2 use-case baseline |
| `assistant/MVP_SMOKE_CHECKLIST.md` | Smoke test checklist |

---

## 4. Supplemental Technical References

| File | Description |
|------|-------------|
| `BIM765T.Revit.Agent-Architecture.md` | Compact architecture quick reference |
| `BIM765T.Revit.Agent-Debug.md` | Debug/smoke checklist |
| `BIM765T.Revit.McpHost.md` | MCP facade behavior |
| `BIM765T.Revit.Snapshot-Strategy.md` | Snapshot/review workflow |
| `QUICKSTART_AI_TESTING.md` | AI testing quickstart |

---

## 5. Agent Operations & Memory

| Path | Description |
|------|-------------|
| `agent/BUILD_LOG.md` | Chronological build/change log |
| `agent/LESSONS_LEARNED.md` | Durable troubleshooting lessons |
| `agent/PROJECT_MEMORY.md` | Long-term project memory |
| `agent/IMPROVEMENT_ROADMAP_2026Q1.md` | Q1 roadmap with quality scores |
| `agent/INTELLIGENCE_LAYER.md` | Memory tiers (hot/warm/cold) |
| `agent/REVT_WORKER_V1.md` | Worker shell UX spec |
| `agent/FIX_LOOP_AND_DELIVERY_OPS.md` | Fix-loop and delivery operations |
| `agent/FAMILY_AUTHORING_BENCHMARK_V1.md` | Family authoring benchmark |
| `agent/playbooks/*` | Operational playbook definitions |
| `agent/personas/*` | Agent persona configs |
| `agent/skills/*` | Knowledge packs and tool overlays |
| `agent/prompts/*` | Prompt templates |
| `agent/templates/*` | Task card / handoff templates |

---

## 6. BA Execution Pack

| File | Description |
|------|-------------|
| `ba/BA_STATUS.md` | Completeness tracker |
| `ba/DECISION_LOG.md` | Locked decisions |
| `ba/TRACEABILITY_MATRIX.md` | Source → requirement → MVP traceability |
| `ba/phase-0/` | Source of truth map, feature inventory, glossary |
| `ba/phase-1/` | ICP, positioning, competitive matrix |
| `ba/phase-2/` | JTBD, personas, user journey, pain points |
| `ba/phase-3/` | Story map, requirements, acceptance criteria |
| `ba/phase-4/` | AI trust/safety, risk register, interview guides |
| `ba/phase-5/` | MVP definition, pilot plan, KPIs, roadmap |

---

## 7. Archive

> Superseded docs. Historical reference only — not startup truth.

| Path | Description |
|------|-------------|
| `archive/legacy-assistant/` | Old assistant-era design docs |
| `archive/legacy-vision/` | Old vision, blueprint, review docs (superseded by `765T_PRODUCT_VISION.md`) |
| `archive/legacy-agent/` | Old agent roles, memory soul, dream team docs |
