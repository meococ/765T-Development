param([switch]$Execute)
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge   = Resolve-BridgeExe
$payload  = Join-Path $PSScriptRoot '_schedule_payload.json'
$tmpDr    = Join-Path $PSScriptRoot '_schedule_dryrun.json'
$ctxFile  = Join-Path $PSScriptRoot '_schedule_context.json'

if ($Execute) {
    if (-not (Test-Path $tmpDr)) { Write-Host "CHUA CO dry-run. Chay khong co -Execute truoc." -ForegroundColor Red; exit 1 }
    $dr = (Get-Content $tmpDr -Raw) | ConvertFrom-Json

    Write-Host "CREATING schedule..." -ForegroundColor Yellow
    $raw = & $bridge 'schedule.create_safe' --payload $payload `
        --dry-run false `
        --approval-token $dr.ApprovalToken `
        --preview-run-id $dr.PreviewRunId `
        --expected-context $ctxFile 2>$null
    $res = $raw | ConvertFrom-Json
    Write-Host "Status   : $($res.StatusCode)"
    Write-Host "Succeeded: $($res.Succeeded)"
    $res.Diagnostics | ForEach-Object { Write-Host "  [$($_.Severity)] $($_.Message)" }
    if ($res.Succeeded) {
        $data = $res.PayloadJson | ConvertFrom-Json
        Write-Host ""
        Write-Host "SCHEDULE DA TAO:" -ForegroundColor Green
        Write-Host "  Name     : $($data.ScheduleName)"
        Write-Host "  ElementId: $($data.ScheduleElementId)"
        Write-Host "  Category : $($data.CategoryName)"
        Write-Host "  Rows     : $($data.RowCount)"
    }
} else {
    Write-Host "PREVIEW schedule..." -ForegroundColor Cyan
    $raw = & $bridge 'schedule.preview_create' --payload $payload 2>$null
    $res = $raw | ConvertFrom-Json
    Write-Host "Status   : $($res.StatusCode)"
    Write-Host "Succeeded: $($res.Succeeded)"
    $res.Diagnostics | ForEach-Object { Write-Host "  [$($_.Severity)] $($_.Message)" }

    if ($res.Succeeded) {
        $data = $res.PayloadJson | ConvertFrom-Json
        Write-Host ""
        Write-Host "PREVIEW:" -ForegroundColor Cyan
        Write-Host "  ScheduleName     : $($data.ScheduleName)"
        Write-Host "  Category         : $($data.CategoryName) (Id=$($data.ResolvedCategoryId))"
        Write-Host "  ExistingSchedule : $($data.ExistingScheduleId)"
        Write-Host "  Fields resolved  : $($data.ResolvedFields.Count)"
        $data.ResolvedFields | ForEach-Object { Write-Host "    - $($_.ParameterName) -> '$($_.ColumnHeading)' (found=$($_.Found))" }
        Write-Host "  Filters          : $($data.ResolvedFilters.Count)"
        $data.ResolvedFilters | ForEach-Object { Write-Host "    - $($_.ParameterName) $($_.Operator) '$($_.Value)' (valid=$($_.Valid))" }

        # Dry-run create để lấy token
        Write-Host "`nDRY-RUN create..." -ForegroundColor Cyan
        $raw2 = & $bridge 'schedule.create_safe' --payload $payload --dry-run true 2>$null
        $res2 = $raw2 | ConvertFrom-Json
        Write-Host "Status   : $($res2.StatusCode)"
        Write-Host "Succeeded: $($res2.Succeeded)"

        if ($res2.Succeeded -and $res2.ApprovalToken) {
            $raw2 | Out-File $tmpDr -Encoding UTF8
            $ctx = ($res2.PayloadJson | ConvertFrom-Json).ResolvedContext
            if ($ctx) { ($ctx | ConvertTo-Json -Depth 5 -Compress) | Out-File $ctxFile -Encoding UTF8 -NoNewline }

            Write-Host ""
            Write-Host "====== APPROVAL ======" -ForegroundColor Yellow
            Write-Host "Token : $($res2.ApprovalToken)" -ForegroundColor Green
            Write-Host ""
            Write-Host "De tao schedule thuc su:"
            Write-Host "  .\tools\_create_schedule.ps1 -Execute"
        }
    }
}
