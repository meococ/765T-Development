. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge  = Resolve-BridgeExe
$payload = Join-Path $PSScriptRoot '_axis_payload.json'
$family  = 'Mii_Pen-Rec'   # broad match: catches Mii_Pen-Rec, Mii_Pen-Rectangle, etc.

Write-Host "=== Family Axis Review: '$family*' ===" -ForegroundColor Cyan

$raw    = & $bridge 'review.family_axis_alignment' --payload $payload 2>$null
$result = $raw | ConvertFrom-Json

if (-not $result.Succeeded) {
    Write-Host "THAT BAI: $($result.StatusCode)" -ForegroundColor Red
    $result.Diagnostics | ForEach-Object { Write-Host "  $($_.Message)" }
    exit 1
}

$data   = $result.PayloadJson | ConvertFrom-Json
$all    = @($data.Items | Where-Object { $_.FamilyName -like "*$family*" })
$issues = @($all | Where-Object { $_.MatchesProjectAxes -eq $false })
$ok     = @($all | Where-Object { $_.MatchesProjectAxes -eq $true })

Write-Host ""
Write-Host ("  Doc : Total={0}, Checked={1}, Aligned={2}, Mismatch={3}, Truncated={4}" -f `
    $data.TotalFamilyInstances, $data.CheckedCount, $data.AlignedCount, $data.MismatchCount, $data.Truncated) -ForegroundColor Gray
Write-Host ("  Filter '*{0}*' : Tim thay={1}, Aligned={2}, Lech={3}" -f $family, $all.Count, $ok.Count, $issues.Count) -ForegroundColor Cyan

if ($all.Count -eq 0) {
    Write-Host "`nKHONG tim thay family nao match '*$family*' trong $($data.CheckedCount) instances." -ForegroundColor Yellow
    Write-Host "Tat ca FamilyName duy nhat trong result:"
    $data.Items | Select-Object -ExpandProperty FamilyName | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" }
    exit 0
}

# Hien thi cac ten family khac nhau tim thay
$familyNames = $all | Select-Object -ExpandProperty FamilyName | Sort-Object -Unique
Write-Host ("  Family names khop: {0}" -f ($familyNames -join ', ')) -ForegroundColor DarkCyan

if ($issues.Count -gt 0) {
    Write-Host "`n--- LECH TRUC (can xu ly) ---" -ForegroundColor Red
    $issues | ForEach-Object {
        Write-Host ("  [Id {0}] {1} / {2}" -f $_.ElementId, $_.FamilyName, $_.TypeName) -ForegroundColor Yellow
        Write-Host ("    Status   : {0}  |  ProjectAxisStatus: {1}" -f $_.Status, $_.ProjectAxisStatus)
        Write-Host ("    Reason   : {0}" -f $_.Reason)
        Write-Host ("    Mirrored : {0}" -f $_.Mirrored)
        Write-Host ("    AngleX={0:F2}  AngleY={1:F2}  AngleZ={2:F2} deg" -f $_.AngleXDegrees, $_.AngleYDegrees, $_.AngleZDegrees)
        Write-Host ("    RotationZ(project): {0:F2} deg" -f $_.RotationAroundProjectZDegrees)
        Write-Host ("    BasisX: [{0:F4}, {1:F4}, {2:F4}]" -f $_.BasisX.X, $_.BasisX.Y, $_.BasisX.Z)
        Write-Host ("    BasisY: [{0:F4}, {1:F4}, {2:F4}]" -f $_.BasisY.X, $_.BasisY.Y, $_.BasisY.Z)
        Write-Host ("    Origin: X={0:F3} Y={1:F3} Z={2:F3} (internal unit)" -f $_.Origin.X, $_.Origin.Y, $_.Origin.Z)
        Write-Host ""
    }
} else {
    Write-Host "`nTAT CA $($all.Count) instances -> ALIGNED voi truc project." -ForegroundColor Green
}

Write-Host "--- CHI TIET TAT CA INSTANCES ---" -ForegroundColor Gray
$all | ForEach-Object {
    $color = if ($_.MatchesProjectAxes) { 'Green' } else { 'Red' }
    Write-Host ("  [Id {0}]  {1}/{2}  Rot={3:F1}deg  Mirror={4}  -> {5}" -f `
        $_.ElementId, $_.FamilyName, $_.TypeName,
        $_.RotationAroundProjectZDegrees, $_.Mirrored, $_.Status) -ForegroundColor $color
}
