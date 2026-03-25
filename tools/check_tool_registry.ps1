param(
    [string]$BridgeExe = "",
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)

function Invoke-BridgeTool {
    param(
        [Parameter(Mandatory = $true)][string]$Tool
    )

    $raw = & $BridgeExe $Tool 2>$null
    if (-not $raw) {
        throw "Bridge khong tra du lieu cho tool $Tool."
    }

    try {
        return $raw | ConvertFrom-Json
    }
    catch {
        throw "Bridge tra ve non-JSON cho tool $Tool.`nRaw: $raw"
    }
}

function Get-ToolValue {
    param(
        [object]$Tool,
        [string]$PropertyName
    )

    if ($null -eq $Tool) {
        return $null
    }

    if ($Tool.PSObject.Properties[$PropertyName]) {
        return $Tool.$PropertyName
    }

    return $null
}

function Get-SourceToolCount {
    param([string]$ProjectRoot)

    $toolNamesPath = Join-Path $ProjectRoot 'src\BIM765T.Revit.Contracts\Bridge\ToolNames.cs'
    if (-not (Test-Path $toolNamesPath)) {
        return $null
    }

    $content = Get-Content $toolNamesPath -Raw
    return ([regex]::Matches($content, 'public const string\s+\w+\s*=')).Count
}

$toolsResponse = Invoke-BridgeTool -Tool 'session.list_tools'
if (-not $toolsResponse.Succeeded) {
    throw "session.list_tools failed: $($toolsResponse.StatusCode)"
}

$capResponse = Invoke-BridgeTool -Tool 'session.get_capabilities'
$sourceToolCount = Get-SourceToolCount -ProjectRoot $projectRoot
$toolCatalog = if ([string]::IsNullOrWhiteSpace([string]$toolsResponse.PayloadJson)) {
    $null
}
else {
    $toolsResponse.PayloadJson | ConvertFrom-Json
}

$tools = @($toolCatalog.Tools)
$issues = New-Object System.Collections.Generic.List[object]

function Add-Issue {
    param(
        [string]$Severity,
        [string]$Code,
        [string]$Message,
        [string]$ToolName = ""
    )

    $issues.Add([pscustomobject]@{
            Severity = $Severity
            Code     = $Code
            Message  = $Message
            ToolName = $ToolName
        }) | Out-Null
}

if ($tools.Count -eq 0) {
    Add-Issue -Severity 'error' -Code 'EMPTY_CATALOG' -Message 'Bridge returned an empty tool catalog.'
}
else {
    foreach ($tool in $tools) {
        $toolName = [string](Get-ToolValue -Tool $tool -PropertyName 'ToolName')
        $commandFamily = [string](Get-ToolValue -Tool $tool -PropertyName 'CommandFamily')
        $capabilityDomain = [string](Get-ToolValue -Tool $tool -PropertyName 'CapabilityDomain')
        $coverageTier = [string](Get-ToolValue -Tool $tool -PropertyName 'CoverageTier')
        $sourceKind = [string](Get-ToolValue -Tool $tool -PropertyName 'SourceKind')
        $sourceRef = [string](Get-ToolValue -Tool $tool -PropertyName 'SourceRef')
        $nativeCommandId = [string](Get-ToolValue -Tool $tool -PropertyName 'NativeCommandId')
        $executionMode = [string](Get-ToolValue -Tool $tool -PropertyName 'ExecutionMode')
        $safetyClass = [string](Get-ToolValue -Tool $tool -PropertyName 'SafetyClass')

        if ([string]::IsNullOrWhiteSpace($toolName)) {
            Add-Issue -Severity 'error' -Code 'MISSING_TOOL_NAME' -Message 'Tool manifest is missing ToolName.'
            continue
        }

        if ([string]::IsNullOrWhiteSpace($commandFamily)) {
            Add-Issue -Severity 'error' -Code 'MISSING_COMMAND_FAMILY' -Message 'Tool manifest is missing CommandFamily.' -ToolName $toolName
        }

        if ([string]::IsNullOrWhiteSpace($capabilityDomain)) {
            Add-Issue -Severity 'error' -Code 'MISSING_CAPABILITY_DOMAIN' -Message 'Tool manifest is missing CapabilityDomain.' -ToolName $toolName
        }

        if ([string]::IsNullOrWhiteSpace($coverageTier)) {
            Add-Issue -Severity 'error' -Code 'MISSING_COVERAGE_TIER' -Message 'Tool manifest is missing CoverageTier.' -ToolName $toolName
        }

        if ([string]::IsNullOrWhiteSpace($sourceKind)) {
            Add-Issue -Severity 'error' -Code 'MISSING_SOURCE_KIND' -Message 'Tool manifest is missing SourceKind.' -ToolName $toolName
        }

        if ([string]::IsNullOrWhiteSpace($sourceRef)) {
            Add-Issue -Severity 'error' -Code 'MISSING_SOURCE_REF' -Message 'Tool manifest is missing SourceRef.' -ToolName $toolName
        }

        if ([string]::IsNullOrWhiteSpace($safetyClass)) {
            Add-Issue -Severity 'warning' -Code 'MISSING_SAFETY_CLASS' -Message 'Tool manifest is missing SafetyClass.' -ToolName $toolName
        }

        if ([string]::Equals($executionMode, 'native', [StringComparison]::OrdinalIgnoreCase) -and [string]::IsNullOrWhiteSpace($nativeCommandId)) {
            Add-Issue -Severity 'warning' -Code 'MISSING_NATIVE_COMMAND_ID' -Message 'Native execution mode should ideally carry NativeCommandId.' -ToolName $toolName
        }
    }
}

$duplicateSourceRefs = @(
    $tools |
        Group-Object { [string](Get-ToolValue -Tool $_ -PropertyName 'SourceRef') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) -and $_.Count -gt 1 }
)
foreach ($group in $duplicateSourceRefs) {
    Add-Issue -Severity 'warning' -Code 'DUPLICATE_SOURCE_REF' -Message "Multiple tools share SourceRef '$($group.Name)'." -ToolName ($group.Group | Select-Object -First 1 | ForEach-Object { [string](Get-ToolValue -Tool $_ -PropertyName 'ToolName') })
}

$errorCount = @($issues | Where-Object { $_.Severity -eq 'error' }).Count
$warningCount = @($issues | Where-Object { $_.Severity -eq 'warning' }).Count
$coverageTiers = @(
    $tools |
        Group-Object { [string](Get-ToolValue -Tool $_ -PropertyName 'CoverageTier') } |
        Sort-Object Name |
        ForEach-Object { '{0}:{1}' -f $_.Name, $_.Count }
)
$commandFamilies = @(
    $tools |
        Group-Object { [string](Get-ToolValue -Tool $_ -PropertyName 'CommandFamily') } |
        Sort-Object Name |
        ForEach-Object { '{0}:{1}' -f $_.Name, $_.Count }
)
$sourceKinds = @(
    $tools |
        Group-Object { [string](Get-ToolValue -Tool $_ -PropertyName 'SourceKind') } |
        Sort-Object Name |
        ForEach-Object { '{0}:{1}' -f $_.Name, $_.Count }
)

$summary = @{
    BridgeOnline     = [bool]$toolsResponse.Succeeded
    CapabilityStatus  = [string]$capResponse.StatusCode
    TotalTools        = $tools.Count
    SourceToolCount   = $sourceToolCount
    RuntimeLooksStale = ($null -ne $sourceToolCount -and $tools.Count -lt $sourceToolCount)
    StaleReasons     = @(
        if ($null -ne $sourceToolCount -and $tools.Count -lt $sourceToolCount) {
            "RuntimeToolCount($($tools.Count)) < SourceToolCount($sourceToolCount)"
        }
    )
    IssueCount        = $issues.Count
    ErrorCount        = $errorCount
    WarningCount      = $warningCount
    HasErrors         = $errorCount -gt 0
    IssueMessages     = @($issues | ForEach-Object { '{0}:{1}:{2}' -f $_.Severity, $_.Code, $_.ToolName })
    CoverageTiers     = ($coverageTiers -join '|')
    CommandFamilies   = ($commandFamilies -join '|')
    SourceKinds       = ($sourceKinds -join '|')
}

if ($AsJson) {
    $summary | ConvertTo-Json -Depth 12
}
else {
    [pscustomobject]$summary
}

if ($summary.HasErrors) {
    exit 1
}
