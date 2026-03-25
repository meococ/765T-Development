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
$doc = ''
$view = ''
if (Test-Path $ctxPath) {
    $ctx = Get-Content $ctxPath -Raw | ConvertFrom-Json
    if ($ctx.ActiveDocument) { $doc = $ctx.ActiveDocument.Title }
    if ($ctx.ActiveView) { $view = $ctx.ActiveView.ViewName }
}

$memDir = Join-Path $ProjectRoot '.assistant\memory'
New-Item -ItemType Directory -Force -Path $memDir | Out-Null
$projectMemory = Join-Path $memDir 'project-memory.md'
$sessionMemory = Join-Path $memDir 'session-memory.latest.md'

$entry = @()
$entry += "## $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
if ($doc) { $entry += "- Document: **$doc**" }
if ($view) { $entry += "- View: **$view**" }
$entry += "- Run: $(Split-Path $RunDirectory -Leaf)"
$entry += "- Summary: $($parsed.summary)"
$entry += '- Next Actions:'
foreach ($a in @($parsed.nextActions)) { $entry += "  - $a" }
$entry += ''

Add-Content -Path $projectMemory -Value ($entry -join "`r`n") -Encoding UTF8
Set-Content -Path $sessionMemory -Value ($entry -join "`r`n") -Encoding UTF8

[pscustomobject]@{
    Succeeded = $true
    ProjectMemory = $projectMemory
    SessionMemory = $sessionMemory
    RunDirectory = $RunDirectory
} | ConvertTo-Json -Depth 20
