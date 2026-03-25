# Architecture Redline 2026Q2

> Status: target-state baseline
> Scope: architecture cleanup trước, UI modernization sau
> Last updated: 2026-03-25

## 1. Mục tiêu

Đưa 765T từ trạng thái:
- Revit pane chat-first chạy được
- WorkerHost sidecar đã có
- web repo đã tồn tại nhưng chưa integrated

...sang trạng thái:
- **1 control-plane model rõ ràng**
- **1 session/state model rõ ràng**
- **Revit pane phản hồi thật, streaming thật, không gây surprise**
- **Hub / Project Brief / Audit dashboard đi theo đúng slice**

## 2. Redline summary

| Layer | Current truth | Target state |
|---|---|---|
| Revit UI | WPF chat-first worker shell | WPF-first Flow shell cho Revit 2024; WebView2 chỉ là optional surface/popup cho lane phù hợp |
| Web UI | Có repo `BIM765T-Revit-WebPage` riêng, chưa embed vào pane | React dashboard sống độc lập trước; embed chỉ sau khi host strategy ổn |
| Streaming | Mission/chat/event streaming đã có | Chuẩn hóa thành 765T Flow states trong transcript và cards |
| Public ingress | HTTP + adapter surfaces đang cùng tồn tại | **1 canonical public control plane** ở WorkerHost; protocol khác chỉ là adapter |
| Private Revit ingress | Kernel named pipe | Giữ nguyên, continue as private kernel boundary |
| Sidecar lifecycle | Có nhưng UX chưa đủ “integrated” | hidden auto-start, reconnect, recover, health-aware |
| Hub | workspace/project init/deep scan đã có infra | productize thành Project Brief + Hub state + health shell |
| Memory | lexical fallback + hash/vector non-semantic | production memory boundary rõ: fallback usable, semantic lane không overclaim |

## 3. Những gì giữ nguyên

### 3.1 Boundary
- public caller không gọi raw Revit API
- private mutation vẫn ở `BIM765T.Revit.Agent`
- mutation vẫn theo `preview -> approval -> execute -> verify`

### 3.2 Kernel
- private kernel named pipe vẫn là ingress đúng cho Revit
- không nhét toàn bộ WorkerHost vào process Revit

### 3.3 Revit 2024 UI host
- không ép full WebView2 dockable pane trên Revit 2024
- WPF vẫn là shell host mặc định

## 4. Những gì phải thay đổi

## 4.1 IPC topology phải đơn giản lại

Target-state rule:

- **canonical public ingress**: WorkerHost runtime API
- **canonical private ingress**: kernel pipe
- **MCP / CLI / future web**: adapter gọi canonical public ingress
- **legacy lanes**: sunset

Nói ngắn:
- user/product chỉ nên hiểu có **1 AI runtime**
- engineering chỉ nên vận hành **1 public control plane + 1 private kernel**

## 4.2 AgentHost không được tiếp tục là god object

Phải tách tối thiểu thành:

- `RuntimeBootstrapper`
- `UiCompositionRoot`
- `BridgeCompositionRoot`
- `WorkerSessionCoordinator`
- `ThemeShellCoordinator`
- `AmbientContextCoordinator`

## 4.3 Session state phải có 1 nguồn truth

Target-state shell phải có model rõ cho:

- active document/view
- active session
- transcript state
- onboarding state
- approval state
- worker busy/streaming state

Không để:
- dashboard che mất transcript
- onboarding ghi đè thành “No active model”
- theme toggle làm reset session rail

## 4.4 Hub phải đi từ infra thành product surface

Hiện có:
- `project.init_*`
- `project.get_context_bundle`
- `project.deep_scan`
- workspace root và reports

Target state:
- Project Brief card
- workspace state banner
- health score / next actions
- one-click init / deep scan

## 4.5 Memory phải nói thật

Target state chốt:
- lexical fallback là baseline mandatory
- hash embedding chỉ là fallback/indexing aid
- semantic memory production-quality là **future lane**
- không overclaim “deep semantic project brain” khi chưa có embedding pipeline thật

## 5. UI strategy redline

## 5.1 Revit pane

Ưu tiên:
- WPF host
- Flow transcript
- approval cards
- session rail
- project brief banner

Không ưu tiên trước:
- full manager dashboard trong pane
- WebView2-first pane rewrite trên Revit 2024

## 5.2 Web

React dashboard nên đi trước cho:
- Project Brief view
- Audit dashboard
- trend / health / reports
- future Connect/Hub shells

## 5.3 WebView2

Chỉ nên dùng khi:
- host/version strategy an toàn
- scope rõ
- không phá pane stability

Các lane hợp lý trước:
- evidence/artifact viewer
- popup/auxiliary surfaces
- Revit 2026+ dockable strategy nếu runtime thực tế cho phép

## 6. Delivery priority

### Slice 1 — Architecture cleanup
- canonical IPC
- AgentHost split
- session/onboarding state cleanup
- sidecar lifecycle hardening

### Slice 2 — Flow shell
- transcript streaming
- Flow states
- approval card
- dark/light stability
- low-jank rendering

### Slice 3 — Project Brief / Hub
- init/deep scan product shell
- project brief
- quick actions
- health surface

### Slice 4 — Audit dashboard
- manager surfaces
- trend / delta / assignment / report surfaces

## 7. Explicit non-goals cho đợt này

- full plugin SDK
- ACC/Jira/Connect ecosystem rộng
- multi-agent audit productization
- full semantic memory claim
- full WebView2 dockable pane migration trên Revit 2024

## 8. Exit criteria cho đợt architecture cleanup

Đợt cleanup chỉ được coi là xong khi:

1. team nói được rõ canonical public/private ingress
2. pane không còn mất transcript khi đổi theme
3. WorkerHost không cần user bật tay
4. onboarding/project context không che context thật
5. Flow shell chạy ổn trước khi thêm dashboard bề mặt rộng

