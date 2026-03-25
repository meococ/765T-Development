<#
.SYNOPSIS
    Goi Claude tu trong Codex session.
    Codex dung script nay de "call Claude" review, plan, debug, v.v.

.PARAMETER Role
    Vai tro gan cho Claude: reviewer, planner, debugger, executor, critic, architect.

.PARAMETER Task
    Mo ta task can Claude xu ly.

.PARAMETER ContextFiles
    Comma-separated paths toi files can doc.

.PARAMETER Model
    Override model: haiku, sonnet, opus. Mac dinh: opus.

.PARAMETER IncludeRevitContext
    Tu dong dinh kem revit-task-context.latest.json.

.PARAMETER UseModeCSchema
    Ep Claude tra JSON theo mode-c-dual-agent-result.schema.json.

.EXAMPLE
    .\invoke_claude_agent.ps1 -Role reviewer -Task "Review security of PipeServerHostedService"
    .\invoke_claude_agent.ps1 -Role architect -Task "Evaluate PlatformServices refactoring" -Model opus
    .\invoke_claude_agent.ps1 -Role debugger -Task "Debug pipe timeout" -IncludeRevitContext
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('reviewer', 'planner', 'debugger', 'executor', 'critic', 'architect')]
    [string]$Role,

    [Parameter(Mandatory)]
    [string]$Task,

    [string]$ContextFiles = '',
    [string]$Model = '',
    [switch]$IncludeRevitContext,
    [switch]$UseModeCSchema,
    [switch]$SaveRun
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot

# --- Load config ---
$configPath = Join-Path $projectRoot '.assistant\config\agent-bridge.json'
$config = $null
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
}

# --- Resolve Claude CLI ---
$claudeExe = Resolve-ClaudeExe

# --- Resolve model ---
$resolvedModel = if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $Model
}
elseif ($config -and $config.claude.defaultModel) {
    $config.claude.defaultModel
}
else {
    'opus'
}

# --- Build role prompt ---
$roleDescription = ''
if ($config -and $config.roles.$Role) {
    $roleDescription = $config.roles.$Role
}
if ([string]::IsNullOrWhiteSpace($roleDescription)) {
    $roleDescription = "Act as a $Role for this BIM/Revit automation project."
}

# --- Build prompt ---
$prompt = @"
# Role: $Role
$roleDescription

# Task
$Task
"@

# Attach context files
if (-not [string]::IsNullOrWhiteSpace($ContextFiles)) {
    $files = $ContextFiles -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    foreach ($f in $files) {
        $fullPath = if ([System.IO.Path]::IsPathRooted($f)) { $f } else { Join-Path $projectRoot $f }
        if (Test-Path $fullPath) {
            $content = Get-Content $fullPath -Raw -ErrorAction SilentlyContinue
            if ($content.Length -gt 50000) {
                $content = $content.Substring(0, 50000) + "`n... [TRUNCATED at 50KB]"
            }
            $relativePath = $fullPath.Replace($projectRoot, '').TrimStart('\', '/')
            $prompt += "`n`n## File: $relativePath`n``````n$content`n```````n"
        }
    }
}

# Attach Revit context
if ($IncludeRevitContext) {
    $revitCtx = Join-Path $projectRoot '.assistant\context\revit-task-context.latest.json'
    if (Test-Path $revitCtx) {
        $ctxContent = Get-Content $revitCtx -Raw
        if ($ctxContent.Length -gt 20000) {
            $ctxContent = $ctxContent.Substring(0, 20000) + "`n... [TRUNCATED]"
        }
        $prompt += "`n`n## Revit Context`n``````json`n$ctxContent`n```````n"
    }
}

# --- Build claude args ---
$claudeArgs = @('-p', '-', '--model', $resolvedModel, '--output-format', 'json')

$schemaPath = ''
if ($UseModeCSchema) {
    $schemaPath = Join-Path $projectRoot '.assistant\schemas\mode-c-dual-agent-result.schema.json'
    if (Test-Path $schemaPath) {
        $claudeArgs += @('--json-schema', $schemaPath)
    }
}

$appendSystem = "You are role=$Role. Return structured JSON: {summary, findings, risks, recommendations, codeChanges, questions, nextActions, confidence}. Be direct, specific, actionable."
$claudeArgs += @('--append-system-prompt', $appendSystem)

Write-Host "Calling Claude ($resolvedModel) as $Role..." -ForegroundColor Cyan

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# --- Invoke Claude CLI ---
$rawOutput = $prompt | & $claudeExe @claudeArgs 2>&1

$stopwatch.Stop()
$durationMs = $stopwatch.ElapsedMilliseconds

if ($LASTEXITCODE -ne 0) {
    throw "Claude CLI failed (exit $LASTEXITCODE): $rawOutput"
}

$rawText = ($rawOutput | Out-String).Trim()

# --- Parse response ---
$parsedContent = $null
try {
    $converted = ConvertFrom-ClaudeJsonEnvelopeResult -RawOutput $rawText
    if ($converted.ResultText) {
        $jsonText = $converted.ResultText -replace '(?s)^```(?:json)?\s*', '' -replace '(?s)\s*```$', ''
        $parsedContent = $jsonText | ConvertFrom-Json
    }
}
catch {
    Write-Warning "Could not parse Claude response as JSON. Using text fallback."
    try {
        $parsedContent = ConvertFrom-ModeCTextFallback -RawText $rawText
    }
    catch {
        $parsedContent = @{ summary = $rawText; _parseError = 'Failed to parse' }
    }
}

# --- Build result ---
$result = [ordered]@{
    succeeded  = $true
    role       = $Role
    model      = $resolvedModel
    task       = $Task
    durationMs = $durationMs
    response   = $parsedContent
    rawText    = $rawText
}

# --- Save run ---
if ($SaveRun) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $runDir = Join-Path $projectRoot ".assistant\runs\${timestamp}_claude-${Role}"
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null

    $Task | Set-Content (Join-Path $runDir 'task.txt') -Encoding UTF8
    $prompt | Set-Content (Join-Path $runDir 'prompt.txt') -Encoding UTF8
    $rawText | Set-Content (Join-Path $runDir 'response.raw.txt') -Encoding UTF8
    $result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $runDir 'response.parsed.json') -Encoding UTF8

    $result['runDirectory'] = $runDir
    Write-Host "Run saved: $runDir" -ForegroundColor Green
}

# --- Write to relay ---
$relayDir = Join-Path $projectRoot '.assistant\relay\active'
New-Item -ItemType Directory -Force -Path $relayDir | Out-Null

$relayMessage = [ordered]@{
    messageId  = [Guid]::NewGuid().ToString('N')
    timestamp  = [DateTime]::UtcNow.ToString('o')
    from       = 'claude'
    to         = 'codex'
    role       = $Role
    type       = if ($Role -eq 'reviewer') { 'review' } elseif ($Role -eq 'planner') { 'plan' } else { 'status' }
    sessionId  = "auto-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    taskContext = $Task
    content    = if ($parsedContent) { $parsedContent } else { @{ summary = $rawText } }
    metadata   = @{
        model      = $resolvedModel
        durationMs = $durationMs
        source     = 'invoke_claude_agent.ps1'
    }
}
$relayPath = Join-Path $relayDir "$($relayMessage.messageId).json"
$relayMessage | ConvertTo-Json -Depth 10 | Set-Content $relayPath -Encoding UTF8

# --- Output ---
$result | ConvertTo-Json -Depth 10
