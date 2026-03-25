# Implementation Slices 2026Q2

> Status: execution-oriented architecture plan
> Last updated: 2026-03-25

> Companion docs:
> - `IMPLEMENTATION_BACKLOG_2026Q2.md`
> - `WORK_PACKAGES_2026Q2.md`
> - `EXECUTION_GATES_2026Q2.md`

## Slice 0 — Guardrails

Mọi slice dưới đây phải giữ:
- `preview -> approval -> execute -> verify`
- Revit API chỉ trong Agent
- WorkerHost là sidecar, không ép merge vào Revit process

## Slice 1 — Control plane cleanup

### Deliverables
- map canonical ingress
- mark transitional lanes
- adapter boundary rõ cho CLI/MCP
- legacy lane deprecation list

### Done
- public path rõ
- private kernel path rõ
- docs/index/ADR thống nhất

## Slice 2 — Agent runtime split

### Deliverables
- tách bootstrap / UI / runtime coordinator
- giảm responsibility của `AgentHost`

### Done
- theme/session/onboarding không còn chồng chéo
- startup logic sidecar không nằm rải rác nhiều nơi

## Slice 3 — Revit Flow shell

### Deliverables
- transcript state machine
- session rail
- streaming phases
- approval card
- explicit error bubble

### Done
- user luôn thấy reply hoặc failure rõ nghĩa
- không còn “dashboard ăn mất session”

## Slice 4 — Hub / Project Brief

### Deliverables
- onboarding detect state
- init/deep scan CTA
- project brief summary
- health / quick actions shell

### Done
- workspace state thấy được trong UI
- deep scan trở thành grounded context source mặc định khi có

## Slice 5 — Audit dashboard

### Deliverables
- web-first dashboard
- score / trend / delta / suggestions
- manager lanes tách khỏi transcript chính

### Done
- không ép toàn bộ manager UX vào dockable pane chat

## Slice 6 — Connect / SDK / advanced memory

### Gate
Chỉ bắt đầu sau khi:
- Flow shell ổn
- Hub rõ
- session/runtime truth ổn
