. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge  = Resolve-BridgeExe
$payload = Join-Path $PSScriptRoot '_axis_payload.json'
$family  = 'Mii_Pen-Rec'

$raw    = & $bridge 'review.family_axis_alignment' --payload $payload 2>$null
$result = $raw | ConvertFrom-Json
$data   = $result.PayloadJson | ConvertFrom-Json
$all    = @($data.Items | Where-Object { $_.FamilyName -like "*$family*" })

Write-Host "=== Categories cua Mii_Pen-Rec* instances ==="
$all | Select-Object ElementId, FamilyName, TypeName, CategoryName, Status, RotationAroundProjectZDegrees |
    Sort-Object CategoryName, FamilyName |
    Format-Table -AutoSize
