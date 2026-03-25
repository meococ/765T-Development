. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge  = Resolve-BridgeExe
$payload = Join-Path $PSScriptRoot '_axis_payload.json'

$raw    = & $bridge 'review.family_axis_alignment' --payload $payload 2>$null
$result = $raw | ConvertFrom-Json

Write-Host "Succeeded: $($result.Succeeded)"
Write-Host "StatusCode: $($result.StatusCode)"

if ($result.Succeeded -and $result.PayloadJson) {
    $data = $result.PayloadJson | ConvertFrom-Json
    Write-Host "`n--- PayloadJson top-level fields ---"
    $data.PSObject.Properties | ForEach-Object { Write-Host "  $($_.Name) = $($_.Value)" }

    if ($data.Items -and $data.Items.Count -gt 0) {
        Write-Host "`n--- Sample Item[0] fields ---"
        $data.Items[0].PSObject.Properties | ForEach-Object { Write-Host "  $($_.Name) = $($_.Value)" }
    }
} else {
    Write-Host "`nDiagnostics:"
    $result.Diagnostics | ForEach-Object { Write-Host "  $($_.Code): $($_.Message)" }
    Write-Host "`nReviewSummaryJson: $($result.ReviewSummaryJson)"
}
