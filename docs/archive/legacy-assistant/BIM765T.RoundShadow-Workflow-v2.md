# 765T Round Shadow Workflow v2

## Mục tiêu
Tạo `Round shadow` để compare/export mà không phá `Penetration Alpha` cũ và không lặp lại lỗi `CopyElement` kéo theo cassette/dependency.

## Nguyên tắc
1. Không sửa family definition `Round` gốc.
2. Không dùng `CopyElement` từ instance Round reference trong project.
3. Luôn cleanup batch lỗi cũ trước khi rerun.
4. Luôn pilot 3-5 source trước khi scale full.
5. Mỗi item create phải verify:
   - đúng family `Round`
   - axis = `ALIGNED`
   - dependent graph không bất thường
6. Nếu item fail verify => rollback item đó.

## Tool sequence
1. `report.round_shadow_cleanup_plan`
2. `cleanup.round_shadow_by_run_safe` (preview -> execute)
3. `report.penetration_alpha_inventory`
4. `report.penetration_round_shadow_plan`
5. `batch.create_round_shadow_safe` với `SourceElementIds` pilot nhỏ
6. `review.family_axis_alignment` / compare review
7. Khi pilot pass mới scale full.

## Placement strategy v2
- Ưu tiên `host_face_project_aligned`
- Dùng `NewFamilyInstance(...)` theo `FamilyPlacementType`
- Với `WorkPlaneBased`:
  - resolve host face gần target anchor
  - dùng reference direction ưu tiên trục project (`Z`, rồi `X`)
- Nếu result không `ALIGNED` => rollback.

## Cleanup safety
Cleanup chỉ delete khi:
- element nằm trong journal batch đã chọn hoặc explicit ids
- và trace comment match prefix `BIM765T_SHADOW_ROUND`

## Pilot khuyến nghị
- 3 đến 5 `Penetration Alpha`
- ưu tiên các type đại diện khác nhau
- review bằng mắt + axis audit + schedule/report
