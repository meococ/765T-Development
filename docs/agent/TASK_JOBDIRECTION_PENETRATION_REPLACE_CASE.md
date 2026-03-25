# Task / Job Direction - Penetration Replace Case

## Mục tiêu
Blueprint này dùng cho các job kiểu:
- family cũ đang nested trong family/container khác
- cần externalize ra project-level family mới
- hoặc cần đồng bộ nested child type theo parent family type trong family editor
- phải giữ mapping old -> new theo từng instance hoặc từng parent type
- phải có QC đủ cho vị trí / kích thước / trục / family-type
- khi cần, phải có review surface trong Revit bằng `Comments` + `Schedule`

Mục tiêu của file này là để agent mới vào làm nhanh đúng quy trình, không loay hoay giữa project-doc, family-doc, và downstream truth.

---

## Chia task đúng lane

### Lane 1 - project replace / externalize
Dùng khi:
- replace family cũ bằng family project mới
- pair old/new theo từng instance
- cần QC + schedule review trong project

### Lane 2 - family-doc nested type sync
Dùng khi:
- family mẹ chứa nested family con
- child family phải có đủ type theo parent family
- mỗi parent type phải gán child type cùng tên
- ví dụ: `Penetration Alpha M` chứa `Penetration Alpha`

### Lane 3 - downstream export truth
Dùng khi:
- acceptance không dừng ở Revit
- còn phải qua IFC / MiTek / consumer downstream khác
- orientation/pose contract của consumer mới là truth cuối

---

## Điều kiện để nói “xử lý tương tự được”
Chỉ nên claim “xử lý tương tự được” khi case mới còn đủ:
- source instances hoặc parent types query được
- target family / target type / target mapping rõ ràng
- old -> new mapping deterministic
- có QC surface cho family/type/position/size/axis hoặc nested type assignment
- mutation vẫn đi qua bridge-safe flow

Thiếu 1 trong các điều trên thì phải làm thêm bootstrap phase trước, không claim same workflow quá sớm.

---

## Fast-start checklist
1. `tools/check_bridge_health.ps1`
2. xác nhận đúng active document live
3. đọc `ASSISTANT.md` + `PROJECT_MEMORY.md`
4. đọc handoff hiện hành nếu có
5. chọn đúng lane ngay từ đầu

---

## Lane 1 - project replace / externalize chuẩn

### Chuỗi thực thi chuẩn
1. audit old family live
2. tạo/đọc plan artifact
3. build/load target family
4. execute externalization
5. nếu partial failure -> rerun đúng failed source ids
6. merge execute artifacts
7. QC từ merged artifact cuối
8. nếu còn residual lỗi nhỏ -> targeted repair
9. ghi comments + tạo review schedule
10. save local file

### Rule quan trọng
- không rerun full batch sau partial failure mặc định
- không kết luận “done” chỉ vì doc/handoff cũ nói vậy; phải đối chiếu live model
- QC phải đọc từ merged artifact cuối, không đọc partial artifact

### Acceptance gates cho model replacement
1. `OldCount == NewCount`
2. `FamilyName = 100% đúng`
3. `TypeName = 100% đúng`
4. `Position = 100% OK`
5. `Size = 100% OK`
6. `GeometryAxis = 100% OK`
7. `ProjectAxis = 100% OK`
8. review schedule tạo được
9. comments có trạng thái review/export-ready phù hợp
10. file local đã save

---

## Lane 2 - family-doc nested type sync chuẩn

### Setup đúng
- active document phải là **family doc**
- project doc có thể mở song song để reload family về project
- không chạy `FamilyManager` workflow khi active doc vẫn là project

### Mục tiêu chuẩn
- child family có đủ type theo parent family
- với mỗi parent type, nested child phải được set sang type cùng tên
- diameter/kích thước giữ nguyên nếu task chỉ yêu cầu sync type name + nested mapping

### Pattern ổn định
Không dùng `ChangeTypeId(...)` đơn thuần để mong nó persist theo từng parent type.

Pattern đúng:
1. inventory `FamilyManager.Types` của parent family
2. tìm nested instance đúng family con
3. create/reuse child types còn thiếu theo tên parent types
4. lấy element parameter `Type` của nested instance
5. create/reuse một family type control parameter
6. associate nested `Type` parameter vào family parameter đó
7. với mỗi `FamilyManager.CurrentType`:
   - set control parameter sang child type cùng tên
8. verify lại từng parent type
9. reload family về project nếu task yêu cầu

### Context gate cần nhớ
- family doc mở từ `Edit Family` có thể không có `OwnerFamily.Name` ổn định
- document identity phải fallback theo:
  - `OwnerFamily.Name`
  - `Path.GetFileNameWithoutExtension(doc.Title)`
  - `Path.GetFileNameWithoutExtension(doc.PathName)`

### Acceptance cho lane này
- parent type count match target expectation
- child type count đủ
- không còn missing child type
- nested assignment đúng cho từng parent type
- family reload về project thành công nếu requested

---

## Lane 3 - downstream export truth
Nếu user nói rõ task còn phải qua IFC / MiTek / converter downstream, thì đổi mental model ngay:

> `ProjectAxis = OK` trong Revit chưa đủ để claim done.

Khi đó task có 2 tầng truth:
1. `model truth` trong Revit
2. `export truth` ở downstream

### Rule hiện tại cho Round -> MiTek
- model replacement trong Revit có thể đã done
- nhưng export truth phải dựa trên sample IFC + consumer check thật
- current direction ổn định là:
  - **`AXIS_X` canonical thật**
  - rotate instance theo hướng thật cho X/Y/Z
  - export view phải dùng active 3D sạch hoặc view template export sạch
- không dùng lại conclusion cũ kiểu:
  - `Z blocked`
  - hoặc `AXIS_XY__` / `AXIS_XZ__` alias là production truth
  nếu chưa có batch rerun mới chứng minh đúng

### Acceptance bổ sung khi export là đích thật
11. sample IFC convert đúng orientation ở downstream
12. case đứng không bị convert ngang sai
13. export report/mapping chỉ rõ canonical type + pose/rotation contract

Pass 10 gate đầu = model replacement done.
Pass thêm gate downstream = export workflow done.

---

## Tool-calling rules cho nhóm case này
Ưu tiên:
- `report.*`
- `element.query`
- `type.list_element_types`
- `parameter.set_safe`
- `element.delete_safe`
- `element.place_family_instance_safe`
- `schedule.preview_create`
- `schedule.create_safe`
- `data.export_schedule`
- `file.save_document`
- tool family-doc chuyên biệt khi task đụng `FamilyManager`

Không làm:
- không gọi raw Revit API ngoài bridge flow
- không rerun full batch sau partial failure mặc định
- không coi schedule/comment xanh là downstream done nếu consumer thật còn fail

---

## Notes / handoff discipline
- Current truth của job đang chạy phải nằm ở handoff file tương ứng, không chỉ ở chat.
- `BUILD_LOG.md` là chronology; nếu một hướng cũ đã bị supersede, phải ghi rõ current truth ở `PROJECT_MEMORY.md` và handoff/task doc.

---

## Current implemented opening workflow for round cassette penetrations
- planning tool: `report.round_penetration_cut_plan`
- execute tool: `batch.create_round_penetration_cut_safe`
- QC tool: `report.round_penetration_cut_qc`
- live review/repair/review-sheet flow: `docs/agent/TASK_JOBDIRECTION_ROUND_PENETRATION_REVIEW_REPAIR_CASE.md`

Current V1 truth:
- source detect theo effective `Mii_ElementClass in {PIP, PPF, PPG}` + family-name token fallback
- host detect theo effective `Mii_ElementClass in {GYB, WFR}`
- prefilter mạnh bằng `Mii_CassetteID`
- target canonical family name là **một family duy nhất** `Mii_Pen-Round_Project`, còn variation đi bằng type theo `host class + size + length`
- family schema hiện tại:
  - `BIM765T_SchemaVersion = 3`
  - centered axial control bằng cách associate trực tiếp:
    - `EXTRUSION_START_PARAM -> BIM765T_ExtrusionStart`
    - `EXTRUSION_END_PARAM -> BIM765T_ExtrusionEnd`
  - `BIM765T_ExtrusionStart = - BIM765T_CutLength / 2`
  - `BIM765T_ExtrusionEnd = BIM765T_CutLength / 2`
  - goal là family origin = planned cut midpoint, không để geometry walk khỏi origin khi đổi type
- clearance:
  - `GYB = 1/4"` mỗi bên
  - `WFR = 1/8"` mỗi bên
- placement dùng **work-plane-based** family để xử lý được cả case ngang + đứng; mục tiêu là `local X` của opening bám `source BasisX`
- nếu placement đi qua host-face fallback, chỉ được phép correction **một lần duy nhất** từ face insertion point về planned midpoint; không được correction đại trà cho mọi work-plane placement vì sẽ double-transform instance
- instance metadata phải persist planned midpoint qua `BIM765T_PlannedPoint`; QC/review phải ưu tiên metadata này thay vì recompute midpoint từ host geometry đã bị cắt
- execute phải commit theo **từng item transaction**; không gom full batch vào một transaction lớn vì một Revit failure sẽ rollback toàn bộ wave
- fail cut kiểu WFR/OSB gần stub/frame => giữ ở residual review, không auto-nudge
- legacy opening family kiểu `Mii_Pen-Round_Project__...` vẫn phải được detect ở plan/QC để tránh duplicate trong giai đoạn chuyển tiếp
- review packet hiện phải support cả case `MISSING_INSTANCE` bằng view chỉ gồm source + host (không yêu cầu opening instance phải tồn tại)
