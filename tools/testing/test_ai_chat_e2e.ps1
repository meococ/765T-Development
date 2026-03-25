<#
.SYNOPSIS
    Test AI chat end-to-end via WorkerHost HTTP API.
.DESCRIPTION
    Sends a chat message to /api/external-ai/chat and reports the full
    response including mission state, reasoning mode, provider used.
    Use to verify the AI connection without opening Revit UI.
.PARAMETER Message
    The chat message to send. Default: "Chao ban, hay gioi thieu ban than."
.PARAMETER WorkerHostUrl
    Base URL of WorkerHost HTTP API. Default: http://localhost:50765
.PARAMETER SessionId
    Chat session ID. Default: auto-generated.
.PARAMETER TimeoutSec
    HTTP request timeout in seconds. Default: 30.
.PARAMETER AsJson
    Output raw JSON response.
.PARAMETER Verbose
    Show detailed request/response info.
.EXAMPLE
    .\test_ai_chat_e2e.ps1
    .\test_ai_chat_e2e.ps1 -Message "List all walls in the model"
    .\test_ai_chat_e2e.ps1 -Message "Create a wall" -Verbose
#>
param(
    [string]$Message = 'Chao ban, hay gioi thieu ban than.',
    [string]$WorkerHostUrl = 'http://localhost:50765',
    [string]$SessionId = '',
    [int]$TimeoutSec = 30,
    [switch]$AsJson,
    [switch]$ShowVerbose
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$statusTimeoutSec = [Math]::Max(15, [Math]::Min($TimeoutSec, 30))

if ([string]::IsNullOrWhiteSpace($SessionId)) {
    $SessionId = "test-session-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
}

$missionId = "test-mission-$([Guid]::NewGuid().ToString('N').Substring(0,8))"

# ── 1. Pre-flight check ──────────────────────────────────────────────
Write-Host ""
Write-Host "=== BIM765T AI Chat E2E Test ===" -ForegroundColor Cyan
Write-Host "Target: $WorkerHostUrl" -ForegroundColor DarkGray
Write-Host ""

# Check status first
Write-Host "[1/3] Checking WorkerHost status..." -ForegroundColor White
try {
    $status = Invoke-RestMethod -Uri "$WorkerHostUrl/api/external-ai/status" -Method Get -TimeoutSec $statusTimeoutSec
    $restartRequired = [bool]$status.RestartRequired
    Write-Host "  Provider: $($status.ConfiguredProvider)" -ForegroundColor Green
    Write-Host "  Model:    $($status.PlannerModel)" -ForegroundColor Green
    Write-Host "  Mode:     $($status.ReasoningMode)" -ForegroundColor Green
    if ($restartRequired) {
        Write-Host "  Warning:  Revit runtime restart required" -ForegroundColor Yellow
        foreach ($warning in @($status.StatusWarnings | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            Write-Host "            $warning" -ForegroundColor DarkYellow
        }
    }
}
catch {
    Write-Host "  ERROR: Cannot reach WorkerHost at $WorkerHostUrl" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor DarkRed
    Write-Host ""
    Write-Host "  Fix: Start WorkerHost first:" -ForegroundColor Yellow
    Write-Host "    .\tools\start_workerhost.ps1" -ForegroundColor DarkGray
    exit 1
}

# ── 2. Send chat message ─────────────────────────────────────────────
Write-Host ""
Write-Host "[2/3] Sending chat message..." -ForegroundColor White
Write-Host "  Session:  $SessionId" -ForegroundColor DarkGray
Write-Host "  Mission:  $missionId" -ForegroundColor DarkGray
Write-Host "  Message:  $Message" -ForegroundColor Yellow
Write-Host ""

$chatUrl = "$WorkerHostUrl/api/external-ai/chat"
$body = @{
    MissionId       = $missionId
    SessionId       = $SessionId
    Message         = $Message
    PersonaId       = 'freelancer-default'
    ContinueMission = $true
    TimeoutMs       = $TimeoutSec * 1000
} | ConvertTo-Json -Depth 10

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $response = Invoke-RestMethod `
        -Uri $chatUrl `
        -Method Post `
        -ContentType 'application/json; charset=utf-8' `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) `
        -TimeoutSec $TimeoutSec `
        -ErrorAction Stop

    $stopwatch.Stop()
}
catch {
    $stopwatch.Stop()
    Write-Host "  ERROR: Chat request failed" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor DarkRed

    # Try to extract response body
    if ($_.Exception.Response) {
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $errBody = $reader.ReadToEnd()
            Write-Host "  Response: $errBody" -ForegroundColor DarkRed
        }
        catch {}
    }

    exit 1
}

# ── 3. Report results ────────────────────────────────────────────────
Write-Host "[3/3] Response received ($($stopwatch.ElapsedMilliseconds)ms)" -ForegroundColor White
Write-Host ""

if ($AsJson) {
    $response | ConvertTo-Json -Depth 20
    exit 0
}

# Formatted output
$succeeded = [bool]$response.Succeeded
$stateColor = if ($succeeded) { 'Green' } else { 'Red' }

Write-Host "  --- Mission Result ---" -ForegroundColor Cyan
Write-Host "  Mission ID:    $($response.MissionId)" -ForegroundColor Gray
Write-Host "  State:         $($response.State)" -ForegroundColor $stateColor
Write-Host "  Succeeded:     $succeeded" -ForegroundColor $stateColor
Write-Host "  Status Code:   $($response.StatusCode)" -ForegroundColor Gray
Write-Host "  Provider:      $($response.ConfiguredProvider)" -ForegroundColor Yellow
Write-Host "  Model:         $($response.PlannerModel)" -ForegroundColor Yellow
Write-Host "  Reasoning:     $($response.ReasoningMode)" -ForegroundColor Yellow
Write-Host "  Latency:       $($stopwatch.ElapsedMilliseconds)ms" -ForegroundColor Gray
Write-Host ""

if ($response.ResponseText) {
    Write-Host "  --- AI Response ---" -ForegroundColor Cyan
    Write-Host "  $($response.ResponseText)" -ForegroundColor White
    Write-Host ""
}

if ($response.HasPendingApproval) {
    Write-Host "  [!] Pending approval required" -ForegroundColor Yellow
    Write-Host "  Approve: POST $WorkerHostUrl/api/external-ai/missions/$missionId/approve" -ForegroundColor DarkGray
    Write-Host ""
}

# Show events
$events = $response.Events
if ($events -and $events.Count -gt 0) {
    Write-Host "  --- Mission Events ($($events.Count)) ---" -ForegroundColor Cyan
    foreach ($evt in $events) {
        $evtTime = if ($evt.PSObject.Properties['OccurredUtc'] -and $evt.OccurredUtc) { $evt.OccurredUtc } elseif ($evt.PSObject.Properties['TimestampUtc'] -and $evt.TimestampUtc) { $evt.TimestampUtc } else { '' }
        $evtKind = if ($evt.PSObject.Properties['EventType'] -and $evt.EventType) { $evt.EventType } elseif ($evt.PSObject.Properties['Kind'] -and $evt.Kind) { $evt.Kind } else { 'event' }
        $evtSummary = if ($evt.PSObject.Properties['Summary'] -and $evt.Summary) { $evt.Summary } elseif ($evt.PSObject.Properties['PayloadJson'] -and $evt.PayloadJson) { $evt.PayloadJson } else { '' }
        if ($evtSummary.Length -gt 140) {
            $evtSummary = $evtSummary.Substring(0, 140) + '...'
        }
        Write-Host "  [$evtTime] ${evtKind}: $evtSummary" -ForegroundColor DarkGray
    }
    Write-Host ""
}

# Verdict
Write-Host "  --- Verdict ---" -ForegroundColor Cyan
if ($succeeded) {
    $isLlm = $response.ReasoningMode -eq 'llm_validated'
    if ($restartRequired) {
        Write-Host "  [!!] WorkerHost is on the new MiniMax config, but Revit runtime is still stale. Restart Revit and re-run this test." -ForegroundColor Yellow
    }
    elseif ($isLlm) {
        Write-Host "  [OK] AI chat working end-to-end with LLM ($($response.ConfiguredProvider))" -ForegroundColor Green
    }
    elseif ($status -and $status.ReasoningMode -eq 'llm_validated') {
        Write-Host "  [OK] Runtime is LLM-enabled; this prompt resolved through the rule-first lane after planning." -ForegroundColor Yellow
        Write-Host "  Provider/runtime are healthy: $($status.ConfiguredProvider) / $($status.PlannerModel)" -ForegroundColor DarkGray
    }
    else {
        Write-Host "  [OK] Chat responded (rule-first mode, no LLM call)" -ForegroundColor Yellow
        Write-Host "  To use LLM: Set API key and restart WorkerHost" -ForegroundColor DarkGray
    }
}
else {
    Write-Host "  [!!] Chat failed - check WorkerHost logs and Revit kernel" -ForegroundColor Red
    Write-Host "  Log location: artifacts\workerhost\logs\" -ForegroundColor DarkGray
}

Write-Host ""

# Save response to artifacts
$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$artifactsDir = Join-Path $projectRoot 'artifacts\ai-tests'
New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifactPath = Join-Path $artifactsDir "chat-e2e-$stamp.json"

[pscustomobject]@{
    Timestamp   = [DateTime]::UtcNow.ToString('O')
    Message     = $Message
    SessionId   = $SessionId
    MissionId   = $missionId
    LatencyMs   = $stopwatch.ElapsedMilliseconds
    Response    = $response
} | ConvertTo-Json -Depth 20 | Set-Content -Path $artifactPath -Encoding UTF8

Write-Host "  Response saved: $artifactPath" -ForegroundColor DarkGray
Write-Host ""

exit $(if ($succeeded) { 0 } else { 1 })
