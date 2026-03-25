# Execution Gates 2026Q2

| Field | Value |
|---|---|
| Purpose | Checklist rõ ràng để biết package nào thực sự hoàn tất, tránh “xong cảm tính”. |
| Inputs | `IMPLEMENTATION_BACKLOG_2026Q2.md`, `WORK_PACKAGES_2026Q2.md` |
| Outputs | Delivery gates cho M0/M1/M2/M3 |
| Status | Active |
| Owner | Product + Engineering |
| Source refs | `ARCHITECTURE_REDLINE_2026Q2.md`, `../assistant/MVP_SMOKE_CHECKLIST.md` |
| Last updated | 2026-03-25 |

## Gate M0 — Architecture cleanup

- [ ] docs canonical nói rõ 1 public ingress + 1 private kernel
- [ ] lane legacy có deprecation map
- [ ] WorkerHost hidden auto-start hoạt động ổn
- [ ] không còn generic runtime-down UX ở first request
- [ ] AgentHost responsibilities có split plan hoặc code split rõ

## Gate M1 — Flow shell

- [ ] transcript là surface chính sau khi gửi chat
- [ ] flow states thấy được trong UI
- [ ] system/error turns luôn hiện nếu request fail
- [ ] dark/light không làm mất history
- [ ] click theme khi worker busy không crash Revit
- [ ] approval card usable cho preview-first mutations

## Gate M2 — Hub / Project Brief

- [ ] onboarding detect state đúng
- [ ] init/deep scan CTA hoạt động
- [ ] workspace readiness thấy được
- [ ] project brief render từ context bundle/deep scan
- [ ] grounded query dùng đúng lane live -> workspace -> deep scan

## Gate M3 — Audit dashboard

- [ ] web dashboard dùng shared runtime truth
- [ ] score/trend/delta usable
- [ ] suggested actions có evidence source
- [ ] manager actions không bypass approval boundary

## Regression gates

- [ ] preview -> approval -> execute -> verify không hỏng
- [ ] Revit UI không bị block nặng khi AI streaming
- [ ] WorkerHost restart/reconnect không làm mất session truth
- [ ] docs/index/read path vẫn nhất quán sau mỗi slice

