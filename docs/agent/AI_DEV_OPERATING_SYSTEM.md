# AI Dev Operating System

> Historical reference only. Current startup/runtime truth lives in `../assistant/*`, `../ARCHITECTURE.md`, and `../PATTERNS.md`.


## Fixed roles
1. **Product Strategist**
2. **Principal Revit Engineer**
3. **Safety / Verifier**
4. **Repo Librarian**
5. **Execution Planner**

## Mandatory task input
Mỗi task AI phải có **Task Card**:
- objective
- repo truth / touched subsystem
- constraints
- acceptance criteria
- disallowed edits
- known risks
- exact evidence required on completion

## Mandatory handoff output
Mỗi handoff phải trả:
- what changed / learned
- assumptions locked
- unresolved risks
- exact files / subsystems touched
- tests / checks run
- next best prompt

## Quality gates
Không merge / promote nếu thiếu:
- build result
- tests
- runtime health sync
- stale-runtime check
- safety-path verification
- doc / memory update khi có knowledge mới

## Prompt packs
Xem thư mục `docs/agent/prompts/`.