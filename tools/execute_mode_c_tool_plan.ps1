param(
    [string]$ProjectRoot = "",
    [string]$RunDirectory = "",
    [string]$BridgeExe = "",
    [switch]$DryRunAll,
    [switch]$AllowWrite
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Resolve-ProjectRoot
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

function Test-IsReadOnlyTool {
    param([string]$Tool)

    $readPrefixes = @(
        'session.',
        'document.',
        'selection.',
        'type.',
        'review.'
    )

    foreach ($prefix in $readPrefixes) {
        if ($Tool.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    $readExact = @(
        'view.get_active_context',
        'view.get_current_level_context',
        'element.query',
        'element.inspect',
        'annotation.list_text_note_types',
        'annotation.get_text_type_usage'
    )

    return $readExact -contains $Tool
}

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$RunDirectory = Resolve-RunDirectory -ProjectRoot $ProjectRoot -RequestedPath $RunDirectory -NamePrefix 'mode-c-' -RequiredFiles @('response.parsed.json')
$responsePath = Join-Path $RunDirectory 'response.parsed.json'
if (-not (Test-Path $responsePath)) {
    throw "response.parsed.json not found in run: $RunDirectory"
}

$response = Get-Content $responsePath -Raw | ConvertFrom-Json
$contextPath = Join-Path $RunDirectory 'revit-task-context.json'
if (-not (Test-Path $contextPath)) {
    $contextPath = Join-Path $ProjectRoot '.assistant\context\revit-task-context.latest.json'
}
$context = $null
if (Test-Path $contextPath) {
    $context = Get-Content $contextPath -Raw | ConvertFrom-Json
}

$documentKey = if ($context -and $context.ActiveDocument) { $context.ActiveDocument.DocumentKey } else { '' }

$orderedToolPlan = @($response.toolPlan | Sort-Object -Property step, tool)
$results = New-Object System.Collections.Generic.List[object]
foreach ($step in $orderedToolPlan) {
    $tool = [string]$step.tool
    $payloadJson = ''
    if ($step.payload) {
        $payloadJson = $step.payload | ConvertTo-Json -Depth 30
    }

    $readOnly = Test-IsReadOnlyTool -Tool $tool
    $shouldDryRun = $DryRunAll
    $status = 'SKIPPED'
    $bridgeResponse = $null
    $message = ''

    if (-not $readOnly -and -not $AllowWrite) {
        $status = 'SKIPPED_WRITE_BLOCKED'
        $message = 'Write tool bi chan vi chua bat -AllowWrite.'
    }
    else {
        try {
            $bridgeResponse = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $tool -TargetDocument $documentKey -PayloadJson $payloadJson -DryRun:$shouldDryRun
            $status = [string]$bridgeResponse.StatusCode
            if ($bridgeResponse.PSObject.Properties.Name -contains 'Message') {
                $message = [string]$bridgeResponse.Message
            }
            else {
                $message = ''
            }
        }
        catch {
            $status = 'EXECUTION_ERROR'
            $message = $_.Exception.Message
        }
    }

    $results.Add([pscustomobject]@{
        step = $step.step
        tool = $tool
        readOnly = $readOnly
        dryRun = [bool]$shouldDryRun
        purpose = $step.purpose
        status = $status
        message = $message
        payload = if ($payloadJson) { $step.payload } else { $null }
        raw = $bridgeResponse
    }) | Out-Null
}

$out = [pscustomobject]@{
    ExecutedAtUtc = [DateTime]::UtcNow.ToString('o')
    RunDirectory = $RunDirectory
    BridgeExe = $BridgeExe
    TargetDocument = $documentKey
    DryRunAll = [bool]$DryRunAll
    AllowWrite = [bool]$AllowWrite
    Results = $results
}

$outPath = Join-Path $RunDirectory 'bridge-execution.json'
Write-JsonFile -Path $outPath -Data $out

[pscustomobject]@{
    Succeeded = $true
    RunDirectory = $RunDirectory
    OutputPath = $outPath
    ExecutedCount = $results.Count
    SuccessCount = @($results | Where-Object { $_.status -notmatch 'ERROR|SKIPPED' }).Count
} | ConvertTo-Json -Depth 30
