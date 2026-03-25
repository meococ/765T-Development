# AGENTS.md

This file is the operating constitution for humans and AI agents working in `BIM765T-Revit-Agent`.

## Read Order

1. `CLAUDE.md`
2. `README.md` or `README.en.md`
3. `AGENTS.md`
4. `ASSISTANT.md`
5. `docs/765T_PRODUCT_VISION.md`
6. `docs/ARCHITECTURE.md`
7. `docs/PATTERNS.md`
8. `docs/assistant/BASELINE.md`
9. `docs/assistant/CONFIG_MATRIX.md`

## Operating Principles

- Attack problems directly. Use AI and tooling to move faster, not to add ceremony.
- Research first, then change code or docs.
- Do not overclaim. If a feature is not shipped or not verified, say so.
- Own the outcome. Finish the loop through implementation, validation, and doc updates.
- Prefer updating the current system over inventing parallel lanes.
- If docs and code conflict, verify with code and then repair the docs.

## Engineering Discipline

- `BIM765T.Revit.Agent` is the only layer allowed to touch the Revit API.
- `BIM765T.Revit.WorkerHost` is the control plane: orchestration, routing, memory projection, and external AI gateway.
- `BIM765T.Revit.Contracts*` stays append-only and backward-safe.
- Tool modules register and orchestrate. Heavy Revit logic belongs in services, not in registration modules.
- Mutation flow is tiered:
  - Read-only or harmless actions can use a quick path.
  - Deterministic mutations require preview or light confirm.
  - High-impact mutations require preview, approval, execute, and verify.
- Threading rules are not optional:
  - no Revit API work from background threads
  - no blocking `.Result` on the UI thread
  - use `ExternalEvent` or equivalent approved gateway for mutations

## Current Product Posture

- The current Revit experience is a chat-first single worker shell with one assistant surface.
- Do not describe the current product as four dedicated top-level tabs unless that runtime shape is restored and verified.
- The system is rule-first. LLMs are bounded helpers, not the source of truth.
- WorkerHost durable truth is SQLite.
- Qdrant is a vector layer backed by hash embeddings today, not true semantic memory.
- Provider selection is environment-driven. Repo examples currently favor the MiniMax lane, but the runtime is first-found-wins unless pinned.

## Documentation Contract

- `README.md` and `README.en.md` are front-door overviews.
- `ASSISTANT.md` is the repo adapter and operating baseline for assistant lanes.
- `docs/assistant/*` is the best compact statement of current runtime truth.
- `docs/ARCHITECTURE.md` and `docs/PATTERNS.md` define system boundaries and implementation rules.
- `docs/765T_PRODUCT_VISION.md` describes product direction and target state, not every detail of current runtime behavior.
- `docs/agent/*` is historical or operational memory unless a canonical doc points back to it explicitly.
- `docs/archive/*` is historical only and must not re-enter startup truth silently.

## Done Definition

- Relevant code builds.
- Relevant tests pass, or blockers are called out explicitly.
- Docs are updated in the same change when product truth changed.
- No new contradiction is introduced between `README*`, `ASSISTANT.md`, `docs/assistant/*`, `docs/ARCHITECTURE.md`, and `docs/PATTERNS.md`.
- The resulting behavior or guidance is usable by the next human or agent without archaeology.
