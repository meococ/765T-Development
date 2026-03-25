param(
    [string]$ProjectRoot = "",
    [string]$BridgeExe = "",
    [string]$OutputPath = "",
    [switch]$NoTimestampCopy
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Resolve-ProjectRoot
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$BridgeExe   = Resolve-BridgeExe -RequestedPath $BridgeExe
$contextRoot = Join-Path $ProjectRoot '.assistant\context'
New-Item -ItemType Directory -Force -Path $contextRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $contextRoot 'revit-task-context.latest.json'
}

$errors = New-Object System.Collections.Generic.List[object]

function Try-BridgeCall {
    param(
        [string]$Tool,
        [string]$TargetDocument = "",
        [string]$PayloadFile = ""
    )

    try {
        $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool `
            -TargetDocument $TargetDocument -PayloadFile $PayloadFile
        return @{
            Tool     = $Tool
            Response = $response
            Error    = $null
        }
    }
    catch {
        $msg = $_.Exception.Message
        $errors.Add([pscustomobject]@{
            Tool    = $Tool
            Message = $msg
        }) | Out-Null
        return @{
            Tool     = $Tool
            Response = $null
            Error    = $msg
        }
    }
}

# ─── 1 call: session.get_task_context (bundle: doc + view + selection + caps + recent) ─
$taskCtxPayload = '{"MaxRecentOperations":20,"MaxRecentEvents":20,"IncludeCapabilities":true,"IncludeToolCatalog":true}'
$taskCtxPayloadFile = Join-Path ([System.IO.Path]::GetTempPath()) "bim765t_tctx_$([System.Guid]::NewGuid().ToString('N')).json"
try {
    [System.IO.File]::WriteAllText($taskCtxPayloadFile, $taskCtxPayload, [System.Text.Encoding]::UTF8)
    $taskCtxResult = Try-BridgeCall -Tool 'session.get_task_context' -PayloadFile $taskCtxPayloadFile
}
finally {
    if (Test-Path $taskCtxPayloadFile) { Remove-Item $taskCtxPayloadFile -Force -ErrorAction SilentlyContinue }
}

# Unpack bundle
$bundleCtx        = $null
$activeDoc        = $null
$documentKey      = ''
$activeView       = $null
$levelContext     = $null
$selection        = $null
$capabilities     = $null
$toolCatalog      = $null
$recentEvents     = $null
$recentOperations = $null

if ($taskCtxResult.Response -and $taskCtxResult.Response.Succeeded -and $taskCtxResult.Response.PayloadJson) {
    try { $bundleCtx = $taskCtxResult.Response.PayloadJson | ConvertFrom-Json } catch { }
}

if ($bundleCtx) {
    $activeDoc        = $bundleCtx.Document
    $activeView       = $bundleCtx.ActiveContext
    $selection        = $bundleCtx.Selection
    $capabilities     = $bundleCtx.Capabilities
    $recentEvents     = $bundleCtx.RecentEvents
    $recentOperations = $bundleCtx.RecentOperations

    if ($activeDoc -and $activeDoc.DocumentKey) {
        $documentKey = $activeDoc.DocumentKey
    }

    # ToolCatalog từ bundle (IncludeToolCatalog=true)
    if ($bundleCtx.Tools -and @($bundleCtx.Tools).Count -gt 0) {
        $toolCatalog = [pscustomobject]@{ Tools = $bundleCtx.Tools }
    }
    elseif ($capabilities -and $capabilities.Tools -and @($capabilities.Tools).Count -gt 0) {
        $toolCatalog = [pscustomobject]@{ Tools = $capabilities.Tools }
    }
}

# LevelContext — không có trong bundle, vẫn cần 1 call riêng
$levelResult  = Try-BridgeCall -Tool 'view.get_current_level_context'
if ($levelResult.Response -and $levelResult.Response.PayloadJson) {
    try { $levelContext = $levelResult.Response.PayloadJson | ConvertFrom-Json } catch { }
}

# ─── 2 review tools (nặng, chỉ chạy khi có document) ─────────────────────────
$healthResult          = $null
$activeViewSummaryResult = $null
$modelHealth           = $null
$activeViewSummary     = $null

if ($documentKey) {
    $healthResult          = Try-BridgeCall -Tool 'review.model_health'           -TargetDocument $documentKey
    $activeViewSummaryResult = Try-BridgeCall -Tool 'review.active_view_summary'  -TargetDocument $documentKey
}

if ($healthResult -and $healthResult.Response -and $healthResult.Response.PayloadJson) {
    try { $modelHealth = $healthResult.Response.PayloadJson | ConvertFrom-Json } catch { }
}
if ($activeViewSummaryResult -and $activeViewSummaryResult.Response -and $activeViewSummaryResult.Response.PayloadJson) {
    try { $activeViewSummary = $activeViewSummaryResult.Response.PayloadJson | ConvertFrom-Json } catch { }
}

# ─── Compose final payload ────────────────────────────────────────────────────
$payload = [pscustomobject]@{
    GeneratedAtUtc   = [DateTime]::UtcNow.ToString('o')
    ProjectRoot      = $ProjectRoot
    BridgeExe        = $BridgeExe
    RevitOnline      = [bool]($bundleCtx -ne $null -and $activeDoc -ne $null)
    ActiveDocument   = $activeDoc
    ActiveView       = $activeView
    LevelContext     = $levelContext
    Selection        = $selection
    Capabilities     = $capabilities
    ToolCatalog      = $toolCatalog
    RecentEvents     = $recentEvents
    RecentOperations = $recentOperations
    ModelHealth      = $modelHealth
    ActiveViewSummary = $activeViewSummary
    BrokerTaskContext = $bundleCtx   # full bundle để backward compat với Mode C scripts
    Errors           = $errors
}

Write-JsonFile -Path $OutputPath -Data $payload

$timestampPath = $null
if (-not $NoTimestampCopy) {
    $timestampPath = Join-Path $contextRoot ("revit-task-context.{0}.json" -f ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')))
    Write-JsonFile -Path $timestampPath -Data $payload
}

[pscustomobject]@{
    Succeeded     = $true
    OutputPath    = $OutputPath
    TimestampCopy = $timestampPath
    RevitOnline   = $payload.RevitOnline
    DocumentTitle = if ($payload.ActiveDocument) { $payload.ActiveDocument.Title } else { $null }
    ViewName      = if ($payload.ActiveView) { $payload.ActiveView.ViewName } else { $null }
    ErrorCount    = $errors.Count
} | ConvertTo-Json -Depth 20
