param(
    [string]$BridgeExe = "",
    [string]$WrapperQcArtifactDir = "",
    [string]$ViewName = "3D IFC Penetration Export",
    [string]$OutputRootName = "documents_exports",
    [string]$RelativeOutputPathBase = "round-ifc-mitek-spike",
    [string]$FileName = "round-current-contract-full.ifc",
    [switch]$ExecuteExport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Join-Path $PSScriptRoot '..')
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-ifc-mitek-spike\{0}" -f $runId)
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

function Invoke-MutationPreview {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [object]$Payload,
        [string]$TargetDocument = ""
    )

    $payloadJson = $Payload | ConvertTo-Json -Depth 100
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson -DryRun
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-MutationExecute {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [object]$Payload,
        [Parameter(Mandatory = $true)]
        [string]$ApprovalToken,
        [Parameter(Mandatory = $true)]
        [string]$PreviewRunId,
        [object]$ExpectedContext = $null,
        [string]$TargetDocument = ""
    )

    $tmpPayload = Join-Path $env:TEMP ("bridge_exec_payload_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
    $tmpExpectedContext = $null
    try {
        $Payload | ConvertTo-Json -Depth 100 | Set-Content -Path $tmpPayload -Encoding UTF8
        $args = @($Tool, '--dry-run', 'false')
        if (-not [string]::IsNullOrWhiteSpace($TargetDocument)) {
            $args += @('--target-document', $TargetDocument)
        }
        if ($null -ne $ExpectedContext) {
            $tmpExpectedContext = Join-Path $env:TEMP ("bridge_exec_context_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
            $ExpectedContext | ConvertTo-Json -Depth 100 | Set-Content -Path $tmpExpectedContext -Encoding UTF8
            $args += @('--expected-context', $tmpExpectedContext)
        }
        $args += @('--payload', $tmpPayload, '--approval-token', $ApprovalToken, '--preview-run-id', $PreviewRunId)
        $raw = & $BridgeExe @args
        if (-not $raw) {
            throw "Bridge tra ve rong cho tool $Tool khi execute."
        }
        return ($raw | ConvertFrom-Json)
    }
    finally {
        Remove-Item $tmpPayload -Force -ErrorAction SilentlyContinue
        if ($tmpExpectedContext) {
            Remove-Item $tmpExpectedContext -Force -ErrorAction SilentlyContinue
        }
    }
}

function Find-LatestWrapperQcDir {
    param([string]$ProjectRoot)

    $root = Join-Path $ProjectRoot 'artifacts\round-wrapper-qc'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    $dir = Get-ChildItem -Path $root -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'pairs.json') } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $dir) {
        throw "Khong tim thay round-wrapper-qc artifact co pairs.json"
    }

    return $dir.FullName
}

function Import-PairsJson {
    param([string]$Path)

    $rows = Get-Content -Raw -Path $Path | ConvertFrom-Json
    if ($rows.Count -eq 1 -and $rows[0] -is [System.Array]) {
        return @($rows[0])
    }

    return @($rows)
}

function Select-SpikeSamples {
    param([object[]]$Pairs)

    $selected = New-Object System.Collections.Generic.List[object]

    $targets = @(
        @{ SampleKey = 'AXIS_X__PLAN_ZUP'; Axis = 'AXIS_X'; Mode = 'PLAN_ZUP'; Count = 1 }
        @{ SampleKey = 'AXIS_X__ELEV_YUP'; Axis = 'AXIS_X'; Mode = 'ELEV_YUP'; Count = 1 }
        @{ SampleKey = 'AXIS_Y__PLAN_ZUP'; Axis = 'AXIS_Y'; Mode = 'PLAN_ZUP'; Count = 1 }
        @{ SampleKey = 'AXIS_Y__ELEV_YUP'; Axis = 'AXIS_Y'; Mode = 'ELEV_YUP'; Count = 1 }
        @{ SampleKey = 'AXIS_Z__ELEV_XUP'; Axis = 'AXIS_Z'; Mode = 'ELEV_XUP'; Count = 2 }
    )

    foreach ($target in $targets) {
        $candidates = @(
            $Pairs |
                Where-Object {
                    [string]$_.ExpectedGeometryAxis -eq $target.Axis -and
                    [string]$_.ProposedPlacementMode -eq $target.Mode
                } |
                Sort-Object PairNumber
        )

        if ($target.Count -eq 1) {
            $pick = $candidates | Select-Object -First 1
            if ($null -eq $pick) {
                continue
            }

            $selected.Add([pscustomobject]@{
                SampleKey = $target.SampleKey
                PairNumber = [int]$pick.PairNumber
                PairTag = [string]$pick.PairTag
                OldRoundElementId = [int]$pick.OldRoundElementId
                NewWrapperElementId = [int]$pick.NewWrapperElementId
                ProposedPlacementMode = [string]$pick.ProposedPlacementMode
                ExpectedGeometryAxis = [string]$pick.ExpectedGeometryAxis
                ActualGeometryAxis = [string]$pick.ActualGeometryAxis
                ActualTypeName = [string]$pick.ActualTypeName
                OldLength = [string]$pick.OldLength
                OldDiameter = [string]$pick.OldDiameter
                DownstreamExpectation = if ($target.Axis -eq 'AXIS_Z') { 'EXPORT_RISK_VERTICAL' } else { 'EXPECTED_SAFE_HORIZONTAL' }
            }) | Out-Null
            continue
        }

        $usedTypeNames = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($candidate in $candidates) {
            $typeName = [string]$candidate.ActualTypeName
            if ($usedTypeNames.Add($typeName) -or $selected.Count -lt 5) {
                $selected.Add([pscustomobject]@{
                    SampleKey = ('{0}#{1}' -f $target.SampleKey, $usedTypeNames.Count)
                    PairNumber = [int]$candidate.PairNumber
                    PairTag = [string]$candidate.PairTag
                    OldRoundElementId = [int]$candidate.OldRoundElementId
                    NewWrapperElementId = [int]$candidate.NewWrapperElementId
                    ProposedPlacementMode = [string]$candidate.ProposedPlacementMode
                    ExpectedGeometryAxis = [string]$candidate.ExpectedGeometryAxis
                    ActualGeometryAxis = [string]$candidate.ActualGeometryAxis
                    ActualTypeName = [string]$candidate.ActualTypeName
                    OldLength = [string]$candidate.OldLength
                    OldDiameter = [string]$candidate.OldDiameter
                    DownstreamExpectation = 'EXPORT_RISK_VERTICAL'
                }) | Out-Null
            }

            if ($usedTypeNames.Count -ge $target.Count) {
                break
            }
        }
    }

    return @($selected.ToArray())
}

if ([string]::IsNullOrWhiteSpace($WrapperQcArtifactDir)) {
    $WrapperQcArtifactDir = Find-LatestWrapperQcDir -ProjectRoot $projectRoot
}

$pairsPath = Join-Path $WrapperQcArtifactDir 'pairs.json'
if (-not (Test-Path $pairsPath)) {
    throw "Khong tim thay pairs.json tai: $pairsPath"
}

$pairs = Import-PairsJson -Path $pairsPath
$samples = Select-SpikeSamples -Pairs $pairs
if ($samples.Count -lt 6) {
    throw "Khong du 6 sample de lap spike matrix. Tim duoc: $($samples.Count)"
}

$doc = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')
$viewContext = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'view.get_active_context' -TargetDocument $doc.DocumentKey)
$selectedIds = [int[]]@($samples | ForEach-Object { [int]$_.NewWrapperElementId })

$liveQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload @{
    DocumentKey = [string]$doc.DocumentKey
    ViewScopeOnly = $false
    SelectedOnly = $false
    ElementIds = $selectedIds
    CategoryNames = @('Generic Models')
    ClassName = ''
    MaxResults = 50
    IncludeParameters = $true
} -TargetDocument $doc.DocumentKey)

$liveMap = @{}
foreach ($item in @($liveQuery.Items)) {
    $paramMap = @{}
    foreach ($param in @($item.Parameters)) {
        $paramMap[[string]$param.Name] = [string]$param.Value
    }

    $liveMap[[int]$item.ElementId] = [pscustomobject]@{
        ElementId = [int]$item.ElementId
        FamilyName = [string]$item.FamilyName
        TypeName = [string]$item.TypeName
        LevelName = [string]$item.LevelName
        Comments = if ($paramMap.ContainsKey('Comments')) { [string]$paramMap['Comments'] } else { '' }
        Mark = if ($paramMap.ContainsKey('Mark')) { [string]$paramMap['Mark'] } else { '' }
    }
}

$sampleRows = foreach ($sample in $samples) {
    $live = $null
    if ($liveMap.ContainsKey([int]$sample.NewWrapperElementId)) {
        $live = $liveMap[[int]$sample.NewWrapperElementId]
    }

    [pscustomobject]@{
        SampleKey = [string]$sample.SampleKey
        PairNumber = [int]$sample.PairNumber
        PairTag = [string]$sample.PairTag
        OldRoundElementId = [int]$sample.OldRoundElementId
        NewWrapperElementId = [int]$sample.NewWrapperElementId
        ProposedPlacementMode = [string]$sample.ProposedPlacementMode
        ExpectedGeometryAxis = [string]$sample.ExpectedGeometryAxis
        ActualGeometryAxis = [string]$sample.ActualGeometryAxis
        ActualTypeName = [string]$sample.ActualTypeName
        OldLength = [string]$sample.OldLength
        OldDiameter = [string]$sample.OldDiameter
        LiveFamilyName = if ($live) { [string]$live.FamilyName } else { '' }
        LiveTypeName = if ($live) { [string]$live.TypeName } else { '' }
        LiveLevelName = if ($live) { [string]$live.LevelName } else { '' }
        LiveComments = if ($live) { [string]$live.Comments } else { '' }
        DownstreamExpectation = [string]$sample.DownstreamExpectation
        MitekConvertResult = ''
        ManualPoseNeeded = ''
        Notes = ''
    }
}

$csvLines = @(
    'SampleKey,PairNumber,PairTag,OldRoundElementId,NewWrapperElementId,ProposedPlacementMode,ExpectedGeometryAxis,ActualGeometryAxis,ActualTypeName,OldLength,OldDiameter,LiveFamilyName,LiveTypeName,LiveLevelName,DownstreamExpectation,MitekConvertResult,ManualPoseNeeded,Notes'
)
foreach ($row in $sampleRows) {
    $fields = @(
        $row.SampleKey,
        $row.PairNumber,
        $row.PairTag,
        $row.OldRoundElementId,
        $row.NewWrapperElementId,
        $row.ProposedPlacementMode,
        $row.ExpectedGeometryAxis,
        $row.ActualGeometryAxis,
        $row.ActualTypeName,
        $row.OldLength,
        $row.OldDiameter,
        $row.LiveFamilyName,
        $row.LiveTypeName,
        $row.LiveLevelName,
        $row.DownstreamExpectation,
        $row.MitekConvertResult,
        $row.ManualPoseNeeded,
        $row.Notes
    ) | ForEach-Object {
        '"' + ([string]$_).Replace('"', '""') + '"'
    }

    $csvLines += ($fields -join ',')
}

Set-Content -Path (Join-Path $artifactDir 'spike-samples.csv') -Value $csvLines -Encoding UTF8
Write-JsonFile -Path (Join-Path $artifactDir 'spike-samples.json') -Data $sampleRows
Write-JsonFile -Path (Join-Path $artifactDir 'live-query.json') -Data $liveQuery
Write-JsonFile -Path (Join-Path $artifactDir 'active-view-context.json') -Data $viewContext

$exportPayload = [ordered]@{
    DocumentKey = [string]$doc.DocumentKey
    PresetName = 'coordination_ifc'
    OutputRootName = $OutputRootName
    RelativeOutputPath = ("{0}\{1}" -f $RelativeOutputPathBase, $runId)
    FileName = $FileName
    ViewId = $null
    ViewName = $ViewName
    OverwriteExisting = $true
}

$exportPreview = Invoke-MutationPreview -Tool 'export.ifc_safe' -Payload $exportPayload -TargetDocument $doc.DocumentKey
$exportPreviewPayload = ConvertFrom-PayloadJson -Response $exportPreview
Write-JsonFile -Path (Join-Path $artifactDir 'ifc-export-preview.response.json') -Data $exportPreview
Write-JsonFile -Path (Join-Path $artifactDir 'ifc-export-preview.payload.json') -Data $exportPreviewPayload

$exportExecute = $null
if ($ExecuteExport.IsPresent) {
    $exportExecute = Invoke-MutationExecute `
        -Tool 'export.ifc_safe' `
        -Payload $exportPayload `
        -ApprovalToken $exportPreview.ApprovalToken `
        -PreviewRunId $exportPreview.PreviewRunId `
        -ExpectedContext $exportPreviewPayload.ResolvedContext `
        -TargetDocument $doc.DocumentKey

    Write-JsonFile -Path (Join-Path $artifactDir 'ifc-export-execute.response.json') -Data $exportExecute
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    WrapperQcArtifactDir = $WrapperQcArtifactDir
    ActiveViewName = [string]$viewContext.ViewName
    RequestedIfcViewName = $ViewName
    SampleCount = @($sampleRows).Count
    SampleKeys = @($sampleRows | ForEach-Object { [string]$_.SampleKey })
    ExportPreviewStatus = [string]$exportPreview.StatusCode
    ExportExecuteStatus = if ($exportExecute) { [string]$exportExecute.StatusCode } else { 'SKIPPED' }
    ExportExecuteSucceeded = if ($exportExecute) { [bool]$exportExecute.Succeeded } else { $false }
    ExportArtifacts = if ($exportExecute) { @($exportExecute.Artifacts) } else { @($exportPreview.Artifacts) }
}

Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
$summary | ConvertTo-Json -Depth 100
