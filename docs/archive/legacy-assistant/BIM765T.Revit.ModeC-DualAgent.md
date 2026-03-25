# 765T Revit Bridge - Mode C Dual-Agent

## Mục tiêu

Mode C là mode orchestration:

- **Codex / local operator** lo tool execution, bridge, Revit context, file edits
- **Claude Code** đóng vai sub-agent để review / plan / debug / synthesize
- Cả hai dùng chung:
  - project instructions
  - latest Revit context
  - run artifacts
  - schema output

## Thành phần đã thêm

### Scripts

- `tools/Assistant.Common.ps1`
  - common helpers cho Claude + Revit Bridge
- `tools/get_revit_task_context.ps1`
  - export live Revit context vào `.assistant/context/`
- `tools/ask_claude.ps1`
  - thin wrapper gọi `claude -p`
- `tools/run_mode_c_dual_agent.ps1`
  - high-level broker cho dual-agent workflow

### Claude project assets

- `.assistant/schemas/mode-c-dual-agent-result.schema.json`
- `.assistant/commands/revit-context.md`
- `.assistant/commands/revit-review.md`
- cập nhật `ASSISTANT.md`

## Workflow chuẩn

### 1) Refresh live Revit context

```powershell
cd "C:\Users\ADMIN\Downloads\03_BIM_Dynamo\BIM765T-Revit-Agent"
powershell -ExecutionPolicy Bypass -File .\tools\get_revit_task_context.ps1
```

Output chính:

- `.assistant/context/revit-task-context.latest.json`

### 2) Chạy Claude như sub-agent

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_mode_c_dual_agent.ps1 `
  -Role reviewer `
  -Task "Review current Revit context and propose the safest next tool plan."
```

### 3) Kết quả

Mỗi run được lưu ở:

- `.assistant/runs/<timestamp>_mode-c-<role>/`

Bao gồm:

- `prompt.txt`
- `revit-task-context.json` nếu có
- `response.envelope.json`
- `response.result.txt`
- `response.parsed.json`

## Mode C.1 - Execute Claude tool plan

Sau khi Claude trả `toolPlan`, có thể cho bridge chạy tự động:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\execute_mode_c_tool_plan.ps1
```

Mặc định:

- lấy **Mode C run mới nhất có đủ artifact cần thiết**
- chỉ cho phép **read/review tools**
- chặn write tools nếu chưa bật `-AllowWrite`

Kết quả được lưu vào:

- `bridge-execution.json` trong chính run đó

## Mode C.2 - Run history, handoff, memory

### Index toàn bộ runs

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\index_mode_c_runs.ps1
```

Output:

- `.assistant/runs/index.json`
- `.assistant/runs/index.md`

### Tạo handoff từ run mới nhất

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\create_mode_c_handoff.ps1
```

Output:

- `docs/handoff/mode-c-handoff-*.md`

### Cập nhật memory tối thiểu

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\update_mode_c_memory.ps1
```

Output:

- `.assistant/memory/project-memory.md`
- `.assistant/memory/session-memory.latest.md`

## Mode C.3 - Compare Claude plan vs Bridge execution

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\compare_mode_c_execution.ps1
```

Output:

- `mode-c-compare.json`
- `mode-c-compare.md`

Mục đích:

- so sánh step Claude đề xuất với step bridge đã chạy
- giúp review chất lượng orchestration
- làm nền cho compare Codex vs Claude vs rule-engine sau này

## Vai trò hỗ trợ

### reviewer
- evidence-first review
- nêu rõ thiếu context

### planner
- chia phase
- ưu tiên execution plan an toàn

### debugger
- root cause
- instrumentation / reproducibility

### bim_coordinator
- focus vào view/sheet/workset/review

### executor
- tool plan cụ thể nhưng vẫn giữ guardrails

## Gợi ý sử dụng thực tế

### Review task trước khi sửa model

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_mode_c_dual_agent.ps1 `
  -Role reviewer `
  -Task "User wants to create a new 3D view, apply a filter, and snapshot it. Review the safest flow."
```

### Debug bridge / Revit issue

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_mode_c_dual_agent.ps1 `
  -Role debugger `
  -Task "Revit bridge returns empty active document. Propose likely causes and diagnostics."
```

### Plan execution

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_mode_c_dual_agent.ps1 `
  -Role planner `
  -Task "Design a robust workflow for sheet QC with snapshot, rule checks, and report artifacts."
```

## Chất lượng / safety

- Không cho Claude mutate model trực tiếp
- Claude chỉ plan/review/summarize qua structured JSON
- Mọi write thật vẫn phải đi qua:
  - Revit Bridge
  - approval
  - validation
  - diff/journal

## Hướng mở rộng tiếp

- run history index
- project memory / team memory
- prompt compression
- auto-handoff giữa Codex và Claude
- multi-agent compare mode (Codex vs Claude vs rule engine)
