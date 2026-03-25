# Ghi axis status vào Comments của tất cả Mii_Pen-Rec* instances
# Chạy dry-run trước: .\tools\_write_axis_comments.ps1
# Sau đó execute:     .\tools\_write_axis_comments.ps1 -Execute

param([switch]$Execute)

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge       = Resolve-BridgeExe
$axisPayload  = Join-Path $PSScriptRoot '_axis_payload.json'
$tmpPayload   = Join-Path $PSScriptRoot '_comments_payload.json'
$tmpDryRun    = Join-Path $PSScriptRoot '_comments_dryrun.json'
$family       = 'Mii_Pen-Rec'
$date         = (Get-Date).ToString("yyyy-MM-dd")

# ---- 1. Scan axis ----
Write-Host "Dang scan axis alignment toan document..." -ForegroundColor Cyan
$raw    = & $bridge 'review.family_axis_alignment' --payload $axisPayload 2>$null
$result = $raw | ConvertFrom-Json
if (-not $result.Succeeded) { Write-Host "SCAN THAT BAI: $($result.StatusCode)" -ForegroundColor Red; exit 1 }
$data = $result.PayloadJson | ConvertFrom-Json
$all  = @($data.Items | Where-Object { $_.FamilyName -like "*$family*" })
Write-Host "  Tim thay $($all.Count) instances cua '$family*'" -ForegroundColor Green

# ---- 2. Build Changes + write payload ----
$changes = @()
foreach ($item in $all) {
    $commentVal = switch ($item.Status) {
        'TILTED_OUT_OF_PROJECT_Z' { "[AxisAudit $date] TILTED - BasisZ lech 90deg vs Z project. Rot=$($item.RotationAroundProjectZDegrees)deg" }
        'ROTATED_IN_VIEW'         { "[AxisAudit $date] ROTATED_IN_VIEW - BasisXY lech truc project. Rot=$($item.RotationAroundProjectZDegrees)deg" }
        default                   { "[AxisAudit $date] $($item.Status). Rot=$($item.RotationAroundProjectZDegrees)deg" }
    }
    $changes += [ordered]@{ ElementId = $item.ElementId; ParameterName = "Comments"; NewValue = $commentVal }
}
(@{ Changes = $changes } | ConvertTo-Json -Depth 5 -Compress) | Out-File $tmpPayload -Encoding UTF8 -NoNewline
Write-Host "  Payload: $($changes.Count) parameter updates -> $tmpPayload" -ForegroundColor Cyan

# ---- 3. Execute hay Dry-run ----
if ($Execute) {
    # Load dry-run result đã lưu
    if (-not (Test-Path $tmpDryRun)) {
        Write-Host "CHUA CO dry-run result. Chay truoc khong co -Execute." -ForegroundColor Red; exit 1
    }
    $dr          = (Get-Content $tmpDryRun -Raw) | ConvertFrom-Json
    $token       = $dr.ApprovalToken
    $runId       = $dr.PreviewRunId
    $ctxFile     = Join-Path $PSScriptRoot '_comments_context.json'

    Write-Host "`nEXECUTING..." -ForegroundColor Yellow
    $raw2 = & $bridge 'parameter.set_safe' --payload $tmpPayload `
        --dry-run false `
        --approval-token $token `
        --preview-run-id $runId `
        --expected-context $ctxFile 2>$null
    $res2 = $raw2 | ConvertFrom-Json
    Write-Host "Status   : $($res2.StatusCode)"
    Write-Host "Succeeded: $($res2.Succeeded)"
    $res2.Diagnostics | ForEach-Object { Write-Host "  [$($_.Severity)] $($_.Message)" }
    if ($res2.Succeeded) {
        Write-Host "`nGHI XONG! $($res2.ChangedIds.Count) elements da duoc update Comments." -ForegroundColor Green
    }
} else {
    Write-Host "`nDRY-RUN..." -ForegroundColor Cyan
    $raw2 = & $bridge 'parameter.set_safe' --payload $tmpPayload --dry-run true 2>$null
    $res2 = $raw2 | ConvertFrom-Json

    Write-Host "Status   : $($res2.StatusCode)"
    Write-Host "Succeeded: $($res2.Succeeded)"
    $res2.Diagnostics | ForEach-Object { Write-Host "  [$($_.Severity)] $($_.Message)" }

    if ($res2.Succeeded -and $res2.ApprovalToken) {
        # Lưu toàn bộ dry-run response
        $raw2 | Out-File $tmpDryRun -Encoding UTF8

        # Extract ResolvedContext từ PayloadJson -> lưu riêng cho --expected-context
        $ctxFile = Join-Path $PSScriptRoot '_comments_context.json'
        $execResult  = $res2.PayloadJson | ConvertFrom-Json
        if ($execResult.ResolvedContext) {
            ($execResult.ResolvedContext | ConvertTo-Json -Depth 5 -Compress) | Out-File $ctxFile -Encoding UTF8 -NoNewline
            Write-Host "  ResolvedContext saved -> $ctxFile" -ForegroundColor Gray
        }

        Write-Host "`n====== APPROVAL ======" -ForegroundColor Yellow
        Write-Host "Token      : $($res2.ApprovalToken)" -ForegroundColor Green
        Write-Host "PreviewRunId: $($res2.PreviewRunId)" -ForegroundColor Green
        Write-Host "ChangedIds : $($res2.ChangedIds.Count) elements" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Chay execute:" -ForegroundColor Cyan
        Write-Host "  .\tools\_write_axis_comments.ps1 -Execute"
    } elseif (-not $res2.Succeeded) {
        Write-Host "DRY-RUN THAT BAI:" -ForegroundColor Red
        $res2 | ConvertTo-Json -Depth 5
    }
}
