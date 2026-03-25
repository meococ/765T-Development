# docs/agent - historical memory lane

This folder keeps **historical and operational memory** for the 765T assistant stack.

It is useful for:

- long-horizon project memory
- lessons learned and build chronology
- case runbooks and benchmark notes
- older agent-operating-model references

## Current truth moved to

Read these first when you need startup/runtime truth:

- `../assistant/BASELINE.md`
- `../assistant/CONFIG_MATRIX.md`
- `../assistant/SPECIALISTS.md`
- `../assistant/USE_CASE_MATRIX.md`
- `../ARCHITECTURE.md`
- `../PATTERNS.md`

## Historical reference map

### Project memory and chronology

- `PROJECT_MEMORY.md` - operational memory / hybrid reference; canonical docs win on conflict
- `LESSONS_LEARNED.md` - durable troubleshooting / delivery lessons
- `BUILD_LOG.md` - chronological log of changes
- `REPO_BASELINE.md` - migration redirect stub to canonical baseline docs

### Agent operating model history

- `AGENT_ROLES.md` - compact role map used in earlier coordination flows
- `AI_DEV_OPERATING_SYSTEM.md` - historical operating rules and handoff gates
- `DREAM_TEAM.md` - earlier multi-agent team design reference
- `AGENT_MEMORY_SOUL.md` - conceptual memory / identity architecture notes

### Runtime and benchmark references

- `COPILOT_RUNTIME_FOUNDATION.md` - earlier runtime bootstrap snapshot; not current truth
- `FAMILY_AUTHORING_BENCHMARK_V1.md` - stable benchmark lane for family authoring hardening
- `INTELLIGENCE_LAYER.md`, `FIX_LOOP_AND_DELIVERY_OPS.md`, `IMPROVEMENT_ROADMAP_2026Q1.md`, `REVT_WORKER_V1.md` - historical design/reference notes

### Case runbooks and operational overlays

- `TASK_JOBDIRECTION_*.md` - case-specific runbooks
- `playbooks/*`, `presets/*`, `personas/*` - operational overlays and presets
- `skills/*` - repo-local knowledge packs and tool-intelligence overlays

### Prompt and template packs

- `prompts/README.md` - entrypoint for historical prompt pack templates
- `templates/README.md` - entrypoint for task-card / handoff templates

## Usage rule

If a doc here conflicts with `docs/assistant/*`, `../ARCHITECTURE.md`, or `../PATTERNS.md`, treat this folder as **historical or operational context only** and update the canonical docs after code/runtime verification.
