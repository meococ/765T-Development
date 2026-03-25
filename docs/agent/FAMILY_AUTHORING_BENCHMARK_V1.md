# Family Authoring Benchmark V1

## Mục tiêu

`ME_Benchmark_Parametric_ServiceBox_v1` là benchmark family authoring có chủ đích để harden backend `family.*` theo cách **deterministic, audit được, và dễ rerun**.

Nó không nhằm tạo ra một commercial family “đẹp hoàn hảo” ngay từ đầu.  
Mục tiêu của V1 là ép backend đi qua đủ các lane khó nhưng vẫn giữ xác suất pass cao:

- tạo family document mới
- add parameter / formula / type catalog
- add reference plane
- tạo solid + void + accessory forms
- bind material
- gắn visibility parameter
- save + `family.xray` + `family.list_geometry`

## Vì sao cần benchmark riêng

Nếu lao thẳng vào một family “quái vật” có connector, nested family, geometry exotic, alignment phức tạp và flex đầy đủ ngay từ đầu thì khi fail sẽ rất khó biết:

- tool nào gãy
- DTO/payload nào sai
- lane nào ổn định, lane nào mới là gap thật

Benchmark V1 cố tình đi theo thứ tự:

1. **document lifecycle**
2. **parameters + formulas**
3. **reference planes**
4. **subcategories**
5. **solids / voids / visibility / materials**
6. **type catalog**
7. **verification**

Stretch scenarios như connector/alignment sâu được tách riêng để không làm nhiễu pass/fail của core benchmark.

---

## Family benchmark được chọn

### Tên
`ME_Benchmark_Parametric_ServiceBox_v1`

### Template
`generic_model`

### Lý do chọn

- ổn định hơn template MEP cho core benchmark đầu tiên
- vẫn đủ khó để test:
  - nhiều parameters
  - nhiều formulas
  - nhiều forms
  - visibility conditions
  - type catalog
- chưa bị connector/MEP system semantics làm rối từ wave đầu

---

## Scope V1

### Core benchmark phải pass

- `family.create_document_safe`
- `family.add_parameter_safe`
- `family.set_parameter_formula_safe`
- `family.add_reference_plane_safe`
- `family.create_subcategory_safe`
- `family.create_extrusion_safe`
- `family.set_subcategory_safe`
- `family.bind_material_safe`
- `family.set_parameter_visibility_safe`
- `family.add_dimension_safe`
- `family.set_type_catalog_safe`
- `family.save_safe`
- `family.xray`
- `family.list_geometry`

### Stretch scenarios chỉ để mở sau

- `family.add_alignment_safe` cho top/bottom face lock smoke
- `family.add_connector_safe` cho MEP connector smoke
- `family.create_blend_safe` / `family.create_revolution_safe` cho geometry stress

V1 **không claim** full flex geometry production-grade.  
V1 là benchmark authoring/hardening trước.

---

## Cấu trúc benchmark family

### Forms

1. **Body**
   - solid extrusion
   - subcategory: `Benchmark_Body`
   - material param: `BodyMaterial`

2. **CavityVoid**
   - void extrusion
   - subcategory: `Benchmark_Voids`

3. **AccessPanel**
   - solid extrusion
   - subcategory: `Benchmark_Accessories`
   - material param: `AccessoryMaterial`
   - visibility param: `HasAccessPanel`

4. **InspectionWindow**
   - solid extrusion
   - subcategory: `Benchmark_Accessories`
   - material param: `AccessoryMaterial`
   - visibility param: `HasInspectionWindow`

### Reference planes

- `Left`
- `Right`
- `Front`
- `Back`
- `Ref_Level`
- `Top_Extent`
- `Mid_Height`

### Parameters

#### Geometry / constraints
- `Width`
- `Height`
- `Depth`
- `WallThickness`
- `PanelThickness`
- `Clearance`
- `InnerWidth`
- `InnerHeight`
- `InnerDepth`
- `PanelOffset`
- `WindowWidth`
- `WindowHeight`
- `VoidDepth`

#### Visibility / configuration
- `HasAccessPanel`
- `HasInspectionWindow`

#### Materials
- `BodyMaterial`
- `AccessoryMaterial`

### Formulas

- `InnerWidth = Width - (WallThickness * 2)`
- `InnerHeight = Height - (WallThickness * 2)`
- `InnerDepth = Depth - WallThickness`
- `PanelOffset = Width / 4`
- `WindowWidth = Width / 3`
- `WindowHeight = Height / 6`
- `VoidDepth = Depth + Clearance`

### Types

Benchmark V1 tạo 5 types:

- `SB36x24x18_Access`
- `SB48x30x24_Access`
- `SB60x36x24_NoWindow`
- `SB72x42x30_SolidDoor`
- `SB84x48x36_Minimal`

---

## Điều benchmark này kiểm tra thật

### 1. Backend authoring có ổn định không
- preview/approval/execute từng bước có chạy đều không
- payloads có serialize/validate ổn định không
- family document switching có an toàn không

### 2. Tool sequencing có đủ production-safe không
- create doc xong có resolve đúng family context không
- tool sau có target đúng family doc không
- save/xray/list_geometry có phản ánh state mới không

### 3. Visibility/material/type catalog lane có usable không
- visibility parameter có gắn được vào form không
- material binding có degrade an toàn không
- type catalog có tạo đủ types không

---

## Điều benchmark này chưa hứa

- full flex geometry theo type catalog
- connector semantics production-grade
- nested family mapping
- adaptive / loft / complex parametric surfaces
- MEP system classification truth

Nếu muốn test những lane đó, mở **Benchmark V1 Stretch** sau khi core benchmark đã ổn định.

---

## Runner chuẩn

Script:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_family_authoring_benchmark.ps1
```

Mặc định script **không mutate**; nó chỉ tạo plan + payload bundle.

Để thực thi thật:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_family_authoring_benchmark.ps1 -Execute
```

### Artifact đầu ra

- `plan.json`
- `run-report.json`
- `family-xray.json`
- `family-geometry.json`
- step-level preview / execute payloads

### Output family

Mặc định family và artifacts được ghi vào:

- `{repoRoot}\artifacts\family-authoring-benchmark\families\...`
- `{repoRoot}\artifacts\family-authoring-benchmark\runs\...`

### Ghi chú vận hành hiện tại

- Nếu runtime Revit chỉ expose private kernel pipe, hãy chạy benchmark với bridge hotfix exe:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_family_authoring_benchmark.ps1 `
  -BridgeExe .\artifacts\bridgehotfixexe\BIM765T.Revit.Bridge.exe -Execute
```

- Runner hiện fail-closed khi response có `Succeeded = true` nhưng diagnostics chứa lỗi severity `Error`.
- Với benchmark batched dài, runner hiện normalize `ResolvedContext.ActiveDocEpoch = 0` trước execute để tránh false `CONTEXT_MISMATCH` do `DocumentChanged` race; guard chính theo document/view/selection vẫn giữ nguyên.

---

## Current hardening findings (2026-03-21)

Những lỗi backend quan trọng benchmark này đã bóc ra và đã được patch trong lane hiện tại:

1. **Mutation methods thiếu explicit transaction**
   - Nhiều execute method trong `FamilyAuthoringService` trước đó vẫn chạm sub-transaction khi outer transaction chưa mở.
   - Đã bọc lại các lane chính như add parameter, set formula, add reference plane, create extrusion, bind material/visibility, add dimension.

2. **Template resolver chọn nhầm generic model variant**
   - `generic_model` từng resolve nhầm sang `Metric Generic Model Adaptive.rft`, làm extrusion fail với lỗi family type không hỗ trợ operation đó.
   - Đã harden resolver để ưu tiên plain `Metric Generic Model.rft` và tránh adaptive/face-based/line-based variants cho benchmark V1.

3. **Bridge path mismatch sau khi runtime chuyển sang kernel pipe**
   - Runtime hiện có thể chỉ mở `BIM765T.Revit.Agent.Kernel`.
   - Bridge hotfix đã thêm fallback `WorkerHost -> kernel pipe protobuf -> legacy JSON pipe`.

4. **Dimension placement quá brittle**
   - Lane cũ fail ở các cặp plane như `Front/Back -> Depth`.
   - Đã đổi sang chiến lược nhiều candidate views + nhiều candidate dimension lines.

### Trạng thái frontier hiện tại

- Benchmark đã đi qua được lane:
  - create family document
  - toàn bộ parameters
  - reference planes
  - subcategories
  - solid/void/accessory forms
  - subcategory assignment
  - material/visibility binding
  - dimensions
- Frontier live cuối cùng trước khi dừng task nằm ở phần **tail end của benchmark** sau batch formulas, và runner đã được patch thêm để giảm false mismatch theo `ActiveDocEpoch`.
- Vì task này đã dừng giữa chừng, tài liệu này **không claim benchmark V1 đã pass hoàn toàn end-to-end**. Truth hiện tại là: benchmark đã harden đáng kể lane family authoring và đã đẩy failure frontier ra muộn hơn, nhưng cần rerun đầy đủ nếu muốn chốt pass cuối.

---

## Done definition cho benchmark V1

Benchmark coi là pass khi:

- family tạo được từ template mới
- có ít nhất:
  - 4 forms
  - 7 reference planes
  - 5 types
  - 2 visibility-controlled forms
- `family.xray` trả:
  - family name đúng
  - `TypesCount >= 5`
  - formulas có mặt
  - reference planes có mặt
- `family.list_geometry` trả:
  - forms có subcategory đúng
  - body/void/accessory đủ
- family save thành công ra `.rfa`

---

## Stretch roadmap sau V1

### Stretch A — Alignment smoke
- thử `family.add_alignment_safe` cho top/bottom face của body
- mục tiêu: xác nhận face reference indexing thực tế ổn định tới đâu

### Stretch B — Connector smoke
- đổi template sang `mechanical_equipment`
- thêm 1–2 connector để test connector lane

### Stretch C — Blend / revolution stress
- thêm neck / nozzle / tapered adapter
- test `family.create_blend_safe` hoặc `family.create_revolution_safe`

---

## Ghi chú quan trọng cho BIM manager

Benchmark này không phải “family mẫu để đưa production ngay”.  
Đây là **bài test năng lực backend family authoring**:

- BIM manager dùng nó để biết lane nào đã tin được
- dev dùng nó để biết lane nào còn gãy
- AI worker dùng nó để có benchmark deterministic thay vì demo ngẫu hứng
