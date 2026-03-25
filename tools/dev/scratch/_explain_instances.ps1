. (Join-Path $PSScriptRoot "Assistant.Common.ps1")
$bridge = Resolve-BridgeExe
$ids = @(131530902, 128523359, 128511204)

foreach ($id in $ids) {
    Write-Host ""
    Write-Host "=== element.explain : $id ===" -ForegroundColor Cyan
    $payloadFile = Join-Path $PSScriptRoot "_explain_tmp.json"
    (@{ ElementId = $id; IncludeParameters = $true; IncludeHostRelations = $true; IncludeDependents = $false } | ConvertTo-Json -Compress) | Out-File $payloadFile -Encoding UTF8 -NoNewline
    $r = & $bridge "element.explain" --payload $payloadFile 2>$null | ConvertFrom-Json
    if ($r.Succeeded) {
        $d = $r.PayloadJson | ConvertFrom-Json
        Write-Host "  FamilyName       : $($d.FamilyName)"
        Write-Host "  TypeName         : $($d.TypeName)"
        Write-Host "  Category         : $($d.CategoryName)"
        Write-Host "  PlacementType    : $($d.PlacementType)"
        Write-Host "  HostId           : $($d.HostId)"
        Write-Host "  HostCategory     : $($d.HostCategoryName)"
        Write-Host "  HostTypeName     : $($d.HostTypeName)"
        Write-Host "  LevelName        : $($d.LevelName)"
        Write-Host "  IsMirrored       : $($d.IsMirrored)"
        Write-Host "  WorkPlaneName    : $($d.WorkPlaneName)"
        Write-Host "  FaceNormal       : $($d.FaceNormal | ConvertTo-Json -Compress)"
        Write-Host "  HandOrientation  : $($d.HandOrientation | ConvertTo-Json -Compress)"
        Write-Host "  FacingOrientation: $($d.FacingOrientation | ConvertTo-Json -Compress)"
        Write-Host "  Parameters (key) :"
        $d.Parameters | Get-Member -MemberType NoteProperty | ForEach-Object {
            $v = $d.Parameters.($_.Name)
            if ($v) { Write-Host "    $($_.Name) = $v" }
        }
    } else {
        Write-Host "  FAIL: $($r.StatusCode)"
        Write-Host "  $($r.PayloadJson)"
    }
}
