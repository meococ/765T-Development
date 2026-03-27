# docs/ — Navigation Index

> Last updated: 2026-03-26
> Total: 125 markdown files across 7 top-level directories

---

## Unified Read Order

For new contributors or after long gaps, read in this order:

```
CLAUDE.md (this repo's agent constitution)
  -> README.md (project overview)
  -> AGENTS.md (operating constitution)
  -> docs/INDEX.md (this file — doc navigator)
  -> docs/ARCHITECTURE.md (system boundary + control-plane model)
  -> docs/PATTERNS.md (implementation patterns)
  -> docs/assistant/BASELINE.md (current runtime truth)
  -> docs/assistant/CONFIG_MATRIX.md (canonical config ownership)
  -> docs/ba/README.md (BA pack entry — for product/engineering planning)
```

---

## Canonical Truth Map

> Each topic has ONE canonical file. No single file "has everything."

| Topic | Canonical file |
|-------|---------------|
| Architecture truth | `docs/ARCHITECTURE.md` |
| Runtime truth | `docs/assistant/BASELINE.md` |
| Config truth | `docs/assistant/CONFIG_MATRIX.md` |
| Implementation patterns | `docs/PATTERNS.md` |
| Product vision + roadmap | `docs/765T_PRODUCT_VISION.md` |
| BA source map | `docs/ba/README.md` |

---

## How to use this index

- **New to the project?** Follow the [Unified Read Order](#unified-read-order) above.
- **Need architecture or runtime details?** Use the [Canonical Truth Map](#canonical-truth-map) to find the right file directly.
- **Doing Product + Engineering planning?** Start with [BA Execution Pack](#3-ba-execution-pack) before reading broader product docs.
- **Need historical context?** Use [Supplemental Technical & Ops References](#5-supplemental-technical--ops-references), [Agent Operations & Memory](#6-agent-operations--memory), and [Archive](#7-archive--legacy-docs) intentionally, not as startup truth.

---

## 1. Architecture & System Design

**Canonical truth:** `docs/ARCHITECTURE.md`

Technical boundary, control-plane model, and capability stack.

| File | Title | Description |
|------|-------|-------------|
| `ARCHITECTURE.md` | 765T Assistant Platform — Architecture | Public/private boundary, control-plane model, capability stack |
| `PATTERNS.md` | 765T Assistant Platform — Patterns | Approval, memory, pack/workspace, and tool-boundary patterns |
| `architecture/README.md` | Architecture Target-State Pack | Entry point cho redline và ADR pack của target state |
| `architecture/ARCHITECTURE_REDLINE_2026Q2.md` | Architecture Redline 2026Q2 | Chốt target state cho cleanup trước khi modernize UI |
| `architecture/IMPLEMENTATION_SLICES_2026Q2.md` | Implementation Slices 2026Q2 | Slice-based roadmap cho architecture cleanup -> Flow shell -> Hub -> dashboard |
| `architecture/IMPLEMENTATION_BACKLOG_2026Q2.md` | Implementation Backlog 2026Q2 | Backlog có priority, dependency, acceptance gate cho target state |
| `architecture/WORK_PACKAGES_2026Q2.md` | Work Packages 2026Q2 | Gói việc chi tiết để assign cho engineering lanes |
| `architecture/EXECUTION_GATES_2026Q2.md` | Execution Gates 2026Q2 | Definition of done theo milestone M0/M1/M2/M3 |
| `architecture/SDD_V2_ENTERPRISE_REWRITE.md` | SDD v2 — Enterprise Rewrite Control Plane | WorkerHost, SQLite event store, Qdrant, named pipes |
| `architecture/adr/README.md` | ADR Index | Phân biệt ADR nền hiện tại và ADR target-state |
| `architecture/adr/*` | ADRs | Canonical architecture decisions + target-state decisions |

---

## 2. Runtime Truth

**Canonical truth:** `docs/assistant/BASELINE.md`

Current product shape and runtime ownership. **Read these first for runtime truth.** These docs supersede older operational notes.

| File | Title | Description |
|------|-------|-------------|
| `assistant/README.md` | Assistant Docs | Canonical assistant read order |
| `assistant/BASELINE.md` | Current Baseline | Current product shape and runtime ownership |
| `assistant/CONFIG_MATRIX.md` | Config Matrix | Canonical config ownership and machine-local boundaries |
| `assistant/MVP_SMOKE_CHECKLIST.md` | MVP Smoke Checklist | Manual smoke path for current stack |
| `assistant/SPECIALISTS.md` | Specialists | Internal routing/execution roles |
| `assistant/USE_CASE_MATRIX.md` | Use-Case Matrix | P0/P1/P2 use-case baseline |

---

## 3. BA Execution Pack

> **Canonical BA read path for Product + Engineering.**
> Root product docs like `765T_BLUEPRINT.md` and `765T_CRITICAL_REVIEW.md` are source inputs, not BA final pack.

### Start here

| File | Description |
|------|-------------|
| `ba/README.md` | BA pack purpose, read order, claim labels, cleanup policy |
| `ba/BA_STATUS.md` | Completeness tracker and Pass 2 blockers |
| `ba/PASS_2_EXECUTION_PLAN.md` | Pass 2 evidence cadence, sample targets, and exit gates |
| `ba/TRACEABILITY_MATRIX.md` | Source → problem → requirement → MVP traceability |
| `ba/DECISION_LOG.md` | Locked BA decisions |

### Phase artifacts

| Phase | Key files |
|------|-----------|
| `phase-0` | `SOURCE_OF_TRUTH_MAP.md`, `CURRENT_TRUTH_VS_VISION.md`, `ASSUMPTION_REGISTER.md`, `FEATURE_INVENTORY.md`, `GLOSSARY.md` |
| `phase-1` | `ICP_AND_POSITIONING.md`, `PROBLEM_OPPORTUNITY_TREE.md`, `VALUE_PROPOSITION.md`, `COMPETITIVE_MATRIX.md` |
| `phase-2` | `JTBD_MAP.md`, `PERSONA_EVIDENCE.md`, `USER_JOURNEY_MAP.md`, `PAIN_POINT_PRIORITIZATION.md`, `RESEARCH_PLAN.md`, `EVIDENCE_LOG.md` |
| `phase-3` | `STORY_MAP.md`, `REQUIREMENTS_BACKLOG.md`, `ACCEPTANCE_CRITERIA.md`, `NFR_AND_CONSTRAINTS.md` |
| `phase-4` | `AI_TRUST_AND_SAFETY_MATRIX.md`, `WORKFLOW_RISK_REGISTER.md`, `HUMAN_IN_THE_LOOP_POLICY.md`, `interviews/*` |
| `phase-5` | `MVP_DEFINITION.md`, `RELEASE_SLICES.md`, `KPI_TREE.md`, `PILOT_PLAN.md`, `PILOT_INSTRUMENTATION_PLAN.md`, `PASS_2_DECISION_GATES.md`, `ROADMAP_90D.md` |

> `ba/archive/` is reserved for superseded BA drafts only and is **not** part of the main read path.

---

## 4. Product Source Inputs

Vision, critique, and research inputs that feed the BA pack.

| File | Description |
|------|-------------|
| `765T_BLUEPRINT.md` | Product vision and experience blueprint |
| `765T_CRITICAL_REVIEW.md` | Technical/strategy critique of the blueprint |
| `765T_TECHNICAL_RESEARCH.md` | Research-backed technical decision log |
| `765T_TOOL_LIBRARY_BLUEPRINT.md` | Tool-library and capability expansion blueprint |
| `PRODUCT_REVIEW.md` | Strategic review and framework comparison |

---

## 5. Supplemental Technical & Ops References

> Useful supporting references. These are **not** the primary startup truth, but they are still active supporting docs.

| File | Description |
|------|-------------|
| `BIM765T.Revit.Agent-Architecture.md` | Compact architecture quick reference for public/private execution lanes |
| `BIM765T.Revit.Agent-Debug.md` | Revit add-in + bridge smoke/debug checklist |
| `BIM765T.Revit.McpHost.md` | Current MCP facade behavior, argument normalization, and bridge boundary |
| `BIM765T.Revit.Snapshot-Strategy.md` | Snapshot/review workflow guidance for evidence-first tasks |
| `QUICKSTART_AI_TESTING.md` | End-to-end AI testing quickstart for WorkerHost + Revit + providers |

---

## 6. Agent Operations & Memory

Historical or operational knowledge for agents and maintenance work.

| Path | Description |
|------|-------------|
| `agent/README.md` | Historical memory index; redirects startup truth back to `assistant/*` and canonical architecture docs |
| `agent/PROJECT_MEMORY.md` | Long-term project memory |
| `agent/LESSONS_LEARNED.md` | Troubleshooting and durable lessons |
| `agent/BUILD_LOG.md` | Chronology of changes |
| `agent/AGENT_ROLES.md`, `agent/AI_DEV_OPERATING_SYSTEM.md` | Historical role map and operating model for earlier agent-collaboration flows |
| `agent/DREAM_TEAM.md`, `agent/AGENT_MEMORY_SOUL.md` | Earlier multi-agent memory / identity concepts kept for reference |
| `agent/COPILOT_RUNTIME_FOUNDATION.md`, `agent/REPO_BASELINE.md` | Runtime-foundation history and migration redirect stubs |
| `agent/FAMILY_AUTHORING_BENCHMARK_V1.md` | Stable benchmark/runbook for family-authoring hardening |
| `agent/TASK_JOBDIRECTION_*.md` | Case-specific job directions and runbooks |
| `agent/playbooks/*`, `agent/presets/*`, `agent/personas/*` | Operational overlays, presets, and role artifacts |
| `agent/skills/*` | Repo-local knowledge packs and tool-intelligence overlays |
| `agent/prompts/README.md` | Entry point for historical prompt pack templates |
| `agent/templates/README.md` | Entry point for task-card / handoff templates |
| `agent/INTELLIGENCE_LAYER.md`, `agent/FIX_LOOP_AND_DELIVERY_OPS.md` | Pre-architecture design notes (superseded but still in place) |
| `archive/agent/` | Archived design notes (directory removed — files stale, superseded) |

---

## 7. Archive — Legacy Docs

> Superseded or historical material. Do not use as startup truth.

| Path | Description |
|------|-------------|
| `archive/legacy-assistant/README.md` | Entry point for archived assistant-era docs and their modern replacements |
| `archive/legacy-assistant/BIM765T.Revit.ModeC-DualAgent.md`, `archive/legacy-assistant/BIM765T.Revit.ReviewRuleEngine-v1.md`, `archive/legacy-assistant/BIM765T.RoundShadow-Workflow-v2.md`, `archive/legacy-assistant/BIM765T.TaskPause-Handoff.md` | Superseded assistant-era docs kept only for historical migration reference |
| `ba/archive/*` | Archived BA drafts when/if superseded |
