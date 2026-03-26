param(
    [string]$BridgeExe = "",
    [string]$ResultsArtifactDir = "",
    [string]$PlanArtifactDir = "",
    [string]$OldFamilyName = "Round",
    [string]$WrapperFamilyFilter = "",
    [string]$WrapperReviewScheduleName = "BIM765T_Round_WRAPPER_Review",
    [string]$DefaultCommentPrefix = "NEW",
    [double]$PositionToleranceFeet = 0.01,
    [double]$LengthToleranceFeet = 0.01,
    [double]$ExtentToleranceFeet = 0.01,
    [double]$AngleToleranceDegrees = 5.0,
    [switch]$SkipCommentUpdate,
    [switch]$SkipAxisAudit,
    [switch]$SkipScheduleCreate,
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
    param(
        [string]$Comment,
        [string]$DefaultBase = 'NEW'
    )

    if ([string]::IsNullOrWhiteSpace($Comment)) {
        return $DefaultBase
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

function Get-PointFromAxisItem {
    param([object]$AxisItem)

    if ($null -eq $AxisItem -or $null -eq $AxisItem.Origin) {
        return $null
    }

    return [pscustomobject]@{
        X = [double]$AxisItem.Origin.X
        Y = [double]$AxisItem.Origin.Y
        Z = [double]$AxisItem.Origin.Z
    }
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

function Get-ComparableLengthFeet {
    param([Parameter(Mandatory = $true)][object]$Element)

    if ($null -ne $Element.LocationCurveLength) {
        return [double]$Element.LocationCurveLength
    }

    $extents = @(Get-BoundingBoxExtents -BoundingBox $Element.BoundingBox)
    if ($extents.Count -eq 0) {
        return $null
    }

    return ($extents | Measure-Object -Maximum).Maximum
}

function Get-AxisShortCode {
    param([string]$AxisStatus)

    switch -Regex ($AxisStatus) {
        '^ALIGNED$' { return 'ALG' }
        '^ROTATED_IN_VIEW$' { return 'ROT' }
        '^TILTED_OUT_OF_PROJECT_Z$' { return 'TILT' }
        '^MIRRORED$' { return 'MIR' }
        '^MISSING$' { return 'MISS' }
        '^SKIPPED' { return 'SKP' }
        '^TIMEOUT' { return 'SKP' }
        '^ERROR' { return 'ERR' }
        default { return 'UNK' }
    }
}

function Get-GeometryAxisFromTypeName {
    param([string]$TypeName)

    if ([string]::IsNullOrWhiteSpace($TypeName)) {
        return ""
    }

    $baseTypeName = (($TypeName -split '__', 2)[0]).Trim()
    switch -Regex ($baseTypeName) {
        '^AXIS_X$' { return 'AXIS_X' }
        '^AXIS_Y$' { return 'AXIS_Y' }
        '^AXIS_Z$' { return 'AXIS_Z' }
        default { return '' }
    }
}

function Resolve-WrapperFamilyName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseName,
        [string]$Suffix = ""
    )

    if ([string]::IsNullOrWhiteSpace($Suffix)) {
        return $BaseName
    }

    if ($BaseName.EndsWith($Suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $BaseName
    }

    return ($BaseName + $Suffix)
}

function Convert-LengthStringToSizeToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $text = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return '0'
    }

    $sign = 1.0
    if ($text.StartsWith('-')) {
        $sign = -1.0
        $text = $text.Substring(1).Trim()
    }

    $feet = 0.0
    $feetMarker = $text.IndexOf("'")
    if ($feetMarker -ge 0) {
        $feetPart = $text.Substring(0, $feetMarker).Trim()
        if (-not [string]::IsNullOrWhiteSpace($feetPart)) {
            $feet = [double]::Parse($feetPart, [System.Globalization.CultureInfo]::InvariantCulture)
        }
        $text = $text.Substring($feetMarker + 1).Trim()
    }

    $text = $text.Replace('"', '').Trim()
    if ($text.StartsWith('-')) {
        $text = $text.Substring(1).Trim()
    }

    $inches = 0.0
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        foreach ($token in ($text -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            if ($token.Contains('/')) {
                $parts = $token.Split('/')
                if ($parts.Length -eq 2) {
                    $inches += ([double]::Parse($parts[0], [System.Globalization.CultureInfo]::InvariantCulture) / [double]::Parse($parts[1], [System.Globalization.CultureInfo]::InvariantCulture))
                }
            }
            else {
                $inches += [double]::Parse($token, [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
    }

    $totalFeet = $sign * ($feet + ($inches / 12.0))
    $tokenValue = [Math]::Round($totalFeet * 12.0 * 256.0, [System.MidpointRounding]::AwayFromZero)
    return ([int]$tokenValue).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Resolve-ExpectedWrapperFamilyName {
    param(
        [Parameter(Mandatory = $true)]
        [object]$PlanItem,
        [string]$WrapperFamilySuffix = "",
        [bool]$UseSizeSpecificVariants = $false
    )

    $familyName = Resolve-WrapperFamilyName -BaseName ([string]$PlanItem.ProposedTargetFamilyName) -Suffix $WrapperFamilySuffix
    return $familyName
}

function Resolve-ExpectedWrapperTypeName {
    param(
        [Parameter(Mandatory = $true)]
        [object]$PlanItem,
        [bool]$UseSizeSpecificVariants = $false
    )

    $typeName = [string]$PlanItem.ProposedTargetTypeName
    if (-not $UseSizeSpecificVariants) {
        return $typeName
    }

    $lengthToken = Convert-LengthStringToSizeToken -Value ([string]$PlanItem.ParentMiiDimLength)
    $diameterToken = Convert-LengthStringToSizeToken -Value ([string]$PlanItem.ParentMiiDiameter)
    return ('{0}__L{1}__D{2}' -f $typeName, $lengthToken, $diameterToken)
}

if ([string]::IsNullOrWhiteSpace($ResultsArtifactDir)) {
    $ResultsArtifactDir = Get-LatestSuccessfulExternalizationArtifactDir
}

if ([string]::IsNullOrWhiteSpace($PlanArtifactDir)) {
    $PlanArtifactDir = Get-LatestPlanArtifactDir
}

$ResultsArtifactDir = (Resolve-Path $ResultsArtifactDir).Path
$PlanArtifactDir = (Resolve-Path $PlanArtifactDir).Path

$resultsSummaryPath = Join-Path $ResultsArtifactDir 'summary.json'
$resultsSummary = if (Test-Path $resultsSummaryPath) {
    Get-Content -Path $resultsSummaryPath -Raw | ConvertFrom-Json
}
else {
    $null
}

$wrapperFamilySuffix = if ($null -ne $resultsSummary -and $resultsSummary.PSObject.Properties.Name -contains 'WrapperFamilySuffix') {
    [string]$resultsSummary.WrapperFamilySuffix
}
else {
    ""
}

$useSizeSpecificVariants = $false
if ($null -ne $resultsSummary -and $resultsSummary.PSObject.Properties.Name -contains 'UseSizeSpecificVariants') {
    $useSizeSpecificVariants = [bool]$resultsSummary.UseSizeSpecificVariants
}

$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-wrapper-qc\{0}" -f $runId)
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

if ([string]::IsNullOrWhiteSpace($WrapperFamilyFilter)) {
    $firstPlanItem = if ($resultRows.Count -gt 0) { $planMap[[int]$resultRows[0].RoundElementId] } else { $null }
    if ($null -ne $firstPlanItem) {
        $WrapperFamilyFilter = Resolve-ExpectedWrapperFamilyName -PlanItem $firstPlanItem -WrapperFamilySuffix $wrapperFamilySuffix -UseSizeSpecificVariants:$useSizeSpecificVariants
    }

    if ([string]::IsNullOrWhiteSpace($WrapperFamilyFilter)) {
        $WrapperFamilyFilter = Resolve-WrapperFamilyName -BaseName 'Round_Project' -Suffix $wrapperFamilySuffix
    }
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

$wrapperQueryPayload = @{
    DocumentKey = $doc.DocumentKey
    ViewScopeOnly = $false
    SelectedOnly = $false
    ElementIds = $wrapperIds
    MaxResults = 10000
    IncludeParameters = $true
}
$wrapperQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload $wrapperQueryPayload -TargetDocument $doc.DocumentKey)
$wrapperQueryMap = @{}
foreach ($item in @($wrapperQuery.Items)) {
    $wrapperQueryMap[[int]$item.ElementId] = $item
}

$wrapperAxisAudit = [pscustomobject]@{
    Items = @()
}
$axisAuditSucceeded = $false
$axisAuditDiagnostic = ''

if (-not $SkipAxisAudit) {
    $axisAuditPayload = @{
        DocumentKey = $doc.DocumentKey
        CategoryNames = @('Generic Models')
        AngleToleranceDegrees = $AngleToleranceDegrees
        TreatMirroredAsMismatch = $true
        TreatAntiParallelAsMismatch = $false
        HighlightInUi = $false
        IncludeAlignedItems = $true
        MaxElements = 10000
        MaxIssues = 10000
        ZoomToHighlighted = $false
        AnalyzeNestedFamilies = $false
        MaxFamilyDefinitionsToInspect = 50
        MaxNestedInstancesPerFamily = 50
        MaxNestedFindingsPerFamily = 10
        TreatNonSharedNestedAsRisk = $true
        TreatNestedMirroredAsRisk = $true
        TreatNestedRotatedAsRisk = $true
        TreatNestedTiltedAsRisk = $true
        IncludeNestedFindings = $false
        UseActiveViewOnly = $false
    }

    try {
        $wrapperAxisAudit = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'review.family_axis_alignment_global' -Payload $axisAuditPayload -TargetDocument $doc.DocumentKey)
        $axisAuditSucceeded = $true
    }
    catch {
        $axisAuditDiagnostic = $_.Exception.Message
        Write-Warning ("Wrapper axis audit bi fallback vi that bai/timeout: {0}" -f $axisAuditDiagnostic)
        $wrapperAxisAudit = [pscustomobject]@{
            Items = @()
        }
    }
}
else {
    $axisAuditDiagnostic = 'Skipped by switch.'
}

$wrapperAxisMap = @{}
foreach ($item in @($wrapperAxisAudit.Items | Where-Object { $wrapperIds -contains [int]$_.ElementId })) {
    $wrapperAxisMap[[int]$item.ElementId] = $item
}

$pairRows = New-Object System.Collections.Generic.List[object]
$wrapperCommentChanges = New-Object System.Collections.Generic.List[object]

for ($index = 0; $index -lt $resultRows.Count; $index++) {
    $result = $resultRows[$index]
    $pairNumber = $index + 1
    $pairTag = ('PAIR#{0:000}' -f $pairNumber)

    $oldId = [int]$result.RoundElementId
    $wrapperId = [int]$result.CreatedElementId

    $planItem = $planMap[[int]$oldId]
    $oldSummary = $oldQueryMap[[int]$oldId]
    $wrapperSummary = $wrapperQueryMap[[int]$wrapperId]
    $wrapperAxis = $wrapperAxisMap[[int]$wrapperId]

    if ($null -eq $planItem) {
        throw "Khong tim thay plan item cho old Round $oldId"
    }
    if ($null -eq $oldSummary) {
        throw "Khong query duoc Round cu $oldId"
    }
    if ($null -eq $wrapperSummary) {
        throw "Khong query duoc wrapper moi $wrapperId"
    }

    $planOrigin = [pscustomobject]@{
        X = [double]$planItem.Origin.X
        Y = [double]$planItem.Origin.Y
        Z = [double]$planItem.Origin.Z
    }

    $wrapperOrigin = Get-PointFromAxisItem -AxisItem $wrapperAxis
    if ($null -eq $wrapperOrigin) {
        $wrapperOrigin = Get-PointFromElementSummary -Element $wrapperSummary
    }

    $positionDeltaFeet = Get-DistanceFeet -PointA $planOrigin -PointB $wrapperOrigin
    $positionStatus = if ($null -ne $positionDeltaFeet -and [double]$positionDeltaFeet -le $PositionToleranceFeet) { 'OK' } else { 'OFF' }

    $expectedFamilyName = Resolve-ExpectedWrapperFamilyName -PlanItem $planItem -WrapperFamilySuffix $wrapperFamilySuffix -UseSizeSpecificVariants:$useSizeSpecificVariants
    $expectedTypeName = Resolve-ExpectedWrapperTypeName -PlanItem $planItem -UseSizeSpecificVariants:$useSizeSpecificVariants
    $familyNameStatus = if ([string]$wrapperSummary.FamilyName -eq $expectedFamilyName) { 'OK' } else { 'OFF' }
    $typeNameStatus = if ([string]$wrapperSummary.TypeName -eq $expectedTypeName) { 'OK' } else { 'OFF' }

    $oldComparableLength = Get-ComparableLengthFeet -Element $oldSummary
    $wrapperComparableLength = Get-ComparableLengthFeet -Element $wrapperSummary
    $lengthDeltaFeet = if ($null -ne $oldComparableLength -and $null -ne $wrapperComparableLength) {
        [Math]::Abs([double]$oldComparableLength - [double]$wrapperComparableLength)
    }
    else {
        $null
    }

    $extentDeltaFeet = Get-MaxExtentDeltaFeet -BoundingBoxA $oldSummary.BoundingBox -BoundingBoxB $wrapperSummary.BoundingBox
    $lengthOk = if ($null -eq $lengthDeltaFeet) { $true } else { [double]$lengthDeltaFeet -le $LengthToleranceFeet }
    $extentOk = if ($null -eq $extentDeltaFeet) { $true } else { [double]$extentDeltaFeet -le $ExtentToleranceFeet }
    $sizeCheckMode = if ($useSizeSpecificVariants) { 'TYPE_NAME' } else { 'BBOX' }
    $sizeStatus = if ($useSizeSpecificVariants) {
        if ($typeNameStatus -eq 'OK') { 'OK' } else { 'OFF' }
    }
    else {
        if ($lengthOk -and $extentOk) { 'OK' } else { 'OFF' }
    }

    $expectedGeometryAxis = Get-GeometryAxisFromTypeName -TypeName $expectedTypeName
    $actualGeometryAxis = Get-GeometryAxisFromTypeName -TypeName ([string]$wrapperSummary.TypeName)
    $geometryAxisStatus = if ([string]::IsNullOrWhiteSpace($actualGeometryAxis)) {
        'MISSING'
    }
    elseif ($actualGeometryAxis -eq $expectedGeometryAxis) {
        'OK'
    }
    else {
        'OFF'
    }

    if ($null -ne $wrapperAxis) {
        $projectAxisStatus = [string]$wrapperAxis.Status
        $projectAxisReason = [string]$wrapperAxis.Reason
    }
    elseif ($axisAuditSucceeded) {
        $projectAxisStatus = 'MISSING'
        $projectAxisReason = 'Khong co wrapper axis audit item.'
    }
    elseif ($SkipAxisAudit) {
        $projectAxisStatus = 'SKIPPED'
        $projectAxisReason = 'Skip axis audit theo request; placement mode fallback tu TypeName.'
    }
    else {
        $projectAxisStatus = 'TIMEOUT_FALLBACK'
        $projectAxisReason = "Axis audit khong hoan tat cho clean family. $axisAuditDiagnostic"
    }

    $projectAxisQcStatus = switch -Regex ($projectAxisStatus) {
        '^ALIGNED$' { 'OK'; break }
        '^SKIPPED$' { 'SKIPPED'; break }
        '^TIMEOUT' { 'SKIPPED'; break }
        '^MISSING$' { 'MISSING'; break }
        default { 'OFF'; break }
    }

    $existingComment = Get-ParameterValueFromSummary -Element $wrapperSummary -ParameterName 'Comments'
    $commentBase = Get-ExistingCommentBase -Comment $existingComment -DefaultBase $DefaultCommentPrefix
    if ([string]::IsNullOrWhiteSpace($commentBase)) {
        $commentBase = $DefaultCommentPrefix
    }

    $commentTarget = "{0}|{1}|OLD={2}|MODE={3}|P={4}|SZ={5}|AX={6}|PS={7}|F={8}|T={9}" -f `
        $commentBase, `
        $pairTag, `
        $oldId, `
        [string]$planItem.ProposedPlacementMode, `
        $positionStatus, `
        $sizeStatus, `
        $geometryAxisStatus, `
        (Get-AxisShortCode -AxisStatus $projectAxisStatus), `
        $familyNameStatus, `
        $typeNameStatus

    if ($existingComment -ne $commentTarget) {
        $wrapperCommentChanges.Add([pscustomobject]@{
            ElementId = $wrapperId
            ParameterName = 'Comments'
            NewValue = $commentTarget
        }) | Out-Null
    }

    $pairRows.Add([pscustomobject]@{
        PairNumber = $pairNumber
        PairTag = $pairTag
        OldRoundElementId = $oldId
        ParentElementId = if ($null -ne $result.ParentElementId) { [int]$result.ParentElementId } else { $null }
        NewWrapperElementId = $wrapperId
        ProposedPlacementMode = [string]$planItem.ProposedPlacementMode
        ExpectedGeometryAxis = $expectedGeometryAxis
        ActualGeometryAxis = $actualGeometryAxis
        GeometryAxisStatus = $geometryAxisStatus
        WrapperProjectAxisStatus = $projectAxisStatus
        WrapperProjectAxisQcStatus = $projectAxisQcStatus
        WrapperProjectAxisReason = $projectAxisReason
        PositionDeltaFeet = $positionDeltaFeet
        PositionStatus = $positionStatus
        LengthDeltaFeet = $lengthDeltaFeet
        ExtentDeltaFeet = $extentDeltaFeet
        SizeCheckMode = $sizeCheckMode
        SizeStatus = $sizeStatus
        ExpectedFamilyName = $expectedFamilyName
        ActualFamilyName = [string]$wrapperSummary.FamilyName
        FamilyNameStatus = $familyNameStatus
        ExpectedTypeName = $expectedTypeName
        ActualTypeName = [string]$wrapperSummary.TypeName
        TypeNameStatus = $typeNameStatus
        WrapperComment = $commentTarget
        OldDiameter = [string](Get-ParameterValueFromSummary -Element $oldSummary -ParameterName 'Mii_DimDiameter')
        OldLength = [string](Get-ParameterValueFromSummary -Element $oldSummary -ParameterName 'Mii_DimLength')
        WrapperLocationPointX = if ($null -ne $wrapperOrigin) { [double]$wrapperOrigin.X } else { $null }
        WrapperLocationPointY = if ($null -ne $wrapperOrigin) { [double]$wrapperOrigin.Y } else { $null }
        WrapperLocationPointZ = if ($null -ne $wrapperOrigin) { [double]$wrapperOrigin.Z } else { $null }
    }) | Out-Null
}

$commentExecution = $null
if (-not $SkipCommentUpdate -and $wrapperCommentChanges.Count -gt 0) {
    $setPayload = @{
        DocumentKey = $doc.DocumentKey
        Changes = @($wrapperCommentChanges.ToArray())
    }

    $preview = Invoke-MutationPreview -Tool 'parameter.set_safe' -Payload $setPayload -TargetDocument $doc.DocumentKey
    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $commentExecution = Invoke-MutationExecute -Tool 'parameter.set_safe' -Payload $setPayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $doc.DocumentKey
}

$scheduleExecution = $null
if (-not $SkipScheduleCreate) {
    $schedulePayload = @{
        DocumentKey = $doc.DocumentKey
        FamilyName = $WrapperFamilyFilter
        ScheduleName = $WrapperReviewScheduleName
        OverwriteIfExists = $true
        Itemized = $true
    }

    $preview = Invoke-MutationPreview -Tool 'schedule.create_penetration_alpha_inventory_safe' -Payload $schedulePayload -TargetDocument $doc.DocumentKey
    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $scheduleExecution = Invoke-MutationExecute -Tool 'schedule.create_penetration_alpha_inventory_safe' -Payload $schedulePayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $doc.DocumentKey
}

$scheduleExport = $null
if (-not $SkipScheduleExport) {
    $scheduleExportPayload = @{
        DocumentKey = $doc.DocumentKey
        ScheduleId = 0
        ScheduleName = $WrapperReviewScheduleName
        Format = 'json'
    }

    $scheduleExport = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'data.export_schedule' -Payload $scheduleExportPayload -TargetDocument $doc.DocumentKey)
    Write-JsonFile -Path (Join-Path $artifactDir 'wrapper-review-schedule-export.json') -Data $scheduleExport
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    ResultsArtifactDir = $ResultsArtifactDir
    PlanArtifactDir = $PlanArtifactDir
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    WrapperFamilySuffix = $wrapperFamilySuffix
    UseSizeSpecificVariants = [bool]$useSizeSpecificVariants
    WrapperFamilyFilter = $WrapperFamilyFilter
    SizeCheckMode = if ($useSizeSpecificVariants) { 'TYPE_NAME' } else { 'BBOX' }
    AxisAuditSucceeded = $axisAuditSucceeded
    AxisAuditDiagnostic = $axisAuditDiagnostic
    PairCount = $pairRows.Count
    PositionToleranceFeet = $PositionToleranceFeet
    LengthToleranceFeet = $LengthToleranceFeet
    ExtentToleranceFeet = $ExtentToleranceFeet
    PositionOkCount = @($pairRows | Where-Object { $_.PositionStatus -eq 'OK' }).Count
    PositionOffCount = @($pairRows | Where-Object { $_.PositionStatus -eq 'OFF' }).Count
    SizeOkCount = @($pairRows | Where-Object { $_.SizeStatus -eq 'OK' }).Count
    SizeOffCount = @($pairRows | Where-Object { $_.SizeStatus -eq 'OFF' }).Count
    GeometryAxisOkCount = @($pairRows | Where-Object { $_.GeometryAxisStatus -eq 'OK' }).Count
    GeometryAxisOffCount = @($pairRows | Where-Object { $_.GeometryAxisStatus -eq 'OFF' }).Count
    GeometryAxisMissingCount = @($pairRows | Where-Object { $_.GeometryAxisStatus -eq 'MISSING' }).Count
    ProjectAxisOkCount = @($pairRows | Where-Object { $_.WrapperProjectAxisQcStatus -eq 'OK' }).Count
    ProjectAxisOffCount = @($pairRows | Where-Object { $_.WrapperProjectAxisQcStatus -eq 'OFF' }).Count
    ProjectAxisMissingCount = @($pairRows | Where-Object { $_.WrapperProjectAxisQcStatus -eq 'MISSING' }).Count
    ProjectAxisSkippedCount = @($pairRows | Where-Object { $_.WrapperProjectAxisQcStatus -eq 'SKIPPED' }).Count
    FamilyNameOkCount = @($pairRows | Where-Object { $_.FamilyNameStatus -eq 'OK' }).Count
    TypeNameOkCount = @($pairRows | Where-Object { $_.TypeNameStatus -eq 'OK' }).Count
    FullyMatchedCount = @($pairRows | Where-Object {
            $_.PositionStatus -eq 'OK' -and
            $_.SizeStatus -eq 'OK' -and
            $_.GeometryAxisStatus -eq 'OK' -and
            $_.WrapperProjectAxisQcStatus -eq 'OK' -and
            $_.FamilyNameStatus -eq 'OK' -and
            $_.TypeNameStatus -eq 'OK'
        }).Count
    MaxPositionDeltaFeet = ($pairRows | Where-Object { $null -ne $_.PositionDeltaFeet } | Measure-Object PositionDeltaFeet -Maximum).Maximum
    AvgPositionDeltaFeet = ($pairRows | Where-Object { $null -ne $_.PositionDeltaFeet } | Measure-Object PositionDeltaFeet -Average).Average
    MaxLengthDeltaFeet = ($pairRows | Where-Object { $null -ne $_.LengthDeltaFeet } | Measure-Object LengthDeltaFeet -Maximum).Maximum
    MaxExtentDeltaFeet = ($pairRows | Where-Object { $null -ne $_.ExtentDeltaFeet } | Measure-Object ExtentDeltaFeet -Maximum).Maximum
    ProposedPlacementModeSummary = @($pairRows | Group-Object ProposedPlacementMode | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Mode = $_.Name
                Count = $_.Count
            }
        })
    GeometryAxisSummary = @($pairRows | Group-Object ActualGeometryAxis | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                GeometryAxis = $_.Name
                Count = $_.Count
            }
        })
    WrapperProjectAxisStatusSummary = @($pairRows | Group-Object WrapperProjectAxisStatus | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Status = $_.Name
                Count = $_.Count
            }
        })
    CommentUpdateApplied = ($null -ne $commentExecution)
    CommentUpdateChangeCount = $wrapperCommentChanges.Count
    ScheduleCreateApplied = ($null -ne $scheduleExecution)
    WrapperReviewScheduleName = $WrapperReviewScheduleName
    WrapperReviewScheduleRowCount = if ($null -ne $scheduleExport) { [int]$scheduleExport.RowCount } else { 0 }
}

Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
Write-JsonFile -Path (Join-Path $artifactDir 'pairs.json') -Data $pairRows
Write-JsonFile -Path (Join-Path $artifactDir 'wrapper-query.json') -Data $wrapperQuery
Write-JsonFile -Path (Join-Path $artifactDir 'wrapper-axis-audit.json') -Data $wrapperAxisAudit
$pairRows | Export-Csv -Path (Join-Path $artifactDir 'pairs.csv') -NoTypeInformation -Encoding UTF8
Set-Content -Path (Join-Path $artifactDir 'bridge-health.txt') -Value $health -Encoding UTF8

$summary | ConvertTo-Json -Depth 50
