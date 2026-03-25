param(
    [string]$BridgeExe = "",
    [string]$TargetDocument = "",
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifactDir = Join-Path $projectRoot ("artifacts/copilot-runtime-live/{0}" -f $stamp)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

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

function Invoke-ReadTool {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [object]$Payload = $null,
        [string]$Doc = ""
    )

    $payloadJson = if ($null -eq $Payload) { '' } else { ($Payload | ConvertTo-Json -Depth 100 -Compress) }
    return Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $Doc -PayloadJson $payloadJson
}

function Save-Response {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][object]$Response
    )

    $path = Join-Path $artifactDir ($Name + '.json')
    Write-JsonFileAtomically -Path $path -Data $Response
    return [pscustomobject]@{
        Path = $path
        Response = $Response
        Payload = (ConvertFrom-PayloadJson -Response $Response)
    }
}

function Get-OptionalValue {
    param(
        [object]$Object,
        [string]$PropertyName,
        [object]$DefaultValue = $null
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }

    if ($Object.PSObject.Properties[$PropertyName]) {
        return $Object.$PropertyName
    }

    return $DefaultValue
}

$activeDoc = Save-Response -Name 'document-active' -Response (Invoke-ReadTool -Tool 'document.get_active')
$resolvedDocumentKey = if ($null -ne $activeDoc.Payload -and $activeDoc.Payload.PSObject.Properties['DocumentKey']) {
    [string]$activeDoc.Payload.DocumentKey
}
else {
    ''
}
if ([string]::IsNullOrWhiteSpace($TargetDocument)) {
    if ($null -ne $activeDoc.Payload -and $activeDoc.Payload.PSObject.Properties['Title']) {
        $TargetDocument = [string]$activeDoc.Payload.Title
    }
    else {
        throw 'Khong resolve duoc active document title tu document.get_active payload.'
    }
}

$runtimeHealth = Save-Response -Name 'session-runtime-health' -Response (Invoke-ReadTool -Tool 'session.get_runtime_health' -Doc $TargetDocument)
$queueState = Save-Response -Name 'session-queue-state' -Response (Invoke-ReadTool -Tool 'session.get_queue_state' -Doc $TargetDocument)
$hotState = Save-Response -Name 'context-hot-state' -Response (Invoke-ReadTool -Tool 'context.get_hot_state' -Doc $TargetDocument -Payload @{
    DocumentKey = $(if ([string]::IsNullOrWhiteSpace($resolvedDocumentKey)) { $TargetDocument } else { $resolvedDocumentKey })
    MaxRecentOperations = 10
    MaxRecentEvents = 10
    MaxPendingTasks = 10
    IncludeGraph = $true
    IncludeToolCatalog = $false
})
$toolLookup = Save-Response -Name 'tool-find-by-capability' -Response (Invoke-ReadTool -Tool 'tool.find_by_capability' -Doc $TargetDocument -Payload @{
    Query = 'task context verification bundle memory'
    RiskTags = @()
    RequiredContext = @('document','view')
    MaxResults = 8
})

$fixLoopInput = @{
    DocumentKey = $(if ([string]::IsNullOrWhiteSpace($resolvedDocumentKey)) { $TargetDocument } else { $resolvedDocumentKey })
    ScenarioName = 'parameter_hygiene'
    PlaybookName = 'default.fix_loop_v1'
    ElementIds = @()
    CategoryNames = @()
    RequiredParameterNames = @('Comments')
    UseCurrentSelectionWhenEmpty = $false
    ViewId = $null
    SheetId = $null
    MaxIssues = 5
    MaxActions = 5
    ImportFilePath = ''
    MatchParameterName = ''
}

$taskPlan = Save-Response -Name 'task-plan' -Response (Invoke-ReadTool -Tool 'task.plan' -Doc $TargetDocument -Payload @{
    DocumentKey = $(if ([string]::IsNullOrWhiteSpace($resolvedDocumentKey)) { $TargetDocument } else { $resolvedDocumentKey })
    TaskKind = 'fix_loop'
    TaskName = 'parameter_hygiene'
    IntentSummary = 'Copilot runtime smoke: durable fix-loop planning for Comments hygiene.'
    InputJson = ($fixLoopInput | ConvertTo-Json -Depth 50 -Compress)
    Tags = @('copilot-smoke','phase0','parameter-hygiene')
}) 

$runId = [string]$taskPlan.Payload.RunId
$resolvedDocumentKey = if ($null -ne $taskPlan.Payload -and $taskPlan.Payload.PSObject.Properties['DocumentKey']) {
    [string]$taskPlan.Payload.DocumentKey
}
elseif (-not [string]::IsNullOrWhiteSpace($resolvedDocumentKey)) {
    $resolvedDocumentKey
}
else {
    $TargetDocument
}
$taskGet = Save-Response -Name 'task-get-run' -Response (Invoke-ReadTool -Tool 'task.get_run' -Doc $TargetDocument -Payload @{ RunId = $runId })
$taskSummary = Save-Response -Name 'task-summarize' -Response (Invoke-ReadTool -Tool 'task.summarize' -Doc $TargetDocument -Payload @{ RunId = $runId })
$taskMetrics = Save-Response -Name 'task-get-metrics' -Response (Invoke-ReadTool -Tool 'task.get_metrics' -Doc $TargetDocument -Payload @{
    TaskKind = 'fix_loop'
    TaskName = 'parameter_hygiene'
    DocumentKey = $resolvedDocumentKey
    MaxResults = 20
})
$similarRuns = Save-Response -Name 'memory-find-similar-runs' -Response (Invoke-ReadTool -Tool 'memory.find_similar_runs' -Doc $TargetDocument -Payload @{
    RunId = $runId
    TaskKind = 'fix_loop'
    TaskName = 'parameter_hygiene'
    DocumentKey = $resolvedDocumentKey
    Query = 'comments hygiene'
    MaxResults = 10
})
$contextBundle = Save-Response -Name 'context-resolve-bundle' -Response (Invoke-ReadTool -Tool 'context.resolve_bundle' -Doc $TargetDocument -Payload @{
    RunId = $runId
    Query = 'parameter hygiene comments'
    Tags = @('fix_loop','parameter_hygiene')
    MaxAnchors = 8
    IncludeHot = $true
    IncludeWarm = $true
    IncludeCold = $false
})
$anchorSearch = Save-Response -Name 'context-search-anchors' -Response (Invoke-ReadTool -Tool 'context.search_anchors' -Doc $TargetDocument -Payload @{
    Query = 'parameter hygiene comments'
    Tags = @('fix_loop','parameter_hygiene')
    MaxResults = 10
})
$artifactSummary = Save-Response -Name 'artifact-summarize' -Response (Invoke-ReadTool -Tool 'artifact.summarize' -Doc $TargetDocument -Payload @{
    ArtifactPath = $taskGet.Path
    MaxChars = 1200
    MaxLines = 30
})
$listRuns = Save-Response -Name 'task-list-runs' -Response (Invoke-ReadTool -Tool 'task.list_runs' -Doc $TargetDocument -Payload @{
    TaskKind = 'fix_loop'
    TaskName = 'parameter_hygiene'
    DocumentKey = $resolvedDocumentKey
    MaxResults = 10
})

$summary = [ordered]@{
    GeneratedAtUtc = [DateTime]::UtcNow.ToString('o')
    ArtifactDirectory = $artifactDir
    TargetDocument = $TargetDocument
    ResolvedDocumentKey = $resolvedDocumentKey
    RuntimeHealth = [ordered]@{
        StatusCode = $runtimeHealth.Response.StatusCode
        SupportsTaskRuntime = $runtimeHealth.Payload.SupportsTaskRuntime
        SupportsContextBroker = $runtimeHealth.Payload.SupportsContextBroker
        SupportsStateGraph = $runtimeHealth.Payload.SupportsStateGraph
        SupportsDurableTaskRuns = $runtimeHealth.Payload.SupportsDurableTaskRuns
        SupportsCheckpointRecovery = (Get-OptionalValue -Object $runtimeHealth.Payload -PropertyName 'SupportsCheckpointRecovery' -DefaultValue $false)
        DurableRunCount = $runtimeHealth.Payload.DurableRunCount
        PromotionCount = $runtimeHealth.Payload.PromotionCount
        QueuePending = $runtimeHealth.Payload.Queue.PendingCount
        ActiveToolName = $runtimeHealth.Payload.Queue.ActiveToolName
        ToolCount = $runtimeHealth.Payload.ToolCount
    }
    HotState = [ordered]@{
        StatusCode = $hotState.Response.StatusCode
        DocumentKey = (Get-OptionalValue -Object (Get-OptionalValue -Object $hotState.Payload -PropertyName 'Graph') -PropertyName 'DocumentKey' -DefaultValue $resolvedDocumentKey)
        ActiveView = (Get-OptionalValue -Object (Get-OptionalValue -Object $hotState.Payload -PropertyName 'TaskContext') -PropertyName 'ActiveContext' | ForEach-Object { Get-OptionalValue -Object $_ -PropertyName 'ViewName' -DefaultValue '' })
        SelectionCount = (Get-OptionalValue -Object (Get-OptionalValue -Object $hotState.Payload -PropertyName 'TaskContext') -PropertyName 'Selection' | ForEach-Object { Get-OptionalValue -Object $_ -PropertyName 'Count' -DefaultValue 0 })
        GraphNodeCount = @((Get-OptionalValue -Object (Get-OptionalValue -Object $hotState.Payload -PropertyName 'Graph') -PropertyName 'Nodes' -DefaultValue @())).Count
        GraphEdgeCount = @((Get-OptionalValue -Object (Get-OptionalValue -Object $hotState.Payload -PropertyName 'Graph') -PropertyName 'Edges' -DefaultValue @())).Count
        PendingTaskCount = @((Get-OptionalValue -Object $hotState.Payload -PropertyName 'PendingTasks' -DefaultValue @())).Count
        Error = @($hotState.Response.Diagnostics | ForEach-Object { $_.Message }) -join '; '
    }
    TaskPlan = [ordered]@{
        StatusCode = $taskPlan.Response.StatusCode
        RunId = $runId
        TaskKind = $taskPlan.Payload.TaskKind
        TaskName = $taskPlan.Payload.TaskName
        Status = $taskPlan.Payload.Status
        RecommendedActionCount = @($taskPlan.Payload.RecommendedActionIds).Count
        ExpectedDelta = $taskPlan.Payload.ExpectedDelta
    }
    TaskSummary = [ordered]@{
        Status = $taskSummary.Payload.Status
        NextAction = $taskSummary.Payload.NextAction
        ChangedCount = $taskSummary.Payload.ChangedCount
        ResidualCount = $taskSummary.Payload.ResidualCount
        CheckpointCount = (Get-OptionalValue -Object $taskSummary.Payload -PropertyName 'CheckpointCount' -DefaultValue 0)
        RecoveryBranchCount = (Get-OptionalValue -Object $taskSummary.Payload -PropertyName 'RecoveryBranchCount' -DefaultValue 0)
        CanResume = (Get-OptionalValue -Object $taskSummary.Payload -PropertyName 'CanResume' -DefaultValue $false)
        LastErrorCode = (Get-OptionalValue -Object $taskSummary.Payload -PropertyName 'LastErrorCode' -DefaultValue '')
    }
    CapabilityLookup = [ordered]@{
        Query = $toolLookup.Payload.Query
        MatchCount = @($toolLookup.Payload.Matches).Count
        TopMatches = @($toolLookup.Payload.Matches | Select-Object -First 5 | ForEach-Object { $_.Manifest.ToolName })
    }
    SimilarRuns = [ordered]@{
        Count = @($similarRuns.Payload.Runs).Count
    }
    ContextBundle = [ordered]@{
        ItemCount = @($contextBundle.Payload.Items).Count
        Tiers = @($contextBundle.Payload.Items | Group-Object Tier | ForEach-Object { "{0}:{1}" -f $_.Name, $_.Count })
    }
    AnchorSearch = [ordered]@{
        ItemCount = @($anchorSearch.Payload.Items).Count
    }
    Metrics = [ordered]@{
        RowCount = @($taskMetrics.Payload.Metrics).Count
    }
    ListRuns = [ordered]@{
        Count = @($listRuns.Payload.Runs).Count
    }
    DurableRun = [ordered]@{
        RecoveryBranchCount = @((Get-OptionalValue -Object $taskGet.Payload -PropertyName 'RecoveryBranches' -DefaultValue @())).Count
        CheckpointCount = @((Get-OptionalValue -Object $taskGet.Payload -PropertyName 'Checkpoints' -DefaultValue @())).Count
        LastErrorCode = (Get-OptionalValue -Object $taskGet.Payload -PropertyName 'LastErrorCode' -DefaultValue '')
        TopRecoveryBranches = @((Get-OptionalValue -Object $taskGet.Payload -PropertyName 'RecoveryBranches' -DefaultValue @()) | Select-Object -First 5 | ForEach-Object { $_.BranchId })
    }
    ArtifactSummary = [ordered]@{
        DetectedFormat = $artifactSummary.Payload.DetectedFormat
        Exists = $artifactSummary.Payload.Exists
        Summary = $artifactSummary.Payload.Summary
    }
}

$summaryPath = Join-Path $artifactDir 'summary.json'
Write-JsonFileAtomically -Path $summaryPath -Data $summary

if ($AsJson) {
    $summary | ConvertTo-Json -Depth 50
}
else {
    [pscustomobject]$summary | Format-List
}
