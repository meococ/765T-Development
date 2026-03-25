Import-Csv (Join-Path $PSScriptRoot 'axis_audit_report.csv') |
Select-Object -First 5 |
ForEach-Object {
    Write-Host "--- $($_.ElementId) | $($_.Status) | Rot=$($_.RotationZ_deg)"
    Write-Host "  BasisX: $($_.BasisX_XYZ)"
    Write-Host "  BasisY: $($_.BasisY_XYZ)"
    Write-Host "  BasisZ: $($_.BasisZ_XYZ)"
    Write-Host "  Origin: $($_.Origin_X), $($_.Origin_Y), $($_.Origin_Z)"
}
