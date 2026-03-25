param(
    [string]$BridgeExe = "",
    [string]$OutputDir = ""
)

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path (Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)) 'docs\generated'
}

function Invoke-BridgeTool {
    param(
        [string]$BridgePath,
        [string]$Tool
    )

    $raw = & $BridgePath $Tool
    if (-not $raw) {
        throw "Bridge khong tra du lieu cho tool $Tool"
    }

    $response = $raw | ConvertFrom-Json
    if (-not $response.Succeeded) {
        throw "$Tool failed: $($response.StatusCode)"
    }

    return $response
}

function Get-ToolPrefix {
    param([string]$ToolName)
    if ([string]::IsNullOrWhiteSpace($ToolName)) { return "misc" }
    return ($ToolName -split '\.')[0]
}

function Get-PermissionName {
    param([int]$PermissionLevel)
    switch ($PermissionLevel) {
        0 { return "Read" }
        1 { return "Review" }
        2 { return "Mutate" }
        3 { return "FileLifecycle" }
        default { return "Other" }
    }
}

function Get-ApprovalName {
    param([int]$ApprovalRequirement)
    switch ($ApprovalRequirement) {
        0 { return "None" }
        1 { return "ConfirmToken" }
        2 { return "HighRiskToken" }
        default { return "Unknown" }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

$listResponse = Invoke-BridgeTool -BridgePath $BridgeExe -Tool 'session.list_tools'
$capResponse = Invoke-BridgeTool -BridgePath $BridgeExe -Tool 'session.get_capabilities'

$toolCatalog = $listResponse.PayloadJson | ConvertFrom-Json
$capabilities = $capResponse.PayloadJson | ConvertFrom-Json
$tools = @(
    $toolCatalog.Tools |
        Sort-Object ToolName |
        ForEach-Object {
            [pscustomobject]@{
                ToolName = $_.ToolName
                Prefix = Get-ToolPrefix $_.ToolName
                Description = $_.Description
                PermissionLevel = [int]$_.PermissionLevel
                PermissionName = Get-PermissionName ([int]$_.PermissionLevel)
                ApprovalRequirement = [int]$_.ApprovalRequirement
                ApprovalName = Get-ApprovalName ([int]$_.ApprovalRequirement)
                SupportsDryRun = [bool]$_.SupportsDryRun
                Enabled = [bool]$_.Enabled
                InputSchemaHint = $_.InputSchemaHint
                RiskTier = $_.RiskTier
                CanAutoExecute = [bool]$_.CanAutoExecute
                LatencyClass = $_.LatencyClass
                UiSurface = $_.UiSurface
                ProgressMode = $_.ProgressMode
                RecommendedNextTools = @($_.RecommendedNextTools)
                DomainGroup = $_.DomainGroup
                TaskFamily = $_.TaskFamily
                PackId = $_.PackId
                RecommendedPlaybooks = @($_.RecommendedPlaybooks)
                CapabilityDomain = $_.CapabilityDomain
                DeterminismLevel = $_.DeterminismLevel
                RequiresPolicyPack = [bool]$_.RequiresPolicyPack
                VerificationMode = $_.VerificationMode
                SupportedDisciplines = @($_.SupportedDisciplines)
                IssueKinds = @($_.IssueKinds)
                CommandFamily = $_.CommandFamily
                ExecutionMode = $_.ExecutionMode
                NativeCommandId = $_.NativeCommandId
                SourceKind = $_.SourceKind
                SourceRef = $_.SourceRef
                SafetyClass = $_.SafetyClass
                CanPreview = [bool]$_.CanPreview
                CoverageTier = $_.CoverageTier
            }
        }
)

$groups = @(
    $tools |
        Group-Object Prefix |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name
                Count = $_.Count
                EnabledCount = @($_.Group | Where-Object Enabled).Count
                DryRunCount = @($_.Group | Where-Object SupportsDryRun).Count
                Tools = @($_.Group)
            }
        }
)

$stats = [pscustomobject]@{
    TotalTools = $tools.Count
    EnabledTools = @($tools | Where-Object Enabled).Count
    DisabledTools = @($tools | Where-Object { -not $_.Enabled }).Count
    ReadTools = @($tools | Where-Object PermissionName -eq 'Read').Count
    ReviewTools = @($tools | Where-Object PermissionName -eq 'Review').Count
    MutateTools = @($tools | Where-Object PermissionName -eq 'Mutate').Count
    FileLifecycleTools = @($tools | Where-Object PermissionName -eq 'FileLifecycle').Count
    DryRunTools = @($tools | Where-Object SupportsDryRun).Count
    HighRiskApprovalTools = @($tools | Where-Object ApprovalName -eq 'HighRiskToken').Count
    Tier0Tools = @($tools | Where-Object RiskTier -eq 'tier0_read').Count
    Tier1Tools = @($tools | Where-Object RiskTier -eq 'tier1_mutate_low_risk').Count
    Tier2Tools = @($tools | Where-Object RiskTier -eq 'tier2_destructive').Count
    SourceKindCounts = @(
        $tools |
            Group-Object SourceKind |
            Sort-Object Name |
            ForEach-Object { [pscustomobject]@{ Name = $_.Name; Count = $_.Count } }
    )
    CoverageTierCounts = @(
        $tools |
            Group-Object CoverageTier |
            Sort-Object Name |
            ForEach-Object { [pscustomobject]@{ Name = $_.Name; Count = $_.Count } }
    )
}

$catalog = [pscustomobject]@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    Source = "live-bridge"
    BridgeExe = $BridgeExe
    PlatformName = $capabilities.PlatformName
    RevitYear = $capabilities.RevitYear
    Capabilities = [pscustomobject]@{
        SupportsDryRun = [bool]$capabilities.SupportsDryRun
        SupportsApprovalTokens = [bool]$capabilities.SupportsApprovalTokens
        SupportsBackgroundRead = [bool]$capabilities.SupportsBackgroundRead
        AllowWriteTools = [bool]$capabilities.AllowWriteTools
        AllowSaveTools = [bool]$capabilities.AllowSaveTools
        AllowSyncTools = [bool]$capabilities.AllowSyncTools
        SupportsMcpHost = [bool]$capabilities.SupportsMcpHost
    }
    Stats = $stats
    Groups = $groups
    Tools = $tools
}

$jsonPath = Join-Path $OutputDir "revit-tool-catalog.json"
$mdPath = Join-Path $OutputDir "revit-tool-catalog.md"

$catalog | ConvertTo-Json -Depth 30 | Set-Content -Path $jsonPath -Encoding UTF8

$md = @()
$md += "# 765T Revit Bridge - Tool Catalog"
$md += ""
$md += "- GeneratedAtUtc: $($catalog.GeneratedAtUtc)"
$md += "- Source: $($catalog.Source)"
$md += "- Platform: $($catalog.PlatformName)"
$md += "- RevitYear: $($catalog.RevitYear)"
$md += "- BridgeExe: $($catalog.BridgeExe)"
$md += ""
$md += "## Capability summary"
$md += ""
$md += "| Key | Value |"
$md += "|---|---|"
$md += "| SupportsDryRun | $($catalog.Capabilities.SupportsDryRun) |"
$md += "| SupportsApprovalTokens | $($catalog.Capabilities.SupportsApprovalTokens) |"
$md += "| SupportsBackgroundRead | $($catalog.Capabilities.SupportsBackgroundRead) |"
$md += "| AllowWriteTools | $($catalog.Capabilities.AllowWriteTools) |"
$md += "| AllowSaveTools | $($catalog.Capabilities.AllowSaveTools) |"
$md += "| AllowSyncTools | $($catalog.Capabilities.AllowSyncTools) |"
$md += "| SupportsMcpHost | $($catalog.Capabilities.SupportsMcpHost) |"
$md += ""
$md += "## Tool stats"
$md += ""
$md += "| Metric | Value |"
$md += "|---|---|"
$stats.PSObject.Properties | ForEach-Object {
    $md += "| $($_.Name) | $($_.Value) |"
}
$md += ""
$md += "## Groups"
$md += ""
foreach ($group in $groups) {
    $md += "### $($group.Name) ($($group.Count))"
    $md += ""
    $md += "| Tool | Enabled | Permission | Approval | DryRun | TaskFamily | PackId | CapabilityDomain | ExecutionMode | SourceKind | CoverageTier | InputSchemaHint |"
    $md += "|---|---:|---|---|---:|---|---|---|---|---|---|---|"
    foreach ($tool in $group.Tools) {
        $schemaHint = if ($null -ne $tool.InputSchemaHint) { ([string]$tool.InputSchemaHint).Replace('|', '\\|') } else { '' }
        $taskFamily = if ($null -ne $tool.TaskFamily) { ([string]$tool.TaskFamily).Replace('|', '\\|') } else { '' }
        $packId = if ($null -ne $tool.PackId) { ([string]$tool.PackId).Replace('|', '\\|') } else { '' }
        $capabilityDomain = if ($null -ne $tool.CapabilityDomain) { ([string]$tool.CapabilityDomain).Replace('|', '\\|') } else { '' }
        $executionMode = if ($null -ne $tool.ExecutionMode) { ([string]$tool.ExecutionMode).Replace('|', '\\|') } else { '' }
        $sourceKind = if ($null -ne $tool.SourceKind) { ([string]$tool.SourceKind).Replace('|', '\\|') } else { '' }
        $coverageTier = if ($null -ne $tool.CoverageTier) { ([string]$tool.CoverageTier).Replace('|', '\\|') } else { '' }
        $md += "| $($tool.ToolName) | $($tool.Enabled) | $($tool.PermissionName) | $($tool.ApprovalName) | $($tool.SupportsDryRun) | $taskFamily | $packId | $capabilityDomain | $executionMode | $sourceKind | $coverageTier | $schemaHint |"
        $md += ""
        $md += "  - risk tier: $($tool.RiskTier); auto-execute: $($tool.CanAutoExecute); latency: $($tool.LatencyClass); ui: $($tool.UiSurface); progress: $($tool.ProgressMode); source-ref: $($tool.SourceRef); safety: $($tool.SafetyClass); playbooks: $([string]::Join(', ', $tool.RecommendedPlaybooks))"
    }
    $md += ""
}

$md -join [Environment]::NewLine | Set-Content -Path $mdPath -Encoding UTF8

[pscustomobject]@{
    JsonPath = (Resolve-Path $jsonPath).Path
    MarkdownPath = (Resolve-Path $mdPath).Path
    TotalTools = $stats.TotalTools
    EnabledTools = $stats.EnabledTools
    Groups = $groups.Count
} | ConvertTo-Json -Compress
