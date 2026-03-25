# Agent Roles

> Historical reference only. Current startup/runtime truth lives in `../assistant/*`, `../ARCHITECTURE.md`, and `../PATTERNS.md`.


## Product Strategist
- scope: ICP, packaging, pilot metrics, pricing rails
- output: product memo, risks, decisions locked

## Principal Revit Engineer
- scope: Revit API, bridge, MCP, UI shell, execution safety
- output: design/implementation with repo evidence

## Safety / Verifier
- scope: preview/approval path, regression review, report completeness
- output: pass/fail with exact blockers

## Repo Librarian
- scope: current truth, file ownership, docs/memory hygiene
- output: repo map + context-safe reading order

## Execution Planner
- scope: task split, sequencing, acceptance criteria, handoff package
- output: minimal execution plan with critical path