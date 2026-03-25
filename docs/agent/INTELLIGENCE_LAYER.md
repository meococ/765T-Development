# Intelligence Layer

## Mục tiêu
- Giữ knowledge theo kiểu **hot / warm / cold** để không phình context.
- Dùng **live tool catalog + curated overlay** để AI chọn đúng tool chain.
- Ưu tiên **structured data trước visual**, **companion guidance trước autonomous behavior**.

## Memory tiers
### Hot
- `context.get_hot_state`
- `context.get_delta_summary`
- `session.get_task_context`

### Warm
- `PROJECT_MEMORY.md`
- `LESSONS_LEARNED.md`
- durable task runs / promotions

### Cold
- knowledge packs trong `docs/agent/skills/`
- rulesets trong `rulesets/`

## Orchestration principles
- Query scope nhỏ trước; tránh dump full `session.list_tools` nếu chưa cần.
- Tool mutate/file lifecycle luôn đi theo `dry_run -> approval -> execute`.
- Sau mutation nên verify bằng `context.get_hot_state`, `session.get_recent_operations`, hoặc review tool phù hợp.
- Template chỉ là guide; **live manifest** vẫn là nguồn sự thật cho capability/runtime policy.

## Runtime truth
- Tool metadata authoritative = Agent registry live.
- `TOOL_GRAPH.overlay.json` chỉ chứa curated guidance:
  - prerequisites bổ sung
  - follow-ups bổ sung
  - anti-patterns
  - recovery hints
  - template hints

## Structured intelligence surfaces
- `data.extract_schedule_structured`
  - đọc schedule bằng rows/columns JSON thay vì screenshot hoặc CSV thủ công.
- `review.smart_qc`
  - aggregate model health, standards, naming, duplicates, và sheet hygiene thành findings machine-readable.
- `family.xray`
  - đọc anatomy family: types, nested families, parameters, formulas, reference planes, connectors.
- `sheet.capture_intelligence`
  - đọc title block, viewport composition, placed schedules, sheet notes, layout map, và optional artifact paths.
- `context.get_delta_summary`
  - tóm tắt hot delta + estimated add/remove/modify + category/discipline hints + next tools.

## Token budget
- Hot: 300–800 tokens
- Warm: 1k–3k tokens
- Cold: chỉ load đúng skill/ruleset cần thiết

## Safety boundary
- Không auto mutate dựa trên learned pattern.
- Không inline ảnh/base64 lớn vào payload v1.
- Ruleset chỉ được claim những gì system đo được thật.
- `sheet.capture_intelligence` v1 ưu tiên structured output + artifact path, không làm visual blob trong response.
