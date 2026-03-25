# 765T Revit Bridge - Task Pause Handoff (2026-03-14)

## 1) Trạng thái hiện tại
- Repo đã được productize thêm các lớp:
  - payload validation
  - modular tool registry
  - named-pipe transport tốt hơn
  - cache cơ bản theo document
  - dialog/warning hardening
- Build mới nhất đã package: `D:\BIM765Tbuild_v22`
- Manifest hiện trỏ tới: `D:\BIM765Tbuild_v22\Agent\BIM765T.Revit.Agent.dll`

## 2) Bài toán đang pause
Mục tiêu nghiệp vụ:
- Từ `Penetration Alpha` đang có trong project
- tạo `Round shadow` mới để compare/export
- giữ lại `Penetration Alpha` cũ
- đảm bảo `Round shadow` đúng trục project
- sau đó review / gửi team Struc

## 3) Điều đã học được (rất quan trọng)
### 3.1 Không được dùng `CopyElement` từ instance `Round` reference
Bản v20 từng dùng `CopyElement(referenceRound)`.
Kết quả thực tế:
- không tạo single Round sạch
- kéo theo dependency graph / cassette / phần tử phụ
- event log tăng rất lớn (`Added` hàng nghìn)

Kết luận:
> Với bài toán này, primitive đúng phải là `NewFamilyInstance(...)`, không phải `CopyElement(...)`.

### 3.2 Family face-based / work-plane-based rất nhạy orientation
Quan sát thực tế:
- cùng family `Round`
- đặt trên mặt ngang thì có thể `ALIGNED`
- đặt trên mặt đứng/tường thì có thể `TILTED_OUT_OF_PROJECT_Z`

Kết luận:
> "Đúng hình" không đồng nghĩa "đúng trục".

### 3.3 Nested transform chain là vấn đề thật
Case `Penetration Alpha` cho thấy:
- direct ngoài project có thể đúng
- nested thêm tầng family khác thì có thể sai
- lỗi nằm ở transform chain, không chỉ ở geometry nhìn thấy.

### 3.4 Shared không phải lúc nào cũng là thủ phạm chính
Trong một số audit trước đó:
- nested transform risk có thật
- nhưng không phải lúc nào cũng do `non-shared`

## 4) Workflow đúng đã chốt
### Phase A - Inventory
- `report.penetration_alpha_inventory`
- `schedule.create_penetration_alpha_inventory_safe`

### Phase B - Cleanup batch lỗi cũ
- `report.round_shadow_cleanup_plan`
- `cleanup.round_shadow_by_run_safe`

### Phase C - Pilot Round shadow v2
- chỉ chạy 3-5 source trước
- dùng `batch.create_round_shadow_safe`
- strategy: `host_face_project_aligned`
- mỗi item create phải:
  - đúng family `Round`
  - axis = `ALIGNED`
  - không spawn dependent graph bất thường
- nếu fail -> rollback item đó

### Phase D - Scale full
- chỉ sau khi pilot pass bằng mắt + tool review

## 5) Những tool đáng tin ở thời điểm pause
### Read / review
- `report.penetration_alpha_inventory`
- `report.penetration_round_shadow_plan`
- `review.family_axis_alignment`
- `review.capture_snapshot`
- `session.get_recent_operations`
- `session.get_recent_events`

### Cleanup / guarded write
- `cleanup.round_shadow_by_run_safe`
- `schedule.create_penetration_alpha_inventory_safe`

### Shadow create v2
- `batch.create_round_shadow_safe`
  - đã bỏ `CopyElement`
  - đã thêm per-item rollback
  - chưa được live-verified trong session sau cùng tại thời điểm pause

## 6) Hardening đã thêm
### 6.1 Dialog / warning
- warning trong transaction sẽ auto-delete
- blocking failure sẽ rollback
- modal dialog khi agent đang chạy sẽ auto-dismiss fail-fast

### 6.2 Scripts
- `tools/check_bridge_health.ps1` tự resolve bridge exe
- `tools/test_penetration_shadow_batch.ps1` đã sửa execute branch
- `tools/test_round_shadow_cleanup.ps1` đã thêm cho cleanup batch lỗi

## 7) Lệnh gợi ý khi resume
### 7.1 Sau khi mở Revit với v22
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\check_bridge_health.ps1
```

### 7.2 Xem cleanup plan batch lỗi cũ
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\test_round_shadow_cleanup.ps1
```

### 7.3 Cleanup thật
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\test_round_shadow_cleanup.ps1 -Execute
```

### 7.4 Pilot shadow v2
Nên truyền `SourceElementIds` ít trước, không chạy full 95 ngay.

## 8) Quy tắc làm tiếp
1. Không rerun logic v20 cũ.
2. Không sửa family definition `Round` gốc bừa.
3. Pilot trước, scale sau.
4. Mọi create phải verify axis ngay sau create.
5. Nếu còn modal/warning lạ -> kiểm tra log trong `%APPDATA%\BIM765T.Revit.Agent\logs`.
