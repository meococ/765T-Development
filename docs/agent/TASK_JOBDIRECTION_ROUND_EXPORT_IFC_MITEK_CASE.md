# Task / Job Direction - Round Export IFC -> MiTek Case

## Mục tiêu
Blueprint này dành riêng cho case:
- `Round_Project` trong Revit nhìn đúng và QC project/model đã xanh
- nhưng xuất IFC sang MiTek Structure / consumer downstream vẫn đọc orientation sai
- đặc biệt các case Y/Z hoặc case đứng dễ bị hiểu sai theo red axis

File này chốt một sự thật quan trọng:

> Với Round IFC cho MiTek, `export truth > project-axis truth`.

---

## Truth đang dùng hiện tại

### 1) Model truth trong Revit
- replacement `Round -> Round_Project` trong model đã xong
- old/new mapping đúng
- vị trí / size / family-type / axis QC trong model đã xanh
- review schedule trong Revit chỉ là gate nội bộ

### 2) Downstream truth
- phải dựa trên file IFC + check thật bên MiTek
- nếu consumer còn đọc sai red axis/orientation thì task export chưa xong

---

## Bài học đã chốt

### Spike 1 - IFC evidence từ Revit
Artifact:
- `artifacts/round-ifc-mitek-spike/20260317-064539`

Spike này cho thấy Revit IFC exporter vẫn encode geometry/orientation theo type, không tự flatten hết về 1 trục.

### Spike 2 - local-X spike quan trọng
Artifact:
- `artifacts/round-ifc-localx-spike/20260317-075921`

Team structure đã confirm cả 3 case local-X spike đều đúng trong MiTek:
- `LOCALX_GLOBALX`
- `LOCALX_GLOBALY`
- `LOCALX_GLOBALZ`

### Kết luận ổn định
Rule ổn định hiện tại cho **Round only** là:
- dùng **`AXIS_X` canonical thật**
- rotate instance theo hướng thật cho X / Y / Z
- downstream truth phải dựa vào việc local X / red axis của exported object trùng hướng penetration thật

---

## Điều gì đã bị supersede
Những kết luận sau **không còn là current truth**:
- `Z blocked`
- `AXIS_Z` mặc định phải đi track riêng
- batch alias `AXIS_XY__...` / `AXIS_XZ__...` là production contract cuối

Lý do:
- user/team structure đã check downstream thật và xác nhận local-X spike đúng cho cả X/Y/Z
- sau đó cũng phát hiện batch full alias-based export + export view quá rộng vẫn gây sai downstream và kéo nhiều element rác

=> current direction phải quay về đúng contract đã validate:
- **AXIS_X canonical actual + instance rotation**
- export view sạch

---

## Contract export đang khuyến nghị

### Canonical geometry contract
- source `AXIS_X` -> `AXIS_X` as-is
- source `AXIS_Y` -> map về `AXIS_X` rồi rotate trong mặt phẳng
- source `AXIS_Z` -> map về `AXIS_X` rồi rotate 3D theo direction thật

### View/export scoping contract
Không tạo export view quá trần.

Mặc định:
- copy active 3D orientation khi có thể
- copy section box từ active 3D khi có thể
- nếu project có view template export sạch, ưu tiên dùng template đó

Nếu active 3D đang quá rộng hoặc còn nhiều element phụ, phải dừng và yêu cầu view sạch trước khi export.

---

## Working flow khuyến nghị
1. xác nhận active model đúng file
2. xác nhận chỉ có 1 `Revit.exe` dùng pipe
3. chuẩn bị active 3D sạch hoặc view template export sạch
4. build/export contract theo local-X canonical thực sự
5. tạo temporary export surrogates nếu cần
6. export IFC
7. cleanup temporary surrogates
8. verify cleanup `Count = 0`
9. giao IFC cho team structure check downstream

---

## Acceptance cho Round export
Task Round export chỉ xong khi có đủ 2 tầng:

### Tầng A - Revit/model
- old/new count đúng
- vị trí đúng
- size đúng
- mapping đúng
- schedule/comments review ổn

### Tầng B - IFC/MiTek
- sample IFC convert pass
- case X/Y/Z đều đúng orientation theo consumer truth
- file export không kéo theo quá nhiều element rác ngoài scope mong muốn

Nếu mới pass Tầng A:
- replacement done
- export workflow chưa done

---

## Checklist agent phải nhớ ở chat mới
- đừng dừng ở `ProjectAxisOkCount`
- luôn hỏi acceptance cuối là Revit QC hay MiTek convert
- nếu thấy doc cũ nói `Z blocked` hoặc `AXIS_XY__/AXIS_XZ__` là contract cuối, coi đó là historical note đã bị supersede
- current truth phải đọc ở:
  - `PROJECT_MEMORY.md`
  - `ROUND_TASK_HANDOFF.md`
  - file này
