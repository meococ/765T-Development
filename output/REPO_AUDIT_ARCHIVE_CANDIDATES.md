# Repo Audit Archive Candidates

> Reviewed: 2026-03-25
> Purpose: separate true archive candidates from files that should stay active but be relabeled or rewritten.

## Decision Rules

- `Archive` means the file is historical and should leave the active read path.
- `Keep and relabel` means the file should remain where it is, but with clearer scope or banner text.
- `Rewrite / dedup` means the file stays active and needs cleanup, not archival.
- `Delete` means generated or cache-only material that should not be tracked as curated docs.

## Keep In Archive, But Verify No Live Refs

| Path | Current status | Action |
| --- | --- | --- |
| `docs/archive/legacy-vision/*` | Already archived | Verify no root or BA docs still point back to these as active inputs. |
| `docs/archive/legacy-assistant/*` | Already archived | Verify no startup/read-order docs point here. |
| `docs/archive/legacy-agent/*` | Already archived | Keep as history only; verify no live runtime docs still depend on them. |

## Keep In Place, But Relabel More Clearly

| Path | Why it should stay | Action |
| --- | --- | --- |
| `docs/agent/BUILD_LOG.md` | Chronological delivery log; useful operational history | Keep, but continue treating it as chronology only, not current truth. |
| `docs/agent/LESSONS_LEARNED.md` | Durable troubleshooting memory | Keep. This is an operational memory lane, not an archive candidate. |
| `docs/agent/PROJECT_MEMORY.md` | Active operational memory used by the team | Keep, but reconcile any current-state conflicts with canonical docs. |
| `docs/agent/prompts/*` | Historical prompt pack still useful for drafting | Keep in place with the existing historical banner. Do not archive automatically. |
| `packs/*/assets/README.md` | Structural pack markers used by the pack system | Keep. These are light-weight markers, not cleanup targets unless they drift semantically. |
| `docs/765T_TECHNICAL_RESEARCH.md` | Still useful as evidence log | Keep active, but relabel/update so it cannot be mistaken for current runtime strategy. |

## Strong Archive Candidates

| Path | Why it is a candidate | Recommended action |
| --- | --- | --- |
| `docs/agent/REVT_WORKER_V1.md` | Reads like an older design slice and overlaps current shell docs | Review against `BASELINE.md`; archive if no longer needed as active reference. |
| `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md` | Time-bound roadmap note with likely superseded status | Archive or relabel as historical roadmap if no active work still points to it. |
| Time-boxed handoff / task-jobdirection docs no longer referenced | Often historical execution artifacts | Move to an archive lane once no active runbooks or prompts depend on them. |

## Rewrite / Dedup, Not Archive

| Lane A | Lane B | Overlap | Action |
| --- | --- | --- | --- |
| `README.md` | `README.en.md` | Front-door product and runtime claims | Rewrite together; treat English file as required mirror. |
| `README*` | `docs/assistant/BASELINE.md` | Current product shape and memory claims | Keep `BASELINE.md` canonical for runtime truth; rewrite `README*` to summarize only. |
| `README*` | `docs/ARCHITECTURE.md` | Architecture overview | Keep high-level diagram in README, detailed truth in `ARCHITECTURE.md`. |
| `CLAUDE.md` | `docs/ARCHITECTURE.md`, `docs/assistant/BASELINE.md` | Summary vs canonical technical truth | Keep `CLAUDE.md` as startup summary only. Remove repeated claims that drift. |
| `docs/765T_PRODUCT_VISION.md` | `docs/765T_TECHNICAL_RESEARCH.md`, `docs/ba/*` | Product thesis vs evidence vs execution | Re-scope each lane; do not archive `PRODUCT_VISION.md`. |
| `.assistant/*` | `packs/agents/external-broker/assets/*` | Onboarding and delegation guidance | Align pack assets to current root read order; do not let them diverge. |

## Delete / Ignore As Generated Or Runtime-Only

| Path | Why |
| --- | --- |
| `.assistant/runs/*` | Runtime artifacts, not curated docs |
| `.assistant/relay/*` | Runtime artifacts, not curated docs |
| `.assistant/context/*.json` | Helper cache / live context exports, not canonical docs |
| `workspaces/runtime-smoke/reports/*` | Generated runtime output |
| `workspaces/runtime-smoke/memory/*` | Generated runtime output |
| Scratch files under `tools/dev/scratch/` not intentionally curated | Investigation residue, not canonical documentation |

## Files That Look Stale, But Need Evidence Before Archiving

| Path | Risk | Next check |
| --- | --- | --- |
| `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md` | Stale references, but active BA role | Rewrite first; do not archive. |
| `packs/agents/external-broker/assets/onboard.md` | Read-order drift plus encoding damage | Repair and keep if still used by pack flow. |
| `docs/agent/FIX_LOOP_AND_DELIVERY_OPS.md` | Could be historical or still operational | Search active refs before deciding. |
| `docs/agent/INTELLIGENCE_LAYER.md` | Could be target-state history, not current truth | Search refs and compare with `BASELINE.md`. |

## Bottom Line

The previous archive list was too aggressive. The main cleanup need is not "move more files to archive". The main need is:

1. restore missing canonical root files,
2. relabel active historical lanes correctly,
3. rewrite front-door docs that overclaim current state,
4. archive only genuinely superseded design notes after reference checks.
