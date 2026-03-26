param(
    [string]$WrapperQcArtifactDir = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Join-Path $PSScriptRoot '..')
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-mitek-export-contract\{0}" -f $runId)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

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

function Import-JsonArrayFile {
    param([string]$Path)

    $rows = Get-Content -Raw -Path $Path | ConvertFrom-Json
    if ($rows.Count -eq 1 -and $rows[0] -is [System.Array]) {
        return @($rows[0])
    }

    return @($rows)
}

function Get-CanonicalTypeName {
    param(
        [string]$ActualTypeName,
        [string]$Axis
    )

    if ([string]::IsNullOrWhiteSpace($ActualTypeName)) {
        return ''
    }

    switch ($Axis) {
        'AXIS_X' { return $ActualTypeName }
        'AXIS_Y' { return ($ActualTypeName -replace '^AXIS_Y__', 'AXIS_X__') }
        'AXIS_Z' { return ($ActualTypeName -replace '^AXIS_Z__', 'AXIS_X__') }
        default { return $ActualTypeName }
    }
}

if ([string]::IsNullOrWhiteSpace($WrapperQcArtifactDir)) {
    $WrapperQcArtifactDir = Find-LatestWrapperQcDir -ProjectRoot $projectRoot
}

$summaryPath = Join-Path $WrapperQcArtifactDir 'summary.json'
$pairsPath = Join-Path $WrapperQcArtifactDir 'pairs.json'
if (-not (Test-Path $pairsPath)) {
    throw "Khong tim thay pairs.json tai: $pairsPath"
}

$qcSummary = if (Test-Path $summaryPath) { Get-Content -Raw -Path $summaryPath | ConvertFrom-Json } else { $null }
$pairs = Import-JsonArrayFile -Path $pairsPath

$rows = foreach ($pair in $pairs) {
    $axis = [string]$pair.ActualGeometryAxis
    $actualTypeName = [string]$pair.ActualTypeName
    $canonicalTypeName = Get-CanonicalTypeName -ActualTypeName $actualTypeName -Axis $axis

    $strategy = ''
    $exportTruthStatus = ''
    $mitekPose = ''
    $rotationContract = ''
    $rotationDegrees = ''
    $confidence = ''
    $notes = ''

    switch ($axis) {
        'AXIS_X' {
            $strategy = 'SAFE_AS_IS_LOCAL_X'
            $exportTruthStatus = 'READY_X_AS_IS'
            $mitekPose = 'AS_IS'
            $rotationContract = 'NONE'
            $rotationDegrees = '0'
            $confidence = 'HIGH'
            $notes = 'Dung nguyen current Round_Project; local X da la penetration axis semantic downstream.'
        }
        'AXIS_Y' {
            $strategy = 'CANONICALIZE_TO_AXIS_X_IN_PLANE'
            $exportTruthStatus = 'READY_Y_CANONICAL_X'
            $mitekPose = 'ROTATE_IN_PLANE_TO_Y'
            $rotationContract = 'GLOBAL_Z_IN_PLANE'
            $rotationDegrees = '+90_CANDIDATE'
            $confidence = 'MEDIUM'
            $notes = 'Dung type AXIS_X__... canonical local X, sau do rotate trong mat phang theo Y.'
        }
        'AXIS_Z' {
            $strategy = 'CANONICALIZE_TO_AXIS_X_3D'
            $exportTruthStatus = 'READY_Z_CANONICAL_X'
            $mitekPose = 'ROTATE_3D_TO_Z'
            $rotationContract = 'GLOBAL_Y_3D'
            $rotationDegrees = '-90_VALIDATED_SPIKE'
            $confidence = 'HIGH'
            $notes = 'Dung type AXIS_X__... canonical local X, sau do rotate 3D len Z theo spike da validate.'
        }
        default {
            $strategy = 'UNKNOWN'
            $exportTruthStatus = 'REVIEW_REQUIRED'
            $mitekPose = 'UNKNOWN'
            $rotationContract = 'UNKNOWN'
            $rotationDegrees = ''
            $confidence = 'LOW'
            $notes = 'Khong xac dinh duoc geometry axis.'
        }
    }

    [pscustomobject]@{
        PairNumber = [int]$pair.PairNumber
        PairTag = [string]$pair.PairTag
        OldRoundElementId = [int]$pair.OldRoundElementId
        NewWrapperElementId = [int]$pair.NewWrapperElementId
        ProposedPlacementMode = [string]$pair.ProposedPlacementMode
        ActualGeometryAxis = $axis
        ActualTypeName = $actualTypeName
        CanonicalExportTypeName = $canonicalTypeName
        ExportStrategy = $strategy
        ExportTruthStatus = $exportTruthStatus
        MitekPose = $mitekPose
        RotationContract = $rotationContract
        RotationDegrees = $rotationDegrees
        Confidence = $confidence
        OldDiameter = [string]$pair.OldDiameter
        OldLength = [string]$pair.OldLength
        Notes = $notes
    }
}

$strategySummary = @(
    $rows |
        Group-Object ExportStrategy |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                ExportStrategy = [string]$_.Name
                Count = [int]$_.Count
            }
        }
)

$statusSummary = @(
    $rows |
        Group-Object ExportTruthStatus |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                ExportTruthStatus = [string]$_.Name
                Count = [int]$_.Count
            }
        }
)

$axisSummary = @(
    $rows |
        Group-Object ActualGeometryAxis |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                ActualGeometryAxis = [string]$_.Name
                Count = [int]$_.Count
            }
        }
)

$csvLines = @(
    'PairNumber,PairTag,OldRoundElementId,NewWrapperElementId,ProposedPlacementMode,ActualGeometryAxis,ActualTypeName,CanonicalExportTypeName,ExportStrategy,ExportTruthStatus,MitekPose,RotationContract,RotationDegrees,Confidence,OldDiameter,OldLength,Notes'
)

foreach ($row in $rows) {
    $fields = @(
        $row.PairNumber,
        $row.PairTag,
        $row.OldRoundElementId,
        $row.NewWrapperElementId,
        $row.ProposedPlacementMode,
        $row.ActualGeometryAxis,
        $row.ActualTypeName,
        $row.CanonicalExportTypeName,
        $row.ExportStrategy,
        $row.ExportTruthStatus,
        $row.MitekPose,
        $row.RotationContract,
        $row.RotationDegrees,
        $row.Confidence,
        $row.OldDiameter,
        $row.OldLength,
        $row.Notes
    ) | ForEach-Object {
        '"' + ([string]$_).Replace('"', '""') + '"'
    }

    $csvLines += ($fields -join ',')
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    WrapperQcArtifactDir = $WrapperQcArtifactDir
    DocumentTitle = if ($qcSummary) { [string]$qcSummary.DocumentTitle } else { '' }
    DocumentKey = if ($qcSummary) { [string]$qcSummary.DocumentKey } else { '' }
    PairCount = [int]@($rows).Count
    X_AsIs_Count = [int](@($rows | Where-Object { $_.ExportStrategy -eq 'SAFE_AS_IS_LOCAL_X' }).Count)
    Y_Canonicalize_Count = [int](@($rows | Where-Object { $_.ExportStrategy -eq 'CANONICALIZE_TO_AXIS_X_IN_PLANE' }).Count)
    Z_Canonicalize_Count = [int](@($rows | Where-Object { $_.ExportStrategy -eq 'CANONICALIZE_TO_AXIS_X_3D' }).Count)
    StrategySummary = $strategySummary
    ExportTruthStatusSummary = $statusSummary
    AxisSummary = $axisSummary
}

Write-JsonFile -Path (Join-Path $artifactDir 'export-contract-rows.json') -Data $rows
Set-Content -Path (Join-Path $artifactDir 'export-contract-rows.csv') -Value $csvLines -Encoding UTF8
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary

$summary | ConvertTo-Json -Depth 100
