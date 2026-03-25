<#
.SYNOPSIS
    Dump session state nhanh ra .assistant/context/_session_state.json.
    Agent đọc file này để bootstrap context mà không cần gọi 4-5 tools riêng lẻ.

.DESCRIPTION
    Gọi session.get_task_context (1 tool call) → extract các field quan trọng
    → ghi ra _session_state.json (~5KB, không có tool catalog).

    Agent nên đọc file này đầu mỗi task thay vì gọi:
      session.list_tools, session.get_capabilities,
      document.get_active, document.get_context_fingerprint

    File được coi là "fresh" nếu AgeSecs < 300 (5 phút).
    Sau 5 phút, agent nên gọi lại session.get_task_context trực tiếp.

.PARAMETER BridgeExe
    Đường dẫn Bridge.exe. Nếu bỏ trống, tự resolve.

.PARAMETER OutputPath
    Đường dẫn ghi file. Mặc định: .assistant/context/_session_state.json

.PARAMETER AsJson
    Output kết quả dạng JSON thay vì Format-List.

.EXAMPLE
    .\tools\update_session_state.ps1
    .\tools\update_session_state.ps1 -AsJson
#>
param(
    [string]$BridgeExe  = "",
    [string]$OutputPath = "",
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe    = Resolve-BridgeExe -RequestedPath $BridgeExe
$projectRoot  = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$contextRoot  = Join-Path $projectRoot '.assistant\context'
New-Item -ItemType Directory -Force -Path $contextRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $contextRoot '_session_state.json'
}

# ─── Gọi session.get_task_context (1 call, IncludeToolCatalog=false để nhẹ) ───
$payloadJson = '{"MaxRecentOperations":5,"MaxRecentEvents":5,"IncludeCapabilities":true,"IncludeToolCatalog":false}'
$payloadFile = Join-Path ([System.IO.Path]::GetTempPath()) "bim765t_task_ctx_$([System.Guid]::NewGuid().ToString('N')).json"
try {
    [System.IO.File]::WriteAllText($payloadFile, $payloadJson, [System.Text.Encoding]::UTF8)
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe `
        -Tool 'session.get_task_context' `
        -PayloadFile $payloadFile
}
finally {
    if (Test-Path $payloadFile) { Remove-Item $payloadFile -Force -ErrorAction SilentlyContinue }
}

$now = [DateTime]::UtcNow

if (-not $response -or -not $response.Succeeded) {
    # Bridge offline — ghi state "offline" để agent biết
    $offlineState = [pscustomobject]@{
        UpdatedAtUtc   = $now.ToString('o')
        BridgeAlive    = $false
        WriteEnabled   = $false
        SaveEnabled    = $false
        ActiveDocument = $null
        ActiveView     = $null
        SelectionCount = 0
        Fingerprint    = $null
        RuntimeToolCount = 0
        AgeSecs        = 0
        Error          = if ($response) { $response.StatusCode } else { 'NoResponse' }
    }
    Write-JsonFileAtomically -Path $OutputPath -Data $offlineState

    $result = [pscustomobject]@{
        Succeeded    = $false
        BridgeAlive  = $false
        OutputPath   = $OutputPath
        Error        = $offlineState.Error
    }
    if ($AsJson) { $result | ConvertTo-Json -Depth 5 } else { $result | Format-List }
    return
}

# ─── Unpack TaskContextResponse ───────────────────────────────────────────────
$ctx = $null
if ($response.PayloadJson) {
    try { $ctx = $response.PayloadJson | ConvertFrom-Json } catch { $ctx = $null }
}

$doc         = if ($ctx) { $ctx.Document }        else { $null }
$activeCtx   = if ($ctx) { $ctx.ActiveContext }   else { $null }
$selection   = if ($ctx) { $ctx.Selection }       else { $null }
$fingerprint = if ($ctx) { $ctx.Fingerprint }     else { $null }
$caps        = if ($ctx) { $ctx.Capabilities }    else { $null }

# WriteEnabled: dùng shortcut field mới; fallback sang Capabilities nếu agent dùng build cũ
$writeEnabled = $false
if ($ctx -and (Get-Member -InputObject $ctx -Name 'WriteEnabled' -MemberType NoteProperty -ErrorAction SilentlyContinue)) {
    $writeEnabled = [bool]$ctx.WriteEnabled
}
elseif ($caps) {
    $writeEnabled = [bool]$caps.AllowWriteTools
}

$saveEnabled = if ($caps) { [bool]$caps.AllowSaveTools } else { $false }

# RuntimeToolCount: từ Capabilities.Tools nếu có
$runtimeToolCount = 0
if ($caps -and $caps.Tools) {
    $runtimeToolCount = @($caps.Tools).Count
}

# ActiveDocument summary — chỉ giữ fields hữu ích cho cold-start
$docSummary = $null
if ($doc) {
    $docSummary = [pscustomobject]@{
        DocumentKey  = $doc.DocumentKey
        Title        = $doc.Title
        IsWorkshared = [bool]$doc.IsWorkshared
        IsModified   = [bool]$doc.IsModified
        CanSave      = [bool]$doc.CanSave
        CanSynchronize = [bool]$doc.CanSynchronize
    }
}

# ActiveView summary
$viewSummary = $null
if ($activeCtx) {
    $viewSummary = [pscustomobject]@{
        ViewName = $activeCtx.ViewName
        ViewType = $activeCtx.ViewType
        LevelName = $activeCtx.LevelName
    }
}

# Fingerprint summary
$fpSummary = $null
if ($fingerprint) {
    $fpSummary = [pscustomobject]@{
        DocumentKey  = $fingerprint.DocumentKey
        ViewKey      = $fingerprint.ViewKey
        SelectionHash = $fingerprint.SelectionHash
        ActiveDocEpoch = $fingerprint.ActiveDocEpoch
    }
}

# ─── Ghi _session_state.json ──────────────────────────────────────────────────
$state = [pscustomobject]@{
    UpdatedAtUtc     = $now.ToString('o')
    BridgeAlive      = $true
    WriteEnabled     = $writeEnabled
    SaveEnabled      = $saveEnabled
    ActiveDocument   = $docSummary
    ActiveView       = $viewSummary
    SelectionCount   = if ($selection) { [int]$selection.Count } else { 0 }
    Fingerprint      = $fpSummary
    RuntimeToolCount = $runtimeToolCount
    AgeSecs          = 0
}

Write-JsonFileAtomically -Path $OutputPath -Data $state

$result = [pscustomobject]@{
    Succeeded        = $true
    BridgeAlive      = $true
    WriteEnabled     = $writeEnabled
    OutputPath       = $OutputPath
    DocumentTitle    = if ($docSummary) { $docSummary.Title } else { $null }
    ViewName         = if ($viewSummary) { $viewSummary.ViewName } else { $null }
    RuntimeToolCount = $runtimeToolCount
}

if ($AsJson) { $result | ConvertTo-Json -Depth 5 } else { $result | Format-List }
