# Repo Audit Truth Map

> Reviewed: 2026-03-25
> Scope: `BIM765T-Revit-Agent`
> Purpose: establish which file wins per topic, where authority is split, and which gaps block cleanup.

## Executive Summary

- The repo already has a workable authority split for `runtime truth`, `architecture truth`, `product direction`, `BA execution`, and `historical memory`.
- The biggest blockers are not in `docs/assistant/*`. They are at the repo root: `AGENTS.md` and `ASSISTANT.md` are missing even though docs, tools, and tests still require them.
- `.assistant/*` is partially repaired already: its read order is now `CLAUDE.md` first. Root docs have not caught up.
- `docs/765T_PRODUCT_VISION.md` is currently too broad. It mixes product thesis, technical research, implementation status, target state, and commercial assumptions.
- `README.md` and `README.en.md` are the main public/front-door drift points. They overstate current memory capabilities and still describe the UI/product shape inconsistently with `docs/assistant/BASELINE.md`.

## Authority Contract

| Topic | Canonical authority | Supporting docs | Status | Notes |
| --- | --- | --- | --- | --- |
| Startup notes | `CLAUDE.md` | `.assistant/commands/onboard.md` | Active | Repo-specific critical notes and latest working guidance. |
| Repo overview / front door | `README.md`, `README.en.md` | `docs/INDEX.md` | Drift | Overview docs must summarize, not redefine runtime truth. |
| Constitution / operating boundaries | `AGENTS.md` | `.claude/rules/*`, architecture tests | Missing blocker | Referenced widely; currently absent. |
| Assistant adapter / repo operating baseline | `ASSISTANT.md` | `docs/assistant/CONFIG_MATRIX.md`, tooling | Missing blocker | Referenced by docs, tools, and tests; currently absent. |
| Current runtime truth | `docs/assistant/BASELINE.md` | `docs/assistant/CONFIG_MATRIX.md` | Active | This is the best current statement of what the product is today. |
| Architecture / boundaries / execution model | `docs/ARCHITECTURE.md`, `docs/PATTERNS.md` | `.claude/rules/project-rules.md`, `.claude/rules/safety-rules.md` | Active with one conflict | Strong current truth, but `ARCHITECTURE.md` overclaims runtime-truth ownership. |
| Product direction / target state | `docs/765T_PRODUCT_VISION.md` | `docs/765T_TECHNICAL_RESEARCH.md`, `docs/ba/*` | Active but overscoped | Should be front-door product thesis, not runtime truth and not the only doc that "wins". |
| BA execution / scope / pilot | `docs/ba/*` | `docs/ba/README.md`, `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md` | Active with stale refs | BA lane is active, but one source map still points to archived pre-merge files. |
| Historical and operational memory | `docs/agent/*` | `docs/agent/README.md` | Active historical lane | Useful, but canonical docs win on conflict. |
| Archive / superseded material | `docs/archive/*` | `docs/archive/*/README*` | Active | Historical only, not startup truth. |
| Repo-local assistant prompts | `.assistant/*` | `.assistant/README.md` | Mostly aligned | Read order already updated to start with `CLAUDE.md`. |
| Pack asset overlays | `packs/*/assets/*` | `pack.json`, `catalog/*` | Mixed | Secondary guidance only; several assets lag behind `.assistant/*` and root docs. |

## Blocking Authority Gaps

| Gap | Evidence | Why it matters | Recommended decision |
| --- | --- | --- | --- |
| `AGENTS.md` missing | `tests/BIM765T.Revit.Architecture.Tests/ArchitectureTests.cs:148`, `docs/assistant/CONFIG_MATRIX.md:5`, `README.md`, `README.en.md`, `CLAUDE.md`, `.assistant/*` | Breaks doc authority, root-marker assumptions, and architecture tests. | Restore `AGENTS.md` at repo root. Do not remove references first. |
| `ASSISTANT.md` missing | `tests/BIM765T.Revit.Architecture.Tests/ArchitectureTests.cs:149`, `tools/Assistant.Common.ps1`, `docs/assistant/CONFIG_MATRIX.md:6`, `.assistant/*` | Breaks tooling and startup/read-order assumptions. | Restore `ASSISTANT.md` at repo root. |
| Root read order not unified | `.assistant/*` now starts with `CLAUDE.md`, but `README.md`, `README.en.md`, `docs/INDEX.md`, and `docs/765T_PRODUCT_VISION.md` still drift | Causes humans and agents to enter the repo through conflicting truth layers. | Standardize one root read order after `AGENTS.md` and `ASSISTANT.md` are restored. |
| Product authority overreach | `docs/INDEX.md:10`, `docs/765T_PRODUCT_VISION.md:501` | Encourages people to treat one product doc as runtime truth, architecture truth, and BA truth at once. | Narrow `docs/765T_PRODUCT_VISION.md` to product direction and explicitly defer runtime truth to `docs/assistant/*`. |

## Drift Axes Summary

| Axis | Current status | Severity | Main evidence |
| --- | --- | --- | --- |
| Target user and wedge | Mostly concentrated in `docs/765T_PRODUCT_VISION.md` | Medium | Other front-door docs do not consistently repeat the current wedge. |
| Value proposition naming | `assistant`, `agent`, `worker shell`, `platform` all coexist | Medium | `README.md`, `README.en.md`, `BASELINE.md`, and `PRODUCT_VISION.md` use different framing. |
| Current product shape | Conflicted | Critical | `README.md`, `CLAUDE.md`, and `PRODUCT_VISION.md` still imply multi-tab shell; `BASELINE.md` says single worker shell. |
| AI / memory / privacy | Conflicted | Critical | `README.md` and `README.en.md` still say semantic/vector memory; `BASELINE.md` and `ARCHITECTURE.md` say hash embeddings, non-semantic. |
| Provider defaults / runtime config | Conflicted | High | `README.md` hardcodes model defaults; `BASELINE.md`, `QUICKSTART_AI_TESTING.md`, code, and tests show env-driven MiniMax lane. |
| Current vs target state separation | Weak | High | `PRODUCT_VISION.md` presents target-state memory, pricing, and "what already exists" in one lane. |
| Read order / startup contract | Partially fixed | Critical | `.assistant/*` repaired; root docs still inconsistent; `AGENTS.md` and `ASSISTANT.md` absent. |
| BA source map freshness | Stale | High | `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md` still references archived pre-merge vision docs. |
| Historical memory vs runtime baseline | Conflicted | High | `PROJECT_MEMORY.md` still describes a 5-section Worker v1 slice as current stable product. |

## Practical Rule Set For Cleanup

1. If a doc describes what the product is today, it must agree with `docs/assistant/BASELINE.md`.
2. If a doc describes architecture boundaries or safety patterns, it must agree with `docs/ARCHITECTURE.md` and `docs/PATTERNS.md`.
3. If a doc describes the future product, it must label `Current`, `Next`, and `Target state` separately.
4. `docs/agent/*` can preserve history, but must not silently override canonical runtime docs.
5. `packs/*/assets/*` and `.assistant/*` are secondary lanes. They should inherit root truth, not invent it.
6. Root docs must not depend on missing files. Restore `AGENTS.md` and `ASSISTANT.md` before doing broad wording cleanup.
