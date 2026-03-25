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
    $RunDirectory = Resolve-RunDirectory -ProjectRoot $ProjectRoot -NamePrefix 'mode-c-' -RequiredFiles @('response.parsed.json', 'bridge-execution.json')
}
else {
    $RunDirectory = Resolve-RunDirectory -ProjectRoot $ProjectRoot -RequestedPath $RunDirectory -RequiredFiles @('response.parsed.json', 'bridge-execution.json')
}

$parsedPath = Join-Path $RunDirectory 'response.parsed.json'
$execPath = Join-Path $RunDirectory 'bridge-execution.json'
if (-not (Test-Path $parsedPath)) { throw "Missing response.parsed.json" }
if (-not (Test-Path $execPath)) { throw "Missing bridge-execution.json" }

$parsed = Get-Content $parsedPath -Raw | ConvertFrom-Json
$exec = Get-Content $execPath -Raw | ConvertFrom-Json

$reportItems = New-Object System.Collections.Generic.List[object]
foreach ($planItem in @($parsed.toolPlan | Sort-Object -Property step, tool)) {
    $execution = @($exec.Results | Where-Object { $_.step -eq $planItem.step -and $_.tool -eq $planItem.tool }) | Select-Object -First 1
    $reportItems.Add([pscustomobject]@{
        step = $planItem.step
        tool = $planItem.tool
        plannedPurpose = $planItem.purpose
        plannedRisk = $planItem.risk
        executed = [bool]($null -ne $execution)
        executionStatus = if ($execution) { $execution.status } else { 'NOT_EXECUTED' }
        executionMessage = if ($execution) { $execution.message } else { '' }
    }) | Out-Null
}

$compare = [pscustomobject]@{
    GeneratedAtUtc = [DateTime]::UtcNow.ToString('o')
    RunDirectory = $RunDirectory
    ClaudeSummary = $parsed.summary
    PlannedSteps = @($parsed.toolPlan).Count
    ExecutedSteps = @($exec.Results).Count
    SuccessfulSteps = @($exec.Results | Where-Object { $_.status -notmatch 'ERROR|SKIPPED' }).Count
    Items = $reportItems
}

$jsonPath = Join-Path $RunDirectory 'mode-c-compare.json'
$mdPath = Join-Path $RunDirectory 'mode-c-compare.md'
Write-JsonFile -Path $jsonPath -Data $compare

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Mode C Compare Report') | Out-Null
$lines.Add('') | Out-Null
$lines.Add(("Generated: {0}" -f [DateTime]::UtcNow.ToString('u'))) | Out-Null
$lines.Add(("RunDirectory: {0}" -f $RunDirectory)) | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Summary') | Out-Null
$lines.Add(($parsed.summary | Out-String).Trim()) | Out-Null
$lines.Add('') | Out-Null
$lines.Add(("PlannedSteps: {0}" -f @($parsed.toolPlan).Count)) | Out-Null
$lines.Add(("ExecutedSteps: {0}" -f @($exec.Results).Count)) | Out-Null
$lines.Add(("SuccessfulSteps: {0}" -f @($exec.Results | Where-Object { $_.status -notmatch 'ERROR|SKIPPED' }).Count)) | Out-Null
$lines.Add('') | Out-Null
$lines.Add('## Step Comparison') | Out-Null
foreach ($item in $reportItems) {
    $lines.Add(("### Step {0} - {1}" -f $item.step, $item.tool)) | Out-Null
    $lines.Add(("- Planned purpose: {0}" -f $item.plannedPurpose)) | Out-Null
    $lines.Add(("- Planned risk: {0}" -f $item.plannedRisk)) | Out-Null
    $lines.Add(("- Executed: {0}" -f $item.executed)) | Out-Null
    $lines.Add(("- Execution status: {0}" -f $item.executionStatus)) | Out-Null
    if ($item.executionMessage) { $lines.Add(("- Execution message: {0}" -f $item.executionMessage)) | Out-Null }
    $lines.Add('') | Out-Null
}
$lines -join "`r`n" | Set-Content -Path $mdPath -Encoding UTF8

[pscustomobject]@{
    Succeeded = $true
    RunDirectory = $RunDirectory
    JsonPath = $jsonPath
    MarkdownPath = $mdPath
} | ConvertTo-Json -Depth 20
