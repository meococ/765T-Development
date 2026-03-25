# Work Packages 2026Q2

| Field | Value |
|---|---|
| Purpose | Biến backlog thành các gói việc có scope, dependency, acceptance và write-set rõ ràng. |
| Inputs | `IMPLEMENTATION_BACKLOG_2026Q2.md`, `ARCHITECTURE_REDLINE_2026Q2.md`, ADR `0005-0009` |
| Outputs | Work packages đủ rõ để assign cho engineering lanes. |
| Status | Draft v1 - ready for estimation. |
| Owner | Engineering |
| Source refs | `../ARCHITECTURE.md`, `../PATTERNS.md`, `IMPLEMENTATION_SLICES_2026Q2.md` |
| Last updated | 2026-03-25 |

## WP-01 — Canonical IPC and runtime topology

**Maps to:** ARC-001, ARC-002  
**Goal:** chốt 1 public ingress + 1 private kernel, liệt kê lane transitional/legacy.

### Scope
- inventory toàn bộ ingress hiện có
- đánh nhãn `canonical / adapter / transitional / legacy`
- cập nhật health/readiness wording
- tạo deprecation map cho lane legacy

### Primary write scope
- `docs/ARCHITECTURE.md`
- `docs/PATTERNS.md`
- `docs/architecture/*`
- WorkerHost health/readiness docs hoặc output labels nếu cần

### Acceptance
- không còn câu chữ mô tả nhiều runtime ngang hàng
- docs canonical + target state cùng dùng 1 topology vocabulary
- có sunset list rõ cho lane legacy

### Non-goals
- chưa bắt buộc xóa code legacy ngay trong package này

---

## WP-02 — WorkerHost hidden sidecar lifecycle

**Maps to:** ARC-003  
**Goal:** WorkerHost behave like integrated runtime.

### Scope
- hidden auto-start
- health probe strategy
- reconnect / recover policy
- UI messaging khi runtime cold-start hoặc restart

### Primary write scope
- `src/BIM765T.Revit.Agent/UI/Chat/*`
- `src/BIM765T.Revit.WorkerHost/*` nếu cần readiness/health signals
- `tools/check_ai_readiness.ps1`

### Acceptance
- user không phải tự mở WorkerHost
- request đầu tiên khi sidecar chưa lên không rơi vào generic error
- runtime-down UX có actionable message

### Risks
- timeout race
- false negative từ probe chậm

---

## WP-03 — Agent runtime/bootstrap split

**Maps to:** ARC-004  
**Goal:** giảm god-object pressure trong AgentHost.

### Scope
- tách startup/runtime/ui composition
- tách owner cho session/theme/context bootstrap
- giảm forward references

### Primary write scope
- `src/BIM765T.Revit.Agent/Infrastructure/*`
- `src/BIM765T.Revit.Agent/UI/*`

### Acceptance
- startup path đọc được theo composition roots
- không còn 1 class ôm bootstrap + UI + runtime ownership cùng lúc

### Non-goals
- không đổi boundary Revit API

---

## WP-04 — Session and ambient-context truth model

**Maps to:** ARC-005  
**Goal:** active document, active session, onboarding và transcript có owner rõ.

### Scope
- define shell state model
- projector rules cho dashboard/transcript
- resume/open latest session policy
- onboarding không ghi đè ambient context

### Primary write scope
- `src/BIM765T.Revit.Agent/UI/*`
- `src/BIM765T.Revit.Agent/Services/Platform/*`

### Acceptance
- pane luôn biết nó đang ở dashboard, transcript, waiting hay onboarding
- top bar không còn “No active model” sai khi model đang mở

---

## WP-05 — Flow transcript shell

**Maps to:** ARC-006, ARC-008  
**Goal:** transcript trở thành surface chính kiểu 765T Flow.

### Scope
- flow states: thinking / plan / scan / check / preview / wait / run / done / error
- explicit system/error turns
- approval card
- session rail behavior rõ

### Primary write scope
- `src/BIM765T.Revit.Agent/UI/Tabs/*`
- `src/BIM765T.Revit.Agent/UI/Chat/*`
- `src/BIM765T.Revit.Agent/UI/Components/*`

### Acceptance
- user luôn thấy trạng thái AI đang làm gì
- mutation preview có approval card productized
- external mission/session resume được trong pane

---

## WP-06 — Theme and render pipeline hardening

**Maps to:** ARC-007  
**Goal:** không crash, không mất state, ít jank hơn khi AI streaming.

### Scope
- theme coordinator riêng
- defer/block unsafe theme toggle khi worker busy
- preserve transcript/session rail qua toggle
- coalesced updates, background dispatcher priority

### Primary write scope
- `src/BIM765T.Revit.Agent/UI/Theme/*`
- `src/BIM765T.Revit.Agent/UI/Components/*`
- `src/BIM765T.Revit.Agent/UI/AgentPaneControl.cs`

### Acceptance
- dark/light không làm mất history
- click theme lúc streaming không crash Revit
- render updates không chặn UI thread quá mức

---

## WP-07 — Hub state shell

**Maps to:** ARC-009  
**Goal:** biến workspace/context infra thành surface trạng thái nhìn thấy được.

### Scope
- detect state: not_initialized / deep_scan_pending / ready
- CTA init/deep scan
- workspace banner + readiness hints

### Primary write scope
- `src/BIM765T.Revit.Agent/UI/*`
- WorkerHost project endpoints integration surfaces

### Acceptance
- user nhìn vào pane biết project context đang ở trạng thái nào
- init/deep scan là 1-click action rõ nghĩa

---

## WP-08 — Project Brief v1

**Maps to:** ARC-010  
**Goal:** project understanding trở thành summary shell usable.

### Scope
- brief summary card
- quick actions theo context
- health score shell
- suggested next actions

### Primary write scope
- pane dashboard shell
- project bundle/deep scan renderers
- web contract nếu dùng chung summary models

### Acceptance
- sau init/deep scan, user có 1 brief rõ ràng để bắt đầu làm việc
- quick actions dựa trên context thật, không hardcode chung chung

---

## WP-09 — Web dashboard foundation

**Maps to:** ARC-011  
**Goal:** web repo dùng cùng runtime truth, không trở thành hệ riêng.

### Scope
- map shared contracts
- route plan cho Project Brief / Audit
- API usage rules
- state ownership boundaries giữa web và WorkerHost

### Primary write scope
- `D:\\Development\\BIM765T-Revit-WebPage\\*`
- shared docs/contracts nếu cần

### Acceptance
- web không duplicate business truth
- shared contract rõ cho sessions/reports/audit summaries

---

## WP-10 — Audit dashboard v1

**Maps to:** ARC-012, ARC-013  
**Goal:** manager UX web-first.

### Scope
- score/trend/delta panels
- suggested actions
- assign/review shells
- evidence-backed issue lists

### Primary write scope
- `BIM765T-Revit-WebPage/src/*`
- WorkerHost reporting/query surfaces nếu cần dữ liệu mới

### Acceptance
- manager có view usable mà không phá transcript flow của pane
- action cards nối được sang review/approval lanes đúng boundary

---

## WP-11 — Connect / SDK / memory fences

**Maps to:** ARC-014, ARC-015  
**Goal:** dọn overclaim và đặt gate cho future lanes.

### Scope
- label Connect/SDK là deferred
- label memory capability theo reality
- define entry criteria cho lane tương lai

### Primary write scope
- docs canonical
- health/status wording
- backlog/defer docs

### Acceptance
- repo không còn mô tả Connect/SDK như shipped core
- memory wording không overclaim semantic intelligence

