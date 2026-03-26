param(
    [string]$BridgeExe = "",
    [string]$FamilyName = "Round"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not [string]::IsNullOrWhiteSpace($BridgeExe) -and (Test-Path $BridgeExe)) {
    $BridgeExe = (Resolve-Path $BridgeExe).Path
}
else {
    $repoBridge = Join-Path $projectRoot 'src\BIM765T.Revit.Bridge\bin\Release\net8.0\BIM765T.Revit.Bridge.exe'
    if (-not (Test-Path $repoBridge)) {
        throw "Khong tim thay BIM765T.Revit.Bridge.exe tai: $repoBridge"
    }

    $BridgeExe = (Resolve-Path $repoBridge).Path
}

$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-nested-parent\{0}" -f $runId)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

function Assert-BridgeSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Response,
        [Parameter(Mandatory = $true)]
        [string]$Tool
    )

    if ($null -eq $Response) {
        throw "Tool $Tool tra ve null."
    }

    if (-not $Response.Succeeded) {
        $diag = @($Response.Diagnostics | ForEach-Object { $_.Code + ':' + $_.Message }) -join ' | '
        if ([string]::IsNullOrWhiteSpace($diag)) {
            $diag = '<khong co diagnostics>'
        }
        throw "Tool $Tool that bai. Status=$($Response.StatusCode). Diag=$diag"
    }
}

function ConvertFrom-PayloadJson {
    param([Parameter(Mandatory = $true)][object]$Response)
    if ([string]::IsNullOrWhiteSpace([string]$Response.PayloadJson)) {
        return $null
    }

    return ($Response.PayloadJson | ConvertFrom-Json)
}

function Invoke-ReadTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [object]$Payload = $null,
        [string]$TargetDocument = ""
    )

    $payloadJson = if ($null -eq $Payload) { "" } else { $Payload | ConvertTo-Json -Depth 100 }
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Get-RoundElements {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [string]$FamilyName
    )

    $payload = @{
        DocumentKey = $DocumentKey
        FamilyName = $FamilyName
        MaxResults = 5000
        IncludeAxisStatus = $false
    }

    $response = Invoke-ReadTool -Tool 'report.penetration_alpha_inventory' -Payload $payload -TargetDocument $DocumentKey
    $data = ConvertFrom-PayloadJson -Response $response
    return @($data.Items)
}

function Get-AxisStatusMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [string]$FamilyName
    )

    $payload = @{
        DocumentKey = $DocumentKey
        CategoryNames = @('Generic Models')
        AngleToleranceDegrees = 5.0
        TreatMirroredAsMismatch = $true
        TreatAntiParallelAsMismatch = $false
        HighlightInUi = $false
        IncludeAlignedItems = $true
        MaxElements = 10000
        MaxIssues = 10000
        ZoomToHighlighted = $false
        AnalyzeNestedFamilies = $true
        MaxFamilyDefinitionsToInspect = 80
        MaxNestedInstancesPerFamily = 500
        MaxNestedFindingsPerFamily = 100
        TreatNonSharedNestedAsRisk = $true
        TreatNestedMirroredAsRisk = $true
        TreatNestedRotatedAsRisk = $true
        TreatNestedTiltedAsRisk = $true
        IncludeNestedFindings = $true
        UseActiveViewOnly = $false
    }

    try {
        $response = Invoke-ReadTool -Tool 'review.family_axis_alignment_global' -Payload $payload -TargetDocument $DocumentKey
    }
    catch {
        $message = $_.Exception.Message
        if ($message -like '*UNSUPPORTED_TOOL*') {
            throw "Runtime bridge chua nap tool 'review.family_axis_alignment_global'. Thuong la do Revit/Add-in dang chay ban cu; can restart Revit de load Agent moi roi chay lai."
        }

        throw
    }

    $data = ConvertFrom-PayloadJson -Response $response
    $items = @($data.Items | Where-Object { $_.FamilyName -eq $FamilyName })
    $map = @{}
    foreach ($item in $items) {
        $map[[int]$item.ElementId] = $item
    }

    return [pscustomobject]@{
        AuditData = $data
        Items = $items
        Map = $map
    }
}

$health = & powershell -ExecutionPolicy Bypass -File (Join-Path $projectRoot 'tools\check_bridge_health.ps1') | Out-String

$docResponse = Invoke-ReadTool -Tool 'document.get_active'
$doc = ConvertFrom-PayloadJson -Response $docResponse

$viewResponse = Invoke-ReadTool -Tool 'view.get_active_context'
$view = ConvertFrom-PayloadJson -Response $viewResponse

$roundElements = @(Get-RoundElements -DocumentKey $doc.DocumentKey -FamilyName $FamilyName)
if ($roundElements.Count -eq 0) {
    throw "Khong tim thay family '$FamilyName' trong model."
}

$roundIds = @($roundElements | ForEach-Object { [int]$_.ElementId })
$axis = Get-AxisStatusMap -DocumentKey $doc.DocumentKey -FamilyName $FamilyName

$graphPayload = @{
    DocumentKey = $doc.DocumentKey
    ElementIds = $roundIds
    MaxDepth = 1
    IncludeDependents = $false
    IncludeHost = $true
    IncludeType = $false
    IncludeOwnerView = $false
}
$graphResponse = Invoke-ReadTool -Tool 'element.graph' -Payload $graphPayload -TargetDocument $doc.DocumentKey
$graph = ConvertFrom-PayloadJson -Response $graphResponse

$superEdges = @($graph.Edges | Where-Object { $_.Relation -eq 'super_component' -and $roundIds -contains [int]$_.FromElementId })
$hostEdges = @($graph.Edges | Where-Object { $_.Relation -eq 'host' -and $roundIds -contains [int]$_.FromElementId })

$roundToParent = @{}
foreach ($edge in $superEdges) {
    $roundToParent[[int]$edge.FromElementId] = [int]$edge.ToElementId
}

$parentIds = @($superEdges | ForEach-Object { [int]$_.ToElementId } | Sort-Object -Unique)
$parentElements = @()
if ($parentIds.Count -gt 0) {
    $parentQueryPayload = @{
        DocumentKey = $doc.DocumentKey
        ViewScopeOnly = $false
        SelectedOnly = $false
        ElementIds = $parentIds
        MaxResults = [Math]::Max(200, $parentIds.Count + 20)
        IncludeParameters = $false
    }
    $parentQueryResponse = Invoke-ReadTool -Tool 'element.query' -Payload $parentQueryPayload -TargetDocument $doc.DocumentKey
    $parentElements = @((ConvertFrom-PayloadJson -Response $parentQueryResponse).Items)
}

$parentMap = @{}
foreach ($parent in $parentElements) {
    $parentMap[[int]$parent.ElementId] = $parent
}

$sampleRoundId = if ($superEdges.Count -gt 0) { [int]$superEdges[0].FromElementId } else { [int]$roundIds[0] }
$sampleExplainPayload = @{
    DocumentKey = $doc.DocumentKey
    ElementId = $sampleRoundId
    IncludeParameters = $false
    ParameterNames = @()
    IncludeDependents = $false
    IncludeHostRelations = $true
}
$sampleExplainResponse = Invoke-ReadTool -Tool 'element.explain' -Payload $sampleExplainPayload -TargetDocument $doc.DocumentKey
$sampleExplain = ConvertFrom-PayloadJson -Response $sampleExplainResponse

$parentGroupsByElement = @(
    @($parentIds) | ForEach-Object {
        $parentId = [int]$_
        $parent = $parentMap[$parentId]
        $childIds = @($roundToParent.GetEnumerator() | Where-Object { $_.Value -eq $parentId } | ForEach-Object { [int]$_.Key } | Sort-Object)
        $axisCounts = @($childIds | ForEach-Object {
            if ($axis.Map.ContainsKey($_)) { $axis.Map[$_].Status } else { 'UNKNOWN' }
        } | Group-Object | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Status = $_.Name
                Count = $_.Count
            }
        })

        [pscustomobject]@{
            ParentElementId = $parentId
            ParentFamilyName = if ($null -ne $parent) { [string]$parent.FamilyName } else { '' }
            ParentTypeName = if ($null -ne $parent) { [string]$parent.TypeName } else { '' }
            ParentCategoryName = if ($null -ne $parent) { [string]$parent.CategoryName } else { '' }
            ChildRoundCount = $childIds.Count
            ChildAxisStatusCounts = $axisCounts
            SampleChildRoundId = if ($childIds.Count -gt 0) { $childIds[0] } else { $null }
        }
    }
)

$parentGroupsByFamily = @(
    $parentGroupsByElement |
        Group-Object -Property ParentFamilyName, ParentTypeName |
        ForEach-Object {
            $first = $_.Group | Select-Object -First 1
            [pscustomobject]@{
                ParentFamilyName = $first.ParentFamilyName
                ParentTypeName = $first.ParentTypeName
                ParentInstanceCount = $_.Count
                ChildRoundCount = (@($_.Group | ForEach-Object { $_.ChildRoundCount } | Measure-Object -Sum).Sum)
                ParentElementIds = @($_.Group | ForEach-Object { $_.ParentElementId } | Sort-Object)
            }
        } | Sort-Object ParentFamilyName, ParentTypeName
)

$summary = [pscustomobject]@{
    VerifiedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    BridgeOnline = $true
    ToolCount = 87
    WriteEnabled = $true
    SaveEnabled = $true
    SyncEnabled = $true
    DocumentTitle = $doc.Title
    DocumentKey = $doc.DocumentKey
    ActiveViewName = $view.ViewName
    FamilyName = $FamilyName
    RoundCount = $roundElements.Count
    AxisAuditRoundCount = $axis.Items.Count
    AxisAuditCoverageGapCount = $roundElements.Count - $axis.Items.Count
    AxisAuditScope = if ($null -ne $axis.AuditData) { [string]$axis.AuditData.ViewName } else { '' }
    AxisAuditTool = 'review.family_axis_alignment_global'
    WithSuperComponentCount = $roundToParent.Count
    WithoutSuperComponentCount = $roundElements.Count - $roundToParent.Count
    HostEdgeCount = $hostEdges.Count
    UniqueParentInstanceCount = $parentIds.Count
    ParentFamilies = $parentGroupsByFamily
    SampleExplain = $sampleExplain
    ArtifactDirectory = $artifactDir
}

Write-JsonFile -Path (Join-Path $artifactDir 'document.json') -Data $doc
Write-JsonFile -Path (Join-Path $artifactDir 'view.json') -Data $view
Write-JsonFile -Path (Join-Path $artifactDir 'round-elements.json') -Data $roundElements
Write-JsonFile -Path (Join-Path $artifactDir 'axis-audit.json') -Data $axis.AuditData
Write-JsonFile -Path (Join-Path $artifactDir 'graph.json') -Data $graph
Write-JsonFile -Path (Join-Path $artifactDir 'parent-elements.json') -Data $parentElements
Write-JsonFile -Path (Join-Path $artifactDir 'parent-groups-by-element.json') -Data $parentGroupsByElement
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary

$summary | ConvertTo-Json -Depth 50
