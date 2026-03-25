# Task / Job Direction - Round Penetration Review / Repair Case

## Mục tiêu
Blueprint này dùng cho case:
- workflow `report.round_penetration_cut_plan` / `batch.create_round_penetration_cut_safe` / `report.round_penetration_cut_qc`
- target family là `Mii_Pen-Round_Project`
- source là pipe-like penetrations (`PIP`, `PPF`, `PPG`)
- host là cassette classes (`GYB`, `WFR`)
- cần triage residuals, repair chọn lọc, và có review-sheet artifact rõ ràng

File này là **workflow doc vận hành** cho review / repair, không thay thế current truth của live job.
Current live truth phải đọc thêm ở `ROUND_PENETRATION_TASK_HANDOFF.md`.

---

## Khi nào dùng file này
- cần chạy plan / preview / execute / QC cho round cassette openings
- cần export một review sheet dễ giao tiếp với team model/QC
- cần sửa residuals như:
  - `MISSING_INSTANCE`
  - `CUT_MISSING`
  - `AXIS_REVIEW`
  - `RESIDUAL_PLAN`
  - `ORPHAN_INSTANCE`

Không dùng file này cho:
- nested `Round -> Round_Project` externalization cũ
- IFC / MiTek downstream export truth
- family-doc nested type sync

---

## Truth hierarchy
1. `qc.json` là machine-truth cho trạng thái hiện tại.
2. `review-sheet.csv` là human triage surface.
3. `review.round_penetration_packet_safe` / `tools/round/run_round_penetration_review_packet.ps1` là visual-review truth cho marked views + Revit sheet + PNG snapshot.
4. `ROUND_PENETRATION_TASK_HANDOFF.md` là current job truth / next-step truth.
5. `BUILD_LOG.md` và notes cũ chỉ là chronology.

---

## Tool trio cần nhớ
- `report.round_penetration_cut_plan`
- `batch.create_round_penetration_cut_safe`
- `report.round_penetration_cut_qc`

Current stable design truth:
- detect source theo effective `Mii_ElementClass in {PIP, PPF, PPG}` + fallback family-name token
- detect host theo effective `Mii_ElementClass in {GYB, WFR}`
- prefilter bằng `Mii_CassetteID`
- target family canonical là `Mii_Pen-Round_Project`
- legacy names `Mii_Pen-Round_Project__...` vẫn phải được detect để tránh duplicate
- fail cut kiểu `WFR/OSB gần stub/frame` phải giữ ở residual review; không auto-nudge

---

## Repo-local scripts nên dùng

- `tools/round/run_round_penetration_cut_workflow.ps1`
  - plan -> preview -> optional execute -> QC
  - sinh artifact bundle + `review-sheet.csv`
- `tools/round/repair_round_penetration_from_qc.ps1`
  - đọc `qc.json`, bucket lỗi, tạo payload repair chọn lọc
  - optional delete targeted openings + rerun targeted source ids
- `tools/round/export_round_penetration_review_sheet.ps1`
  - regenerate review sheet từ `qc.json` hoặc `post-repair-qc.json`
- `tools/round/run_round_penetration_review_packet.ps1`
  - create colored 3D review views, place them on a Revit sheet, export PNG snapshot

Artifact roots:
- `artifacts/round-penetration-cut/<timestamp>/`
- `artifacts/round-penetration-cut-repair/<timestamp>/`

---

## Workflow chuẩn
1. `tools/check_bridge_health.ps1`
2. chạy `tools/round/run_round_penetration_cut_workflow.ps1` không `-Execute`
3. review:
   - `plan.json`
   - `preview.json`
   - `qc.json`
   - `review-sheet.csv`
   - nếu cần visual packet: chạy `tools/round/run_round_penetration_review_packet.ps1`
4. nếu preview hợp lý mới chạy lại với `-Execute`
5. nếu còn residual:
   - chạy `tools/round/repair_round_penetration_from_qc.ps1`
   - mặc định review trước
   - chỉ `-Execute` khi đã xác nhận đúng bucket cần xử lý
6. sau mỗi wave execute / repair:
   - regenerate review sheet nếu cần
   - update `ROUND_PENETRATION_TASK_HANDOFF.md`

---

## Status -> action matrix

### `CUT_OK`
- ý nghĩa: opening tồn tại, cut tồn tại, local X đã align đủ tốt
- action: giữ nguyên

### `MISSING_INSTANCE`
- ý nghĩa: plan có pair nhưng hiện chưa có opening traced phù hợp
- action: rerun **chỉ source ids đó**

### `CUT_MISSING`
- ý nghĩa: opening traced có rồi nhưng cut relation không còn / chưa có
- action:
  1. delete opening traced đó bằng preview-safe flow
  2. rerun **chỉ source ids đó**

### `AXIS_REVIEW`
- ý nghĩa: opening traced có cut nhưng orientation chưa đạt tolerance
- action:
  1. delete opening traced lỗi
  2. rerun **chỉ source ids đó**

### `RESIDUAL_PLAN`
- ý nghĩa: planner đã xác định chưa thể place/cut an toàn
- action: manual review
- rule: không blind rerun, không auto-move

### `ORPHAN_INSTANCE`
- ý nghĩa: opening traced tồn tại nhưng không còn pair source/host hiện tại
- action: chỉ delete nếu trace comment đúng prefix và đã confirm orphan thật

---

## Review-sheet contract
`review-sheet.csv` nên luôn có tối thiểu:
- `SourceElementId`
- `HostElementId`
- `PenetrationElementId`
- `HostClass`
- `CassetteId`
- `Status`
- `AxisStatus`
- `CutStatus`
- `SuggestedAction`
- `TraceComment`
- `ResidualNote`

Rule:
- review-sheet là surface giao tiếp / triage
- quyết định mutate vẫn phải quay về `qc.json` + preview/approval flow

---

## Safety rules
- không rerun full batch mặc định
- không auto-nudge / auto-move residual `WFR/OSB` cases
- orphan delete chỉ khi trace comment match prefix `BIM765T_PEN_ROUND`
- preview/execute phải giữ `ApprovalToken + PreviewRunId + ExpectedContext`
- với live run, chỉ nên có 1 Revit process trên pipe nếu không có lý do đặc biệt

---

## Command gợi ý
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check_bridge_health.ps1

powershell -ExecutionPolicy Bypass -File .\tools\run_round_penetration_cut_workflow.ps1

powershell -ExecutionPolicy Bypass -File .\tools\run_round_penetration_cut_workflow.ps1 -Execute

powershell -ExecutionPolicy Bypass -File .\tools\repair_round_penetration_from_qc.ps1

powershell -ExecutionPolicy Bypass -File .\tools\repair_round_penetration_from_qc.ps1 -Execute

powershell -ExecutionPolicy Bypass -File .\tools\export_round_penetration_review_sheet.ps1 -PreferPostRepair
```

---

## Notes / handoff discipline
- Current job truth phải sống ở `ROUND_PENETRATION_TASK_HANDOFF.md`.
- Nếu vừa execute một wave mới, handoff phải ghi rõ:
  - artifact dir mới nhất
  - counts theo status
  - source ids / opening ids đang chờ action
  - next safe command
