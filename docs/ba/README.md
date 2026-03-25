# BA Execution Pack — 765T

| Field | Value |
|-------|-------|
| **Purpose** | Canonical BA execution pack for Product + Engineering. This pack turns product vision into scoped, traceable, implementation-ready decisions. |
| **Inputs** | `docs/765T_PRODUCT_VISION.md` (consolidated vision + research), `docs/assistant/*`, `docs/ARCHITECTURE.md`, `docs/PATTERNS.md`. |
| **Outputs** | Phase 0–5 BA artifacts, traceability, MVP scope, pilot plan, and Pass 2 evidence execution lane. |
| **Status** | Pass 1 baseline complete; Pass 2 execution skeleton ready. |
| **Owner** | Product + Engineering |
| **Source refs** | Product source docs remain inputs; this folder is the BA final pack. |
| **Last updated** | 2026-03-25 |

## What this folder is

`docs/ba/` is the **execution pack** for Product + Engineering.
It is the place to answer:

- user chính là ai
- problem wedge là gì
- ship gì trong MVP/pilot
- requirement nào có safety gate
- roadmap 90 ngày đi theo thứ tự nào

## What this folder is NOT

Các file sau là **source inputs**, không phải BA final pack:

- `docs/765T_PRODUCT_VISION.md` (merged from old BLUEPRINT, CRITICAL_REVIEW, TOOL_LIBRARY_BLUEPRINT, PRODUCT_REVIEW)

> **Note (2026-03-25):** Old separate files (`765T_BLUEPRINT.md`, `765T_CRITICAL_REVIEW.md`, `765T_TOOL_LIBRARY_BLUEPRINT.md`, `PRODUCT_REVIEW.md`) have been consolidated into `docs/765T_PRODUCT_VISION.md` and archived to `docs/archive/legacy-vision/`.

Current technical truth vẫn nằm ở:

- `docs/assistant/*`
- `docs/ARCHITECTURE.md`
- `docs/PATTERNS.md`

## Read order

1. `BA_STATUS.md`
2. `PASS_2_EXECUTION_PLAN.md`
3. `TRACEABILITY_MATRIX.md`
4. `phase-0/SOURCE_OF_TRUTH_MAP.md`
5. `phase-0/CURRENT_TRUTH_VS_VISION.md`
6. `phase-1/ICP_AND_POSITIONING.md`
7. `phase-2/JTBD_MAP.md`
8. `phase-2/RESEARCH_PLAN.md`
9. `phase-3/STORY_MAP.md`
10. `phase-4/AI_TRUST_AND_SAFETY_MATRIX.md`
11. `phase-4/interviews/README.md`
12. `phase-5/MVP_DEFINITION.md`
13. `phase-5/PASS_2_DECISION_GATES.md`
14. `phase-5/ROADMAP_90D.md`

## Phase map

| Phase | Goal | Main output |
|-------|------|-------------|
| Phase 0 | Baseline truth | source map, glossary, assumptions, feature inventory |
| Phase 1 | Market and positioning | ICP, problem tree, value proposition, competitive view |
| Phase 2 | User and problem evidence | JTBD, persona evidence, journeys, pain ranking, research plan, evidence log |
| Phase 3 | Requirements and scope | story map, backlog, acceptance, NFR |
| Phase 4 | Trust, safety, validation plan | safety matrix, risk register, HITL policy, interview kit, observation kit |
| Phase 5 | MVP, pilot, roadmap | MVP definition, release slices, KPI tree, pilot plan, instrumentation, decision gates |

## Claim labels used across the pack

- `current truth` = confirmed by current repo/runtime docs
- `assumption` = not yet validated
- `validated` = enough evidence to drive scope
- `deferred` = visible but not active in MVP decision-making
- `removed` = contradicted, unsafe, or intentionally out of scope

## Pass 2 execution lane

Pass 2 now has a dedicated execution skeleton:
- `PASS_2_EXECUTION_PLAN.md` = master wave/cadence/exit-gate doc
- `phase-2/RESEARCH_PLAN.md` = hypotheses, sample plan, evidence rules
- `phase-2/EVIDENCE_LOG.md` = single ledger of evidence artifacts
- `phase-4/interviews/*` = recruiting, raw-note, observation, and synthesis templates
- `phase-5/PILOT_INSTRUMENTATION_PLAN.md` = metric/event plan
- `phase-5/PASS_2_DECISION_GATES.md` = post-evidence decision checklist

## Cleanup policy

- Chỉ dẫn **docs BA cũ/trùng/superseded**
- Cleanup mode = **archive rồi xóa khỏi read-path/index**
- Không dùng `archive/` như canonical read path
- Không để BA archive xuất hiện trong `docs/INDEX.md` ngoài một dòng policy nếu thực sự cần
