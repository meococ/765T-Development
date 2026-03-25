param(
    [string]$BridgeExe = "",
    [ValidateSet('none', 'round', 'phase23', 'copilot')]
    [string]$Profile = 'none',
    [string[]]$RequireTool = @(),
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)

function Invoke-Bridge([string]$tool) {
    $raw = & $BridgeExe $tool 2>$null
    if (-not $raw) { return $null }
    try {
        return $raw | ConvertFrom-Json
    }
    catch {
        throw "Bridge tra ve non-JSON cho tool $tool.`nRaw: $raw"
    }
}

function ConvertFrom-PayloadJson([object]$Response) {
    if ($null -eq $Response -or [string]::IsNullOrWhiteSpace([string]$Response.PayloadJson)) {
        return $null
    }

    try {
        return ($Response.PayloadJson | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

function Get-RequiredTools {
    param(
        [string]$ProfileName,
        [string[]]$AdditionalTools
    )

    $resolved = @()

    switch ($ProfileName) {
        'round' {
            $resolved += @(
                'report.round_externalization_plan',
                'family.build_round_project_wrappers_safe',
                'report.penetration_round_shadow_plan',
                'batch.create_round_shadow_safe'
            )
        }
        'phase23' {
            $resolved += @(
                'review.fix_candidates',
                'workflow.fix_loop_plan',
                'workflow.fix_loop_apply',
                'workflow.fix_loop_verify',
                'family.list_library_roots',
                'family.load_safe',
                'schedule.preview_create',
                'schedule.create_safe',
                'export.list_presets',
                'export.ifc_safe',
                'export.dwg_safe',
                'sheet.print_pdf_safe',
                'storage.validate_output_target'
            )
        }
        'copilot' {
            $resolved += @(
                'session.get_runtime_health',
                'session.get_queue_state',
                'task.plan',
                'task.preview',
                'task.approve_step',
                'task.execute_step',
                'task.resume',
                'task.verify',
                'task.get_run',
                'task.list_runs',
                'task.summarize',
                'task.get_metrics',
                'task.get_residuals',
                'task.promote_memory_safe',
                'context.get_hot_state',
                'context.resolve_bundle',
                'context.search_anchors',
                'artifact.summarize',
                'memory.find_similar_runs',
                'tool.find_by_capability'
            )
        }
    }

    if ($AdditionalTools) {
        $resolved += $AdditionalTools
    }

    return @(
        $resolved |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
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

function Get-SourceToolNames {
    param([string]$ProjectRoot)

    $toolNamesPath = Join-Path $ProjectRoot 'src\BIM765T.Revit.Contracts\Bridge\ToolNames.cs'
    if (-not (Test-Path $toolNamesPath)) {
        return @()
    }

    $content = Get-Content $toolNamesPath -Raw
    $matches = [regex]::Matches($content, 'public const string\s+\w+\s*=\s*"([^"]+)"')
    return @($matches | ForEach-Object { $_.Groups[1].Value } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
}

$toolsResponse = Invoke-Bridge 'session.list_tools'
$capResponse = Invoke-Bridge 'session.get_capabilities'
$docResponse = Invoke-Bridge 'document.get_active'
$viewResponse = Invoke-Bridge 'view.get_active_context'

$toolCatalog = ConvertFrom-PayloadJson -Response $toolsResponse
$capPayload = ConvertFrom-PayloadJson -Response $capResponse
$docPayload = ConvertFrom-PayloadJson -Response $docResponse
$viewPayload = ConvertFrom-PayloadJson -Response $viewResponse

$toolNames = @()
if ($toolCatalog -and $toolCatalog.Tools) {
    $toolNames = @(
        $toolCatalog.Tools |
            ForEach-Object { [string]$_.ToolName } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
}

$requiredTools = @(Get-RequiredTools -ProfileName $Profile -AdditionalTools $RequireTool)
$missingRequiredTools = if ($toolNames.Count -gt 0) {
    @($requiredTools | Where-Object { $toolNames -notcontains $_ })
}
else {
    @($requiredTools)
}

$sourceToolCount = Get-SourceToolCount -ProjectRoot $projectRoot
$sourceToolNames = @(Get-SourceToolNames -ProjectRoot $projectRoot)
$runtimeLooksStale = $false
$staleReasons = @()
$missingSourceTools = @()
$extraRuntimeTools = @()

if ($sourceToolNames.Count -gt 0) {
    $missingSourceTools = @($sourceToolNames | Where-Object { $toolNames -notcontains $_ })
    $extraRuntimeTools = @($toolNames | Where-Object { $sourceToolNames -notcontains $_ })
}

if (($toolsResponse -and $toolsResponse.Succeeded) -and $sourceToolCount -and $toolNames.Count -lt $sourceToolCount) {
    $runtimeLooksStale = $true
    $staleReasons += "RuntimeToolCount($($toolNames.Count)) < SourceToolCount($sourceToolCount)"
}

if ($missingSourceTools.Count -gt 0) {
    $runtimeLooksStale = $true
    $staleReasons += "MissingSourceTools($($missingSourceTools.Count))"
}

if (($toolsResponse -and $toolsResponse.Succeeded) -and @($requiredTools).Count -gt 0 -and @($missingRequiredTools).Count -gt 0) {
    $runtimeLooksStale = $true
    $staleReasons += 'MissingRequiredTools'
}

$result = [ordered]@{
    BridgeExe              = $BridgeExe
    BridgeOnline           = ($toolsResponse -and $toolsResponse.Succeeded)
    ListToolsStatus        = if ($toolsResponse) { $toolsResponse.StatusCode } else { '<no-response>' }
    RuntimeToolCount       = $toolNames.Count
    SourceToolCount        = $sourceToolCount
    MissingSourceTools     = @($missingSourceTools)
    ExtraRuntimeTools      = @($extraRuntimeTools)
    Profile                = $Profile
    RequiredToolCount      = @($requiredTools).Count
    RequiredToolsSatisfied = (@($requiredTools).Count -eq 0 -or @($missingRequiredTools).Count -eq 0)
    MissingRequiredTools   = @($missingRequiredTools)
    RuntimeLooksStale      = $runtimeLooksStale
    StaleReasons           = @($staleReasons)
    CapabilityStatus       = if ($capResponse) { $capResponse.StatusCode } else { '<no-response>' }
    WriteEnabled           = if ($capPayload) { $capPayload.AllowWriteTools } else { $false }
    SaveEnabled            = if ($capPayload) { $capPayload.AllowSaveTools } else { $false }
    SyncEnabled            = if ($capPayload) { $capPayload.AllowSyncTools } else { $false }
    DocumentStatus         = if ($docResponse) { $docResponse.StatusCode } else { '<no-response>' }
    ActiveDocument         = if ($docPayload) { $docPayload.Title } else { '<none>' }
    ViewStatus             = if ($viewResponse) { $viewResponse.StatusCode } else { '<no-response>' }
    ActiveView             = if ($viewPayload) { $viewPayload.ViewName } else { '<none>' }
}

if ($AsJson) {
    [pscustomobject]$result | ConvertTo-Json -Depth 10
}
else {
    [pscustomobject]$result | Format-List
}
