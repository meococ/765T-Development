. (Join-Path $PSScriptRoot "Assistant.Common.ps1")
$b = Resolve-BridgeExe

# Test bridge còn sống
$r = & $b "element.query" --payload "{`"FamilyName`":`"Mii_Pen-Rectangle`",`"MaxResults`":3}" 2>$null | ConvertFrom-Json
Write-Host "element.query Status: $($r.StatusCode)"
if ($r.Succeeded) {
    $d = $r.PayloadJson | ConvertFrom-Json
    $d.Elements | Select-Object -First 3 | ForEach-Object {
        Write-Host "  Id=$($_.ElementId) Family=$($_.FamilyName) Type=$($_.TypeName)"
    }
}

# Test element.explain với full payload
Write-Host ""
$payloadFile = Join-Path $PSScriptRoot "_explain_tmp.json"
(@{ ElementId = 128511204; IncludeParameters = $true; IncludeHostRelations = $true; IncludeDependents = $false } | ConvertTo-Json -Compress) | Out-File $payloadFile -Encoding UTF8 -NoNewline
$r2 = & $b "element.explain" --payload $payloadFile 2>$null | ConvertFrom-Json
Write-Host "element.explain Status: $($r2.StatusCode)"
if (-not $r2.Succeeded) {
    Write-Host "  Raw: $($r2 | ConvertTo-Json -Depth 3)"
} else {
    $r2.PayloadJson | ConvertFrom-Json | Select-Object FamilyName,TypeName,PlacementType,HostId,HostCategoryName,WorkPlaneName | Format-List
}
