param()
. (Join-Path $PSScriptRoot "Assistant.Common.ps1")
$bridge      = Resolve-BridgeExe
$axisPayload = Join-Path $PSScriptRoot "_axis_payload.json"
$csvOut      = Join-Path $PSScriptRoot "axis_audit_report.csv"
$tmpPayload  = Join-Path $PSScriptRoot "_comments_payload.json"
$tmpDryRun   = Join-Path $PSScriptRoot "_comments_dryrun.json"
$ctxFile     = Join-Path $PSScriptRoot "_comments_context.json"
$family      = "Mii_Pen-Rec"
$date        = (Get-Date).ToString("yyyy-MM-dd")

Write-Host "--- STEP 1: Scan axis alignment ---" -ForegroundColor Cyan
$raw    = & $bridge "review.family_axis_alignment" --payload $axisPayload 2>$null
$result = $raw | ConvertFrom-Json
if (-not $result.Succeeded) { Write-Host "FAIL: $($result.StatusCode)" -ForegroundColor Red; exit 1 }
$data = $result.PayloadJson | ConvertFrom-Json
$all  = @($data.Items | Where-Object { $_.FamilyName -like "*$family*" })
Write-Host "  Doc total=$($data.TotalFamilyInstances) scanned=$($data.CheckedCount) truncated=$($data.Truncated)"
Write-Host "  $family* : $($all.Count) instances" -ForegroundColor Green
if ($all.Count -eq 0) { Write-Host "No instances found." -ForegroundColor Yellow; exit 0 }

Write-Host "--- STEP 2: Write Comments ---" -ForegroundColor Cyan
$changes    = @()
$commentMap = @{}
foreach ($item in $all) {
    $rot = $item.RotationAroundProjectZDegrees
    $commentVal = switch ($item.Status) {
        "TILTED_OUT_OF_PROJECT_Z" { "[AxisAudit $date] TILTED - BasisZ lech 90deg vs Z project. Rot=${rot}deg" }
        "ROTATED_IN_VIEW"         { "[AxisAudit $date] ROTATED_IN_VIEW - BasisXY lech truc project. Rot=${rot}deg" }
        "ALIGNED"                 { "[AxisAudit $date] ALIGNED - OK" }
        default                   { "[AxisAudit $date] $($item.Status). Rot=${rot}deg" }
    }
    $changes    += [ordered]@{ ElementId = $item.ElementId; ParameterName = "Comments"; NewValue = $commentVal }
    $commentMap[$item.ElementId] = $commentVal
}
(@{ Changes = $changes } | ConvertTo-Json -Depth 5 -Compress) | Out-File $tmpPayload -Encoding UTF8 -NoNewline

$dr = & $bridge "parameter.set_safe" --payload $tmpPayload --dry-run true 2>$null | ConvertFrom-Json
if (-not $dr.Succeeded) { Write-Host "  DRY-RUN FAIL: $($dr.StatusCode)" -ForegroundColor Red; exit 1 }
$dr | ConvertTo-Json -Depth 10 | Out-File $tmpDryRun -Encoding UTF8
$ctx = ($dr.PayloadJson | ConvertFrom-Json).ResolvedContext
if ($ctx) { ($ctx | ConvertTo-Json -Depth 5 -Compress) | Out-File $ctxFile -Encoding UTF8 -NoNewline }
Write-Host "  Dry-run OK | elements=$($dr.ChangedIds.Count)"

$ex = & $bridge "parameter.set_safe" --payload $tmpPayload --dry-run false --approval-token $dr.ApprovalToken --preview-run-id $dr.PreviewRunId --expected-context $ctxFile 2>$null | ConvertFrom-Json
Write-Host "  Execute: $($ex.StatusCode)"
if (-not $ex.Succeeded) { Write-Host "  EXECUTE FAIL" -ForegroundColor Red; exit 1 }
Write-Host "  Done (changed=$(@($ex.ChangedIds).Count), 0=same value already set)" -ForegroundColor Green

Write-Host "--- STEP 3: Build CSV ---" -ForegroundColor Cyan
$csvLines = [System.Collections.Generic.List[string]]::new()
$csvLines.Add('"ElementId","FamilyName","TypeName","CategoryName","Status","Mirrored","RotationZ_deg","BasisX_XYZ","BasisY_XYZ","BasisZ_XYZ","Origin_X","Origin_Y","Origin_Z","AxisAudit_Comment"')
foreach ($item in ($all | Sort-Object FamilyName, TypeName)) {
    $bx  = "$($item.BasisX.X.ToString('F3')),$($item.BasisX.Y.ToString('F3')),$($item.BasisX.Z.ToString('F3'))"
    $by  = "$($item.BasisY.X.ToString('F3')),$($item.BasisY.Y.ToString('F3')),$($item.BasisY.Z.ToString('F3'))"
    $bz  = if ($item.BasisZ) { "$($item.BasisZ.X.ToString('F3')),$($item.BasisZ.Y.ToString('F3')),$($item.BasisZ.Z.ToString('F3'))" } else { ",," }
    $ox  = $item.Origin.X.ToString('F3')
    $oy  = $item.Origin.Y.ToString('F3')
    $oz  = $item.Origin.Z.ToString('F3')
    $tn  = if ($item.TypeName)  { $item.TypeName  -replace '"','""' } else { "" }
    $cmt = if ($commentMap.ContainsKey($item.ElementId)) { $commentMap[$item.ElementId] -replace '"','""' } else { "" }
    $csvLines.Add("`"$($item.ElementId)`",`"$($item.FamilyName)`",`"$tn`",`"$($item.CategoryName)`",`"$($item.Status)`",`"$($item.Mirrored)`",`"$($item.RotationAroundProjectZDegrees)`",`"$bx`",`"$by`",`"$bz`",`"$ox`",`"$oy`",`"$oz`",`"$cmt`"")
}
[System.IO.File]::WriteAllLines($csvOut, $csvLines, [System.Text.Encoding]::UTF8)
Write-Host "  Rows: $($csvLines.Count - 1)" -ForegroundColor Green
Write-Host "  File: $csvOut" -ForegroundColor Green

Write-Host "--- STEP 4: Summary ---" -ForegroundColor Cyan
$rows = Import-Csv $csvOut
Write-Host "  Total : $($rows.Count)"
Write-Host ""
Write-Host "  [By Status]" -ForegroundColor Yellow
$rows | Group-Object Status | Sort-Object Count -Descending | ForEach-Object {
    $c = if ($_.Name -eq "ALIGNED") { "Green" } else { "Red" }
    Write-Host ("    {0,-35}: {1,3}" -f $_.Name, $_.Count) -ForegroundColor $c
}
Write-Host ""
Write-Host "  [By Family]" -ForegroundColor Yellow
$rows | Group-Object FamilyName | Sort-Object Count -Descending | ForEach-Object {
    Write-Host ("    {0,-35}: {1,3}" -f $_.Name, $_.Count)
}
Write-Host ""
Write-Host "  [Top TypeNames]" -ForegroundColor Yellow
$rows | Group-Object TypeName | Sort-Object Count -Descending | Select-Object -First 10 | ForEach-Object {
    Write-Host ("    {0,-55}: {1,3}" -f $_.Name, $_.Count)
}
Write-Host ""
Write-Host "--- DONE ---" -ForegroundColor Green
Write-Host "  CSV: $csvOut"
