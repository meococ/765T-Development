# Implementation Backlog 2026Q2

| Field | Value |
|---|---|
| Purpose | Chuyển target-state pack thành backlog có thể groom, estimate và triển khai. |
| Inputs | `ARCHITECTURE_REDLINE_2026Q2.md`, `IMPLEMENTATION_SLICES_2026Q2.md`, ADR `0005-0009`, current truth docs. |
| Outputs | Backlog theo epic/workstream với dependency, acceptance gate và priority rõ ràng. |
| Status | Ready for grooming. |
| Owner | Product + Engineering |
| Source refs | `../ARCHITECTURE.md`, `../PATTERNS.md`, `adr/*`, `../../ba/phase-3/REQUIREMENTS_BACKLOG.md` |
| Last updated | 2026-03-25 |

## 1. Planning assumptions

- Phase này ưu tiên **architecture cleanup -> Flow shell -> Hub -> Audit dashboard**
- Revit 2024 tiếp tục dùng **WPF-first pane**
- React web dashboard đi riêng trước, không ép full WebView2 pane ngay
- Connect / SDK / advanced semantic memory là **deferred lanes**

## 2. Milestone map

| Milestone | Goal | Exit signal |
|---|---|---|
| M0 | Canonical runtime topology rõ ràng | team nói được 1 public ingress + 1 private kernel |
| M1 | Flow shell ổn định | transcript/streaming/theme/approval không còn gây surprise |
| M2 | Hub / Project Brief usable | init/deep scan/context readiness thành product surface |
| M3 | Audit dashboard v1 usable | manager view chạy độc lập, không phá flow shell |

## 3. Backlog

| ID | Epic | Workstream | Problem hiện tại | Deliverable chính | Dependencies | Acceptance gate | Priority | Status |
|---|---|---|---|---|---|---|---|---|
| ARC-001 | Canonical IPC topology | Platform | public ingress bị nhìn như nhiều runtime khác nhau | topology map + lane ownership + deprecation map | none | docs/runtime/health dùng cùng 1 topology language | P0 | proposed |
| ARC-002 | Legacy lane sunset | Platform | legacy lane còn gây mơ hồ | inventory lane cũ + redirect plan + removal backlog | ARC-001 | không còn lane “mồ côi” không owner | P0 | proposed |
| ARC-003 | WorkerHost sidecar lifecycle | Platform / UX | user vẫn có thể gặp trạng thái phải tự bật runtime | hidden auto-start + reconnect + recover policy + explicit status UX | ARC-001 | user không cần tự mở WorkerHost | P0 | proposed |
| ARC-004 | AgentHost split | Runtime | bootstrap/UI/runtime đang dính nhau | composition roots + coordinator split | ARC-001 | trách nhiệm startup/UI/session/theme tách rõ | P0 | proposed |
| ARC-005 | Session state truth | Runtime / UX | dashboard/onboarding/transcript có thể ghi đè nhau | session state contract + owner map + projector rules | ARC-004 | 1 source of truth cho active session/document/onboarding | P0 | proposed |
| ARC-006 | Flow transcript shell | UX | user có reply nhưng không luôn thấy rõ trong pane | transcript state machine + flow states + explicit error/system turns | ARC-005 | mọi request đều có reply/error nhìn thấy được | P0 | proposed |
| ARC-007 | Theme/render hardening | UX / Performance | dark-light + streaming từng gây reset/crash/jank | theme coordinator + background-friendly rendering + coalescing rules | ARC-006 | đổi theme không mất history, không crash khi worker busy | P0 | proposed |
| ARC-008 | Approval surface unification | UX / Safety | preview/wait/approve/reject chưa thành surface product hoàn chỉnh | approval card + token/context badges + preview-in-Revit path | ARC-006 | mutation preview luôn có card confirm rõ nghĩa | P1 | proposed |
| ARC-009 | Hub state productization | Product shell | workspace/context engine có nhưng chưa thành Hub nhìn thấy được | onboarding detect state + workspace banner + readiness CTA | ARC-005 | user thấy rõ not_initialized / scan_pending / ready | P1 | proposed |
| ARC-010 | Project Brief v1 | Product shell | project understanding chưa thành 1 brief rõ ràng | project brief card + quick actions + health shell | ARC-009 | brief usable sau init/deep scan | P1 | proposed |
| ARC-011 | Web dashboard foundation | Web | web repo có nhưng chưa rõ shared contract với runtime | shared session/report contracts + web route map + API usage rules | ARC-001, ARC-009 | web không tạo runtime truth riêng | P1 | proposed |
| ARC-012 | Audit dashboard v1 | Web / Product | manager UX mới ở mức vision | score/trend/delta/actions/dashboard skeleton | ARC-011 | dashboard chạy độc lập, không ép vào pane chat | P2 | proposed |
| ARC-013 | Manager action handoff | Web / Workflow | audit actions chưa nối thành assigned/review lanes rõ ràng | assign/review/action cards + evidence handoff shape | ARC-012, ARC-008 | hành động manager không phá approval discipline | P2 | proposed |
| ARC-014 | Connect / SDK fences | Governance | blueprint dễ bị hiểu là đã có ACC/Jira/SDK | explicit fences + defer matrix + extension criteria | ARC-001 | không còn overclaim Connect/SDK trong MVP lane | P1 | proposed |
| ARC-015 | Memory truth hardening | Memory | hash/lexical fallback dễ bị hiểu lầm là semantic memory | memory capability labels + health wording + production gap register | ARC-001 | docs/health/UI không overclaim semantic memory | P1 | proposed |

## 4. Sequencing rule

### Must finish before Flow shell expansion
- ARC-001
- ARC-003
- ARC-004
- ARC-005

### Must finish before Hub product shell
- ARC-006
- ARC-007
- ARC-009

### Must finish before Audit dashboard
- ARC-010
- ARC-011

## 5. Explicit de-prioritized items

- full WebView2 dockable pane rewrite
- ACC/Jira connector rollout
- plugin SDK publicization
- multi-agent audit orchestration productization
- production semantic memory claim

