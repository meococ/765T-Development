# Repo Audit Cleanup Backlog

> Reviewed: 2026-03-25
> Ordering: P0 = unblock canonical truth, P1 = repair high-value drift, P2 = reduce long-tail maintenance drift.

## Already Completed Before This Backlog

| Item | Status |
| --- | --- |
| `.assistant/*` startup lane updated to read `CLAUDE.md` first | Done |
| `.assistant` commands and core agent prompts aligned to repo-local read-order rule | Done |

## P0 - Canonical Truth Blockers

| ID | Task | Why now | Main files |
| --- | --- | --- | --- |
| P0-1 | Restore `AGENTS.md` at repo root | Tests, docs, and startup guidance still require it. | `AGENTS.md`, `.claude/rules/*`, `tests/BIM765T.Revit.Architecture.Tests/ArchitectureTests.cs` |
| P0-2 | Restore `ASSISTANT.md` at repo root | Tooling and repo adapter guidance still depend on it. | `ASSISTANT.md`, `docs/assistant/CONFIG_MATRIX.md`, `tools/Assistant.Common.ps1` |
| P0-3 | Unify root read order after `AGENTS.md` and `ASSISTANT.md` exist | Root docs still drift even though `.assistant/*` is fixed. | `README.md`, `README.en.md`, `docs/INDEX.md`, `docs/765T_PRODUCT_VISION.md` |
| P0-4 | Rewrite `README.md` to match current runtime truth | Main public doc still overclaims UI shape, onboarding, memory, and provider defaults. | `README.md`, `docs/assistant/BASELINE.md`, `docs/ARCHITECTURE.md` |
| P0-5 | Mirror the same cleanup into `README.en.md` | English front door contains its own stale claims. | `README.en.md`, `README.md` |
| P0-6 | Re-scope `docs/INDEX.md` so it stops saying `765T_PRODUCT_VISION.md` "has everything" | Current index over-concentrates authority in one file. | `docs/INDEX.md`, `docs/assistant/BASELINE.md`, `docs/ba/README.md` |
| P0-7 | Re-scope `docs/765T_PRODUCT_VISION.md` as product-direction doc, not total repo truth | It currently mixes runtime truth, target state, evidence log, and commercial assumptions. | `docs/765T_PRODUCT_VISION.md`, `docs/765T_TECHNICAL_RESEARCH.md`, `docs/ba/*` |

## P1 - High-Value Drift Repairs

| ID | Task | Why | Main files |
| --- | --- | --- | --- |
| P1-1 | Fix `README*` and `CLAUDE.md` shell-shape wording | Canonical runtime says single worker shell; other docs still imply multi-tab / 5-section current UI. | `README.md`, `README.en.md`, `CLAUDE.md`, `docs/assistant/BASELINE.md` |
| P1-2 | Fix memory wording across front-door docs | Current truth is hash embeddings / non-semantic vectors, not semantic memory. | `README.md`, `README.en.md`, `docs/assistant/BASELINE.md`, `docs/ARCHITECTURE.md` |
| P1-3 | Fix provider-default wording across front-door docs | Current behavior is env-driven; current examples point to MiniMax lane, not hardcoded GPT-5 defaults. | `README.md`, `README.en.md`, `docs/assistant/BASELINE.md`, `docs/QUICKSTART_AI_TESTING.md` |
| P1-4 | Remove `ARCHITECTURE.md` runtime-truth self-claim and either define or drop "5-layer" wording | The current authority split is muddy. | `docs/ARCHITECTURE.md`, `CLAUDE.md`, `docs/INDEX.md` |
| P1-5 | Reconcile `PROJECT_MEMORY.md` shell description with `BASELINE.md` | This is a real current-state conflict, not just wording drift. | `docs/agent/PROJECT_MEMORY.md`, `docs/assistant/BASELINE.md` |
| P1-6 | Rewrite BA source map to point at current merged docs | BA lane still references archived pre-merge vision files. | `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md`, `docs/ba/README.md` |
| P1-7 | Reclassify `docs/765T_TECHNICAL_RESEARCH.md` as evidence log or update stale target assumptions | It currently reads like active strategy but conflicts with repo reality. | `docs/765T_TECHNICAL_RESEARCH.md`, `docs/INDEX.md` |
| P1-8 | Audit `packs/agents/external-broker/assets/*` for stale onboarding refs and encoding damage | Pack assets should inherit root truth, not drift behind it. | `packs/agents/external-broker/assets/onboard.md`, `delegate-external-ai.md`, `BROKER.adapter.md` |

## P2 - Maintenance And Dedup

| ID | Task | Why | Main files |
| --- | --- | --- | --- |
| P2-1 | Dedup static operational facts | Tool counts, namespace lists, and config tables are repeated in too many places. | `CLAUDE.md`, `README*`, `docs/ARCHITECTURE.md`, `docs/765T_PRODUCT_VISION.md` |
| P2-2 | Decide archive vs keep for `docs/agent/REVT_WORKER_V1.md` | Likely historical, but needs ref check first. | `docs/agent/REVT_WORKER_V1.md` |
| P2-3 | Decide archive vs keep for `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md` and similar time-boxed design notes | Reduce active-lane noise without losing history. | `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md`, related design notes |
| P2-4 | Remove remaining live references into archive lanes | Cleanup only after canonical files are fixed. | `docs/archive/*`, root docs, BA docs |
| P2-5 | Add a lightweight doc-governance check | Prevent future ghost-file and read-order drift from reappearing. | tests or lint script using `rg` / architecture tests |

## Recommended Execution Order

### Wave 1

1. P0-1 restore `AGENTS.md`
2. P0-2 restore `ASSISTANT.md`
3. P0-3 unify root read order
4. P0-4 rewrite `README.md`
5. P0-5 rewrite `README.en.md`

### Wave 2

1. P0-6 fix `docs/INDEX.md`
2. P0-7 re-scope `docs/765T_PRODUCT_VISION.md`
3. P1-4 fix `docs/ARCHITECTURE.md`
4. P1-5 reconcile `PROJECT_MEMORY.md`
5. P1-6 rewrite BA source map

### Wave 3

1. P1-7 reclassify `docs/765T_TECHNICAL_RESEARCH.md`
2. P1-8 repair pack asset onboarding docs
3. P2-1 dedup repeated facts
4. P2-2 through P2-5 archive/governance cleanup

## Validation Checklist After Cleanup

Use `rg`, not ad-hoc manual scanning:

1. `rg -n "AGENTS\\.md|ASSISTANT\\.md" .`
   - All remaining refs should resolve to real files.
2. `rg -n "Chat \\+ Activity \\+ Evidence \\+ Quick Tools|4 tabs|single worker shell" README.md README.en.md CLAUDE.md docs`
   - Only the intended canonical wording should remain.
3. `rg -n "semantic memory|Qdrant semantic|hash embeddings|non-semantic" README.md README.en.md docs`
   - Front-door docs should no longer overclaim semantic memory.
4. `rg -n "GPT-5\\.2 planner|GPT-5-mini|MiniMax-M2\\.7-highspeed|MiniMax-M2\\.7" README.md README.en.md docs`
   - Provider wording should reflect env-driven runtime truth and current examples.
5. `rg -n "single source of truth|has everything|this file wins" docs README.md README.en.md`
   - Product docs should not overclaim total authority.
6. `rg -n "765T_BLUEPRINT|765T_CRITICAL_REVIEW|765T_TOOL_LIBRARY_BLUEPRINT|PRODUCT_REVIEW" docs/ba`
   - BA source map should stop pointing to archived pre-merge files.
7. `dotnet test tests/BIM765T.Revit.Architecture.Tests -c Release`
   - Root canonical file expectations should pass once `AGENTS.md` and `ASSISTANT.md` are restored.
