param(
    [string]$ProjectRoot = "",
    [string]$RunDirectory = ""
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Resolve-ProjectRoot
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

if (-not $RunDirectory) {
    $RunDirectory = Resolve-RunDirectory -ProjectRoot $ProjectRoot -NamePrefix 'mode-c-' -RequiredFiles @('response.parsed.json')
}
else {
    $RunDirectory = Resolve-RunDirectory -ProjectRoot $ProjectRoot -RequestedPath $RunDirectory -RequiredFiles @('response.parsed.json')
}

$parsedPath = Join-Path $RunDirectory 'response.parsed.json'
if (-not (Test-Path $parsedPath)) {
    throw "Khong tim thay response.parsed.json trong run: $RunDirectory"
}
$parsed = Get-Content $parsedPath -Raw | ConvertFrom-Json

$ctxPath = Join-Path $RunDirectory 'revit-task-context.json'
$ctx = $null
if (Test-Path $ctxPath) {
    $ctx = Get-Content $ctxPath -Raw | ConvertFrom-Json
}

$handoffDir = Join-Path $ProjectRoot 'docs\handoff'
New-Item -ItemType Directory -Force -Path $handoffDir | Out-Null
$handoffPath = Join-Path $handoffDir ("mode-c-handoff-{0}.md" -f ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')))

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Mode C Handoff') | Out-Null
$lines.Add('') | Out-Null
$lines.Add(("Generated: {0}" -f [DateTime]::UtcNow.ToString('u'))) | Out-Null
$lines.Add(("RunDirectory: {0}" -f $RunDirectory)) | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Context') | Out-Null
if ($ctx -and $ctx.ActiveDocument) { $lines.Add('- Document: **' + $ctx.ActiveDocument.Title + '**') | Out-Null }
if ($ctx -and $ctx.ActiveView) { $lines.Add('- View: **' + $ctx.ActiveView.ViewName + '**') | Out-Null }
if ($ctx -and $ctx.LevelContext) { $lines.Add('- Level: **' + $ctx.LevelContext.LevelName + '**') | Out-Null }
$lines.Add('') | Out-Null
$lines.Add('## Summary') | Out-Null
$lines.Add([string]$parsed.summary) | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Findings') | Out-Null
foreach ($finding in @($parsed.findings)) {
    if ($finding.finding) {
        $sev = if ($finding.severity) { $finding.severity } else { 'info' }
        $lines.Add('- **' + $sev + '**: ' + $finding.finding) | Out-Null
    }
    elseif ($finding) {
        $lines.Add('- ' + [string]$finding) | Out-Null
    }
}
$lines.Add('') | Out-Null
$lines.Add('## Tool Plan') | Out-Null
foreach ($item in @($parsed.toolPlan)) {
    $lines.Add(($item.step.ToString() + '. `' + $item.tool + '` - ' + $item.purpose)) | Out-Null
}
$lines.Add('') | Out-Null
$lines.Add('## Risks') | Out-Null
foreach ($risk in @($parsed.risks)) {
    if ($risk.description) {
        $level = if ($risk.level) { $risk.level } else { 'unknown' }
        $lines.Add('- **' + $level + '**: ' + $risk.description) | Out-Null
    }
    else {
        $lines.Add('- ' + [string]$risk) | Out-Null
    }
}
$lines.Add('') | Out-Null
$lines.Add('## Next Actions') | Out-Null
foreach ($action in @($parsed.nextActions)) {
    $lines.Add('- ' + [string]$action) | Out-Null
}

$lines -join "`r`n" | Set-Content -Path $handoffPath -Encoding UTF8

[pscustomobject]@{
    Succeeded = $true
    HandoffPath = $handoffPath
    RunDirectory = $RunDirectory
} | ConvertTo-Json -Depth 20
