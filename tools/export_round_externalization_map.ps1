param(
    [string]$BridgeExe = "",
    [string]$FamilyName = "Round",
    [string]$SourceArtifactDir = ""
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
$artifactDir = Join-Path $projectRoot ("artifacts\round-externalization-map\{0}" -f $runId)
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

function Get-PlacementMode {
    param(
        [Parameter(Mandatory = $true)]
        [object]$AuditItem
    )

    $absX = [Math]::Abs([double]$AuditItem.BasisX.Z)
    $absY = [Math]::Abs([double]$AuditItem.BasisY.Z)
    $absZ = [Math]::Abs([double]$AuditItem.BasisZ.Z)

    if ($absZ -ge $absX -and $absZ -ge $absY) {
        return 'PLAN_ZUP'
    }
    if ($absX -ge $absY) {
        return 'ELEV_XUP'
    }
    return 'ELEV_YUP'
}

function Get-PlacementNote {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PlacementMode
    )

    switch ($PlacementMode) {
        'PLAN_ZUP' { return 'Dat family Round ngoai project theo mat bang; local Z song song project Z.' }
        'ELEV_XUP' { return 'Dat family Round ngoai project theo mat dung; truc local X dang la truc dung.' }
        'ELEV_YUP' { return 'Dat family Round ngoai project theo mat dung; truc local Y dang la truc dung.' }
        default { return 'Can review tay vi khong classify duoc mode dat.' }
    }
}

if ([string]::IsNullOrWhiteSpace($SourceArtifactDir)) {
    $nestedAuditRaw = powershell -ExecutionPolicy Bypass -File (Join-Path $projectRoot 'tools\check_round_nested_parent.ps1') -BridgeExe $BridgeExe -FamilyName $FamilyName
    $nestedAudit = $nestedAuditRaw | ConvertFrom-Json
    $SourceArtifactDir = [string]$nestedAudit.ArtifactDirectory
}

if (-not (Test-Path $SourceArtifactDir)) {
    throw "SourceArtifactDir khong ton tai: $SourceArtifactDir"
}

$summary = Get-Content -Path (Join-Path $SourceArtifactDir 'summary.json') -Raw | ConvertFrom-Json
$doc = Get-Content -Path (Join-Path $SourceArtifactDir 'document.json') -Raw | ConvertFrom-Json
$axisAudit = Get-Content -Path (Join-Path $SourceArtifactDir 'axis-audit.json') -Raw | ConvertFrom-Json
$graph = Get-Content -Path (Join-Path $SourceArtifactDir 'graph.json') -Raw | ConvertFrom-Json
$parentElements = Get-Content -Path (Join-Path $SourceArtifactDir 'parent-elements.json') -Raw | ConvertFrom-Json

$roundAuditItems = @($axisAudit.Items | Where-Object { $_.FamilyName -eq $FamilyName })
$roundIds = @($roundAuditItems | ForEach-Object { [int]$_.ElementId })
$superEdges = @($graph.Edges | Where-Object { $_.Relation -eq 'super_component' -and $roundIds -contains [int]$_.FromElementId })

$roundToParent = @{}
foreach ($edge in $superEdges) {
    $roundToParent[[int]$edge.FromElementId] = [int]$edge.ToElementId
}

$parentMap = @{}
foreach ($parent in @($parentElements)) {
    $parentMap[[int]$parent.ElementId] = $parent
}

$parentIds = @($superEdges | ForEach-Object { [int]$_.ToElementId } | Sort-Object -Unique)
$parentParamMap = @{}
if ($parentIds.Count -gt 0) {
    $parentParamQuery = @{
        DocumentKey = $doc.DocumentKey
        ViewScopeOnly = $false
        SelectedOnly = $false
        ElementIds = $parentIds
        MaxResults = [Math]::Max(200, $parentIds.Count + 20)
        IncludeParameters = $true
    }
    $parentParamResponse = Invoke-ReadTool -Tool 'element.query' -Payload $parentParamQuery -TargetDocument $doc.DocumentKey
    $parentParamItems = @((ConvertFrom-PayloadJson -Response $parentParamResponse).Items)

    foreach ($item in $parentParamItems) {
        $pmap = @{}
        foreach ($p in @($item.Parameters)) {
            $pmap[[string]$p.Name] = [string]$p.Value
        }
        $parentParamMap[[int]$item.ElementId] = $pmap
    }
}

$rows = @(
    $roundAuditItems | ForEach-Object {
        $round = $_
        $roundId = [int]$round.ElementId
        $parentId = if ($roundToParent.ContainsKey($roundId)) { [int]$roundToParent[$roundId] } else { $null }
        $parent = if ($null -ne $parentId -and $parentMap.ContainsKey($parentId)) { $parentMap[$parentId] } else { $null }
        $pp = if ($null -ne $parentId -and $parentParamMap.ContainsKey($parentId)) { $parentParamMap[$parentId] } else { @{} }
        $placementMode = Get-PlacementMode -AuditItem $round

        [pscustomobject]@{
            RoundElementId = $roundId
            RoundFamilyName = [string]$round.FamilyName
            RoundTypeName = [string]$round.TypeName
            RoundStatus = [string]$round.Status
            ProposedPlacementMode = $placementMode
            PlacementNote = Get-PlacementNote -PlacementMode $placementMode
            ProposedTargetFamily = 'Round'
            ProposedTargetType = $placementMode
            ParentElementId = $parentId
            ParentFamilyName = if ($null -ne $parent) { [string]$parent.FamilyName } else { '' }
            ParentTypeName = if ($null -ne $parent) { [string]$parent.TypeName } else { '' }
            ParentCategoryName = if ($null -ne $parent) { [string]$parent.CategoryName } else { '' }
            ParentMii_Diameter = if ($pp.ContainsKey('Mii_Diameter')) { [string]$pp['Mii_Diameter'] } else { '' }
            ParentMii_DimLength = if ($pp.ContainsKey('Mii_DimLength')) { [string]$pp['Mii_DimLength'] } else { '' }
            ParentMark = if ($pp.ContainsKey('Mark')) { [string]$pp['Mark'] } else { '' }
            OriginX = [double]$round.Origin.X
            OriginY = [double]$round.Origin.Y
            OriginZ = [double]$round.Origin.Z
            BasisXX = [double]$round.BasisX.X
            BasisXY = [double]$round.BasisX.Y
            BasisXZ = [double]$round.BasisX.Z
            BasisYX = [double]$round.BasisY.X
            BasisYY = [double]$round.BasisY.Y
            BasisYZ = [double]$round.BasisY.Z
            BasisZX = [double]$round.BasisZ.X
            BasisZY = [double]$round.BasisZ.Y
            BasisZZ = [double]$round.BasisZ.Z
            RotationAroundProjectZDegrees = [double]$round.RotationAroundProjectZDegrees
            AngleXDegrees = [double]$round.AngleXDegrees
            AngleYDegrees = [double]$round.AngleYDegrees
            AngleZDegrees = [double]$round.AngleZDegrees
        }
    }
)

$modeSummary = @(
    $rows | Group-Object -Property ProposedPlacementMode | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{
            ProposedPlacementMode = $_.Name
            Count = $_.Count
        }
    }
)

$typeSummary = @(
    $rows | Group-Object -Property ParentFamilyName, ParentTypeName, ProposedPlacementMode | Sort-Object Name | ForEach-Object {
        $first = $_.Group | Select-Object -First 1
        [pscustomobject]@{
            ParentFamilyName = $first.ParentFamilyName
            ParentTypeName = $first.ParentTypeName
            ProposedPlacementMode = $first.ProposedPlacementMode
            Count = $_.Count
        }
    }
)

$globalRoundCount = if ($summary.PSObject.Properties.Name -contains 'RoundCount') { [int]$summary.RoundCount } else { $rows.Count }

$result = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    SourceArtifactDir = $SourceArtifactDir
    ArtifactDirectory = $artifactDir
    DocumentTitle = $summary.DocumentTitle
    ActiveViewName = $summary.ActiveViewName
    FamilyName = $FamilyName
    GlobalRoundCount = $globalRoundCount
    TransformCoverageCount = $rows.Count
    MissingTransformCount = $globalRoundCount - $rows.Count
    ModeSummary = $modeSummary
    TypeSummary = $typeSummary
}

$csvPath = Join-Path $artifactDir 'round-externalization-map.csv'
$jsonPath = Join-Path $artifactDir 'round-externalization-map.json'
$summaryPath = Join-Path $artifactDir 'summary.json'

$rows | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
Write-JsonFile -Path $jsonPath -Data $rows
Write-JsonFile -Path $summaryPath -Data $result

$result | ConvertTo-Json -Depth 50
