. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe

Write-Host "=== TEST 1: payload rong (default) ===" -ForegroundColor Cyan
$raw1 = & $bridge 'review.family_axis_alignment' 2>$null
$r1 = $raw1 | ConvertFrom-Json
Write-Host "Status: $($r1.StatusCode), Succeeded: $($r1.Succeeded)"
if (-not $r1.Succeeded) { $r1.Diagnostics | ForEach-Object { Write-Host "  DIAG: $($_.Message)" } }

Write-Host ""
Write-Host "=== TEST 2: global variant ===" -ForegroundColor Cyan
$raw2 = & $bridge 'review.family_axis_alignment_global' 2>$null
$r2 = $raw2 | ConvertFrom-Json
Write-Host "Status: $($r2.StatusCode), Succeeded: $($r2.Succeeded)"
if (-not $r2.Succeeded) { $r2.Diagnostics | ForEach-Object { Write-Host "  DIAG: $($_.Message)" } }
