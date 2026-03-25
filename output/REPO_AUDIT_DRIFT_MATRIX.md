# Repo Audit Drift Matrix

> Reviewed: 2026-03-25
> Scope: repo docs, prompt lanes, tooling assumptions, and tests.

## Drift Items

| ID | Severity | Status | Files / lane | Evidence | Drift | Recommended fix |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Critical | Open | Root docs, tools, tests | `ArchitectureTests.cs:148`, `docs/assistant/CONFIG_MATRIX.md:5`, `README.md`, `README.en.md`, `CLAUDE.md`, `.assistant/*` | `AGENTS.md` is treated as canonical constitution but does not exist. | Restore `AGENTS.md` at repo root and make other docs reference it, not duplicate it. |
| C2 | Critical | Open | Root docs, tools, tests | `ArchitectureTests.cs:149`, `tools/Assistant.Common.ps1`, `docs/assistant/CONFIG_MATRIX.md:6`, `.assistant/*` | `ASSISTANT.md` is treated as repo adapter / baseline but does not exist. | Restore `ASSISTANT.md` at repo root and use it as adapter/read-order bridge. |
| C3 | Critical | Partial | `README.md`, `README.en.md`, `docs/INDEX.md`, `docs/765T_PRODUCT_VISION.md`, `.assistant/*` | `.assistant/*` starts with `CLAUDE.md`; root docs do not | Read order is only partially repaired. Assistant lane is fixed; root/public docs are not. | Unify root read order after restoring `AGENTS.md` and `ASSISTANT.md`. |
| C4 | Critical | Open | `README.md`, `CLAUDE.md`, `docs/765T_PRODUCT_VISION.md`, `docs/assistant/BASELINE.md`, `docs/agent/PROJECT_MEMORY.md` | `CLAUDE.md:47`, `PRODUCT_VISION.md:346`, `BASELINE.md:19`, `PROJECT_MEMORY.md:274` | Current shell shape is inconsistent: multi-tab / 5-section wording vs single worker shell wording. | Decide the canonical description, then update `README*`, `CLAUDE.md`, `PRODUCT_VISION.md`, and `PROJECT_MEMORY.md`. |
| H1 | High | Open | `README.md`, `README.en.md`, `docs/assistant/BASELINE.md`, `docs/ARCHITECTURE.md` | `README.md:76`, `BASELINE.md:31`, `ARCHITECTURE.md:92` | Front-door docs still imply semantic/vector memory; runtime docs say hash embeddings and non-semantic vectors. | Rewrite both `README` files to describe current vector layer accurately. |
| H2 | High | Open | `README.md`, runtime docs, code, tests | `README.md:121`, `BASELINE.md:38`, `QUICKSTART_AI_TESTING.md:24,55-57`, `LlmBackboneServices.cs:155-157` | README hardcodes outdated provider defaults and OpenRouter-centric model claims. | Rewrite provider section as env-driven. Use MiniMax examples only as current repo default lane, not global truth. |
| H3 | High | Open | `docs/INDEX.md`, `docs/765T_PRODUCT_VISION.md`, `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md` | `INDEX.md:10`, `PRODUCT_VISION.md:501`, `SOURCE_OF_TRUTH_MAP.md` | Product vision is treated as "has everything" and "wins" over all other docs, which conflicts with runtime and BA authority split. | Re-scope `PRODUCT_VISION.md` to product thesis and update `INDEX.md` / BA source map accordingly. |
| H4 | High | Open | `docs/765T_PRODUCT_VISION.md`, `docs/ba/BA_STATUS.md` | `PRODUCT_VISION.md:41`, `PRODUCT_VISION.md:173-225`, `BA_STATUS.md` blockers | Product vision mixes current state, target state, and unvalidated commercial assumptions such as exact pricing. | Label `Current`, `Next`, and `Target state` explicitly; remove or defer exact pricing unless backed by current evidence. |
| H5 | High | Open | `docs/765T_TECHNICAL_RESEARCH.md`, `README.md`, codebase target | `TECHNICAL_RESEARCH.md` section 3 says primary target Revit 2025+/.NET 8; repo is currently net48 Revit Agent | Technical research reads like current strategy, but it now conflicts with current repo posture. | Mark it as evidence log / historical decision context, or update the conclusions to reflect current target. |
| H6 | High | Open | `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md`, `docs/ba/README.md` | `SOURCE_OF_TRUTH_MAP.md` still references `765T_BLUEPRINT.md`, `765T_CRITICAL_REVIEW.md`, `PRODUCT_REVIEW.md` | BA source map still points at pre-merge files already archived. | Rewrite the BA source map to reference current merged docs and archive policy. |
| M1 | Medium | Open | `docs/ARCHITECTURE.md`, `docs/INDEX.md`, `CLAUDE.md` | `ARCHITECTURE.md:134`, `INDEX.md`, `CLAUDE.md` | `ARCHITECTURE.md` self-claims runtime-truth ownership while other docs defer that to `docs/assistant/BASELINE.md`. | Remove the self-claim and explicitly defer compact runtime truth to `BASELINE.md`. |
| M2 | Medium | Open | `README.md`, `README.en.md`, `CLAUDE.md`, `BASELINE.md` | `README*` mutation sections vs `BASELINE.md` quality flow | Front-door docs say every mutation goes through full preview-approval-execute-verify. Runtime docs say tiered quick-path / light confirm / full flow. | Rewrite mutation description in both `README` files and `CLAUDE.md` to match the tiered flow. |
| M3 | Medium | Open | `README.en.md` vs `README.md` | `README.en.md` repeats memory, onboarding, and mutation claims independently | English front door is not just a translation mirror; it carries its own stale claims. | Treat `README.en.md` as a required mirror in the cleanup backlog. |
| M4 | Medium | Open | `packs/agents/external-broker/assets/*`, `.assistant/*` | `packs/agents/external-broker/assets/onboard.md`, `BROKER.adapter.md` | Pack asset prompts still reference old read order and appear to have encoding damage. | Audit and align pack asset onboarding with current root/.assistant startup rules. |
| M5 | Medium | Open | `docs/765T_PRODUCT_VISION.md`, `CLAUDE.md` | Both still repeat tool/module counts and UI claims | Repeated operational facts increase maintenance drift. | Keep counts and operational shape in one canonical lane and reference from others. |
| L1 | Low | Open | `README.md`, `README.en.md`, `CLAUDE.md`, `ARCHITECTURE.md` | Memory namespaces and config tables are repeated | Multiple copies of static lists create avoidable maintenance work. | Deduplicate after critical truth repair. |

## What Changed Since The Initial Audit Draft

- Read-order drift is now `partial`, not `fully broken`, because `.assistant/*` has already been repaired to `CLAUDE.md` first.
- Ghost-file drift is now more severe than the first draft suggested because tests and root-detection tooling also depend on `AGENTS.md` and `ASSISTANT.md`.
- `README.en.md` must be in scope. It is not a harmless mirror; it contains real product and memory claims.
- `docs/765T_TECHNICAL_RESEARCH.md` and `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md` are active drift sources and belong in the cleanup plan.
- `docs/agent/BUILD_LOG.md` and `docs/agent/prompts/*` should not be auto-archived. They are historical or operational lanes, but they are intentionally retained.
