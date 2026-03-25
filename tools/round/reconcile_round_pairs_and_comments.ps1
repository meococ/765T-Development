param(
    [string]$BridgeExe = "",
    [string]$ResultsArtifactDir = "",
    [string]$PlanArtifactDir = "",
    [string]$OldFamilyName = "Round",
    [string]$NewFamilyName = "Round__source",
    [string]$NewCommentPrefix = "NEW",
    [string]$NewReviewScheduleName = "BIM765T_Round_NEW_Review",
    [double]$PositionToleranceFeet = 0.01,
    [double]$LengthToleranceFeet = 0.01,
    [double]$ExtentToleranceFeet = 0.01,
    [switch]$SkipCommentUpdate,
    [switch]$SkipScheduleExport
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

        $response = $raw | ConvertFrom-Json
        Assert-BridgeSuccess -Response $response -Tool $Tool
        return $response
    }
    finally {
        Remove-Item $tmpPayload -Force -ErrorAction SilentlyContinue
        if ($tmpExpectedContext) {
            Remove-Item $tmpExpectedContext -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-LatestSuccessfulExternalizationArtifactDir {
    $root = Join-Path $projectRoot 'artifacts\round-externalization-execute'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    foreach ($dir in (Get-ChildItem -Path $root -Directory | Sort-Object Name -Descending)) {
        $summaryPath = Join-Path $dir.FullName 'summary.json'
        if (-not (Test-Path $summaryPath)) {
            continue
        }

        try {
            $summary = Get-Content -Path $summaryPath -Raw | ConvertFrom-Json
            if ([int]$summary.CreatedCount -gt 0 -and [int]$summary.CreatedCount -eq [int]$summary.RequestedCount) {
                return $dir.FullName
            }
        }
        catch {
            continue
        }
    }

    throw "Khong tim thay externalization artifact thanh cong trong $root"
}

function Get-LatestPlanArtifactDir {
    $root = Join-Path $projectRoot 'artifacts\round-externalization-plan-run'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    $dir = Get-ChildItem -Path $root -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $dir) {
        throw "Khong tim thay plan artifact trong $root"
    }

    return $dir.FullName
}

function Get-ParameterValueFromSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Element,
        [Parameter(Mandatory = $true)]
        [string]$ParameterName
    )

    $match = @($Element.Parameters | Where-Object { [string]$_.Name -eq $ParameterName } | Select-Object -First 1)[0]
    if ($null -eq $match) {
        return ""
    }

    return [string]$match.Value
}

function Get-ExistingCommentBase {
    param([string]$Comment)

    if ([string]::IsNullOrWhiteSpace($Comment)) {
        return ""
    }

    return (($Comment -split '\|PAIR#', 2)[0]).Trim()
}

function Get-PointFromElementSummary {
    param([Parameter(Mandatory = $true)][object]$Element)

    if ($null -ne $Element.LocationCurveStart -and $null -ne $Element.LocationCurveEnd) {
        return [pscustomobject]@{
            X = ([double]$Element.LocationCurveStart.X + [double]$Element.LocationCurveEnd.X) / 2.0
            Y = ([double]$Element.LocationCurveStart.Y + [double]$Element.LocationCurveEnd.Y) / 2.0
            Z = ([double]$Element.LocationCurveStart.Z + [double]$Element.LocationCurveEnd.Z) / 2.0
        }
    }

    if ($null -ne $Element.LocationPoint) {
        return [pscustomobject]@{
            X = [double]$Element.LocationPoint.X
            Y = [double]$Element.LocationPoint.Y
            Z = [double]$Element.LocationPoint.Z
        }
    }

    if ($null -ne $Element.BoundingBox) {
        return [pscustomobject]@{
            X = ([double]$Element.BoundingBox.MinX + [double]$Element.BoundingBox.MaxX) / 2.0
            Y = ([double]$Element.BoundingBox.MinY + [double]$Element.BoundingBox.MaxY) / 2.0
            Z = ([double]$Element.BoundingBox.MinZ + [double]$Element.BoundingBox.MaxZ) / 2.0
        }
    }

    return $null
}

function Get-DistanceFeet {
    param(
        [object]$PointA,
        [object]$PointB
    )

    if ($null -eq $PointA -or $null -eq $PointB) {
        return $null
    }

    $dx = [double]$PointA.X - [double]$PointB.X
    $dy = [double]$PointA.Y - [double]$PointB.Y
    $dz = [double]$PointA.Z - [double]$PointB.Z
    return [Math]::Sqrt(($dx * $dx) + ($dy * $dy) + ($dz * $dz))
}

function Get-BoundingBoxExtents {
    param([object]$BoundingBox)

    if ($null -eq $BoundingBox) {
        return @()
    }

    return @(
        [Math]::Abs([double]$BoundingBox.MaxX - [double]$BoundingBox.MinX),
        [Math]::Abs([double]$BoundingBox.MaxY - [double]$BoundingBox.MinY),
        [Math]::Abs([double]$BoundingBox.MaxZ - [double]$BoundingBox.MinZ)
    ) | Sort-Object
}

function Get-MaxExtentDeltaFeet {
    param(
        [object]$BoundingBoxA,
        [object]$BoundingBoxB
    )

    $a = @(Get-BoundingBoxExtents -BoundingBox $BoundingBoxA)
    $b = @(Get-BoundingBoxExtents -BoundingBox $BoundingBoxB)
    if ($a.Count -eq 0 -or $b.Count -eq 0) {
        return $null
    }

    $maxCount = [Math]::Min($a.Count, $b.Count)
    $deltas = for ($i = 0; $i -lt $maxCount; $i++) {
        [Math]::Abs([double]$a[$i] - [double]$b[$i])
    }

    return ($deltas | Measure-Object -Maximum).Maximum
}

function Get-AxisShortCode {
    param([string]$AxisStatus)

    switch -Regex ($AxisStatus) {
        '^ALIGNED$' { return 'ALG' }
        '^ROTATED_IN_VIEW$' { return 'ROT' }
        '^TILTED_OUT_OF_PROJECT_Z$' { return 'TILT' }
        '^MIRRORED$' { return 'MIR' }
        default { return 'UNK' }
    }
}

if ([string]::IsNullOrWhiteSpace($ResultsArtifactDir)) {
    $ResultsArtifactDir = Get-LatestSuccessfulExternalizationArtifactDir
}

if ([string]::IsNullOrWhiteSpace($PlanArtifactDir)) {
    $PlanArtifactDir = Get-LatestPlanArtifactDir
}

$ResultsArtifactDir = (Resolve-Path $ResultsArtifactDir).Path
$PlanArtifactDir = (Resolve-Path $PlanArtifactDir).Path

$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-pair-qc\{0}" -f $runId)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$health = & powershell -ExecutionPolicy Bypass -File (Join-Path $projectRoot 'tools\check_bridge_health.ps1') | Out-String
$docResponse = Invoke-ReadTool -Tool 'document.get_active'
$doc = ConvertFrom-PayloadJson -Response $docResponse

$results = @(Get-Content -Path (Join-Path $ResultsArtifactDir 'results.json') -Raw | ConvertFrom-Json)
if ($results.Count -eq 1 -and $results[0] -is [System.Array]) {
    $results = @($results[0])
}
$plan = Get-Content -Path (Join-Path $PlanArtifactDir 'plan.json') -Raw | ConvertFrom-Json

$resultRows = @($results | Where-Object { $_.Succeeded -and $null -ne $_.CreatedElementId })
if ($resultRows.Count -eq 0) {
    throw "Khong co result externalization thanh cong trong $ResultsArtifactDir"
}

$oldIds = @($resultRows | ForEach-Object { [int]$_.RoundElementId })
$wrapperIds = @($resultRows | ForEach-Object { [int]$_.CreatedElementId })

$planMap = @{}
foreach ($item in @($plan.Items)) {
    $planMap[[int]$item.RoundElementId] = $item
}

$oldInventoryPayload = @{
    DocumentKey = $doc.DocumentKey
    FamilyName = $OldFamilyName
    MaxResults = 10000
    IncludeAxisStatus = $true
}
$oldInventory = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'report.penetration_alpha_inventory' -Payload $oldInventoryPayload -TargetDocument $doc.DocumentKey)
$oldInventoryMap = @{}
foreach ($item in @($oldInventory.Items)) {
    $oldInventoryMap[[int]$item.ElementId] = $item
}

$newInventoryPayload = @{
    DocumentKey = $doc.DocumentKey
    FamilyName = $NewFamilyName
    MaxResults = 10000
    IncludeAxisStatus = $true
}
$newInventory = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'report.penetration_alpha_inventory' -Payload $newInventoryPayload -TargetDocument $doc.DocumentKey)

$newQueryPayload = @{
    DocumentKey = $doc.DocumentKey
    ViewScopeOnly = $false
    SelectedOnly = $false
    ElementIds = @($newInventory.Items | ForEach-Object { [int]$_.ElementId })
    MaxResults = 10000
    IncludeParameters = $true
}
$newQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload $newQueryPayload -TargetDocument $doc.DocumentKey)
$commentedNewItems = @($newQuery.Items | Where-Object {
        $comment = Get-ParameterValueFromSummary -Element $_ -ParameterName 'Comments'
        -not [string]::IsNullOrWhiteSpace($comment) -and $comment.StartsWith($NewCommentPrefix, [System.StringComparison]::OrdinalIgnoreCase)
    })

$newItems = if ($commentedNewItems.Count -eq $resultRows.Count) {
    $commentedNewItems
}
else {
    @($newQuery.Items)
}

if ($newItems.Count -lt $resultRows.Count) {
    throw "So child '$NewFamilyName' nho hon result externalization. ChildCount=$($newItems.Count), ResultCount=$($resultRows.Count)"
}

$newIds = @($newItems | ForEach-Object { [int]$_.ElementId })
$newInventoryMap = @{}
foreach ($item in @($newInventory.Items)) {
    $newInventoryMap[[int]$item.ElementId] = $item
}

$graphPayload = @{
    DocumentKey = $doc.DocumentKey
    ElementIds = $newIds
    MaxDepth = 1
    IncludeDependents = $false
    IncludeHost = $true
    IncludeType = $false
    IncludeOwnerView = $false
}
$graph = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.graph' -Payload $graphPayload -TargetDocument $doc.DocumentKey)
$childToWrapper = @{}
foreach ($edge in @($graph.Edges | Where-Object { $_.Relation -eq 'super_component' })) {
    $childToWrapper[[int]$edge.FromElementId] = [int]$edge.ToElementId
}

$wrapperToChild = @{}
foreach ($childId in $newIds) {
    $wrapperId = $childToWrapper[[int]$childId]
    if ($null -eq $wrapperId -or [int]$wrapperId -le 0) {
        throw "Khong resolve duoc wrapper parent cho child $childId"
    }

    if ($wrapperToChild.ContainsKey([int]$wrapperId)) {
        throw "Wrapper $wrapperId co nhieu hon 1 child $NewFamilyName. Can review."
    }

    $wrapperToChild[[int]$wrapperId] = [int]$childId
}

$oldQueryPayload = @{
    DocumentKey = $doc.DocumentKey
    ViewScopeOnly = $false
    SelectedOnly = $false
    ElementIds = $oldIds
    MaxResults = 10000
    IncludeParameters = $true
}
$oldQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload $oldQueryPayload -TargetDocument $doc.DocumentKey)
$oldQueryMap = @{}
foreach ($item in @($oldQuery.Items)) {
    $oldQueryMap[[int]$item.ElementId] = $item
}

$newQueryMap = @{}
foreach ($item in $newItems) {
    $newQueryMap[[int]$item.ElementId] = $item
}

$pairRows = New-Object System.Collections.Generic.List[object]
$oldCommentChanges = New-Object System.Collections.Generic.List[object]
$newCommentChanges = New-Object System.Collections.Generic.List[object]

for ($index = 0; $index -lt $resultRows.Count; $index++) {
    $result = $resultRows[$index]
    $pairNumber = $index + 1
    $pairTag = ('PAIR#{0:000}' -f $pairNumber)

    $oldId = [int]$result.RoundElementId
    $wrapperId = [int]$result.CreatedElementId
    $childId = $wrapperToChild[[int]$wrapperId]
    if ($null -eq $childId -or [int]$childId -le 0) {
        throw "Khong resolve duoc child $NewFamilyName cho wrapper $wrapperId"
    }

    $oldSummary = $oldQueryMap[[int]$oldId]
    $newSummary = $newQueryMap[[int]$childId]
    if ($null -eq $oldSummary) {
        throw "Khong query duoc Round cu $oldId"
    }
    if ($null -eq $newSummary) {
        throw "Khong query duoc Round moi $childId"
    }

    $planItem = $planMap[[int]$oldId]
    $oldComment = Get-ParameterValueFromSummary -Element $oldSummary -ParameterName 'Comments'
    $newComment = Get-ParameterValueFromSummary -Element $newSummary -ParameterName 'Comments'
    $oldCommentBase = Get-ExistingCommentBase -Comment $oldComment
    if ([string]::IsNullOrWhiteSpace($oldCommentBase)) {
        $oldCommentBase = 'Old'
    }

    $oldPoint = Get-PointFromElementSummary -Element $oldSummary
    $newPoint = Get-PointFromElementSummary -Element $newSummary
    $positionDeltaFeet = Get-DistanceFeet -PointA $oldPoint -PointB $newPoint

    $lengthDeltaFeet = $null
    if ($null -ne $oldSummary.LocationCurveLength -and $null -ne $newSummary.LocationCurveLength) {
        $lengthDeltaFeet = [Math]::Abs([double]$oldSummary.LocationCurveLength - [double]$newSummary.LocationCurveLength)
    }

    $extentDeltaFeet = Get-MaxExtentDeltaFeet -BoundingBoxA $oldSummary.BoundingBox -BoundingBoxB $newSummary.BoundingBox
    $positionStatus = if ($null -ne $positionDeltaFeet -and [double]$positionDeltaFeet -le $PositionToleranceFeet) { 'OK' } else { 'OFF' }

    $lengthOk = $true
    if ($null -ne $lengthDeltaFeet) {
        $lengthOk = [double]$lengthDeltaFeet -le $LengthToleranceFeet
    }

    $extentOk = $true
    if ($null -ne $extentDeltaFeet) {
        $extentOk = [double]$extentDeltaFeet -le $ExtentToleranceFeet
    }

    $sizeStatus = if ($lengthOk -and $extentOk) { 'OK' } else { 'OFF' }

    $oldAxisStatus = if ($oldInventoryMap.ContainsKey($oldId)) { [string]$oldInventoryMap[$oldId].AxisStatus } else { '' }
    $newAxisStatus = if ($newInventoryMap.ContainsKey($childId)) { [string]$newInventoryMap[$childId].AxisStatus } else { '' }
    $newAxisShortCode = Get-AxisShortCode -AxisStatus $newAxisStatus

    $newCommentTarget = "{0}|{1}|OLD={2}|MODE={3}|P={4}|S={5}|AX={6}" -f $NewCommentPrefix, $pairTag, $oldId, [string]$planItem.ProposedPlacementMode, $positionStatus, $sizeStatus, $newAxisShortCode
    $oldCommentTarget = "{0}|{1}" -f $oldCommentBase, $pairTag

    if ($oldComment -ne $oldCommentTarget) {
        $oldCommentChanges.Add([pscustomobject]@{
            ElementId = $oldId
            ParameterName = 'Comments'
            NewValue = $oldCommentTarget
        }) | Out-Null
    }

    if ($newComment -ne $newCommentTarget) {
        $newCommentChanges.Add([pscustomobject]@{
            ElementId = $childId
            ParameterName = 'Comments'
            NewValue = $newCommentTarget
        }) | Out-Null
    }

    $pairRows.Add([pscustomobject]@{
        PairNumber = $pairNumber
        PairTag = $pairTag
        ProposedPlacementMode = [string]$planItem.ProposedPlacementMode
        OldRoundElementId = $oldId
        NewWrapperElementId = $wrapperId
        NewRoundElementId = $childId
        ParentElementId = if ($null -ne $result.ParentElementId) { [int]$result.ParentElementId } else { $null }
        OldAxisStatus = $oldAxisStatus
        NewAxisStatus = $newAxisStatus
        PositionDeltaFeet = $positionDeltaFeet
        LengthDeltaFeet = $lengthDeltaFeet
        ExtentDeltaFeet = $extentDeltaFeet
        PositionStatus = $positionStatus
        SizeStatus = $sizeStatus
        OldTypeName = [string]$oldSummary.TypeName
        NewTypeName = [string]$newSummary.TypeName
        OldLength = [string](Get-ParameterValueFromSummary -Element $oldSummary -ParameterName 'Mii_DimLength')
        NewLength = [string](Get-ParameterValueFromSummary -Element $newSummary -ParameterName 'Mii_DimLength')
        OldDiameter = [string](Get-ParameterValueFromSummary -Element $oldSummary -ParameterName 'Mii_DimDiameter')
        NewDiameter = [string](Get-ParameterValueFromSummary -Element $newSummary -ParameterName 'Mii_DimDiameter')
        OldComment = $oldCommentTarget
        NewComment = $newCommentTarget
    }) | Out-Null
}

$commentExecution = $null
if (-not $SkipCommentUpdate -and ($oldCommentChanges.Count -gt 0 -or $newCommentChanges.Count -gt 0)) {
    $allChanges = @($oldCommentChanges.ToArray()) + @($newCommentChanges.ToArray())
    $setPayload = @{
        DocumentKey = $doc.DocumentKey
        Changes = $allChanges
    }

    $preview = Invoke-MutationPreview -Tool 'parameter.set_safe' -Payload $setPayload -TargetDocument $doc.DocumentKey
    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $commentExecution = Invoke-MutationExecute -Tool 'parameter.set_safe' -Payload $setPayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $doc.DocumentKey
}

$scheduleExport = $null
if (-not $SkipScheduleExport) {
    $scheduleExportPayload = @{
        DocumentKey = $doc.DocumentKey
        ScheduleId = 0
        ScheduleName = $NewReviewScheduleName
        Format = 'json'
    }

    $scheduleExport = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'data.export_schedule' -Payload $scheduleExportPayload -TargetDocument $doc.DocumentKey)
    Write-JsonFile -Path (Join-Path $artifactDir 'new-review-schedule-export.json') -Data $scheduleExport
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    ResultsArtifactDir = $ResultsArtifactDir
    PlanArtifactDir = $PlanArtifactDir
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    PairCount = $pairRows.Count
    PositionToleranceFeet = $PositionToleranceFeet
    LengthToleranceFeet = $LengthToleranceFeet
    ExtentToleranceFeet = $ExtentToleranceFeet
    PositionOkCount = @($pairRows | Where-Object { $_.PositionStatus -eq 'OK' }).Count
    PositionOffCount = @($pairRows | Where-Object { $_.PositionStatus -eq 'OFF' }).Count
    SizeOkCount = @($pairRows | Where-Object { $_.SizeStatus -eq 'OK' }).Count
    SizeOffCount = @($pairRows | Where-Object { $_.SizeStatus -eq 'OFF' }).Count
    FullyMatchedCount = @($pairRows | Where-Object { $_.PositionStatus -eq 'OK' -and $_.SizeStatus -eq 'OK' }).Count
    MaxPositionDeltaFeet = ($pairRows | Where-Object { $null -ne $_.PositionDeltaFeet } | Measure-Object PositionDeltaFeet -Maximum).Maximum
    AvgPositionDeltaFeet = ($pairRows | Where-Object { $null -ne $_.PositionDeltaFeet } | Measure-Object PositionDeltaFeet -Average).Average
    MaxLengthDeltaFeet = ($pairRows | Where-Object { $null -ne $_.LengthDeltaFeet } | Measure-Object LengthDeltaFeet -Maximum).Maximum
    MaxExtentDeltaFeet = ($pairRows | Where-Object { $null -ne $_.ExtentDeltaFeet } | Measure-Object ExtentDeltaFeet -Maximum).Maximum
    NewAxisStatusSummary = @($pairRows | Group-Object NewAxisStatus | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Status = $_.Name
                Count = $_.Count
            }
        })
    OldAxisStatusSummary = @($pairRows | Group-Object OldAxisStatus | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Status = $_.Name
                Count = $_.Count
            }
        })
    CommentUpdateApplied = ($null -ne $commentExecution)
    CommentUpdateChangeCount = $oldCommentChanges.Count + $newCommentChanges.Count
    NewReviewScheduleName = $NewReviewScheduleName
    NewReviewScheduleRowCount = if ($null -ne $scheduleExport) { [int]$scheduleExport.RowCount } else { 0 }
}

Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
Write-JsonFile -Path (Join-Path $artifactDir 'pairs.json') -Data $pairRows
$pairRows | Export-Csv -Path (Join-Path $artifactDir 'pairs.csv') -NoTypeInformation -Encoding UTF8
Set-Content -Path (Join-Path $artifactDir 'bridge-health.txt') -Value $health -Encoding UTF8

$summary | ConvertTo-Json -Depth 50
