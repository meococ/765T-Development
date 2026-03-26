param(
    [string]$ArtifactDir = "",
    [string]$QcJsonPath = "",
    [string]$OutputPath = "",
    [switch]$PreferPostRepair
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)

function Resolve-LatestRoundPenArtifactDir {
    $roots = @(
        Join-Path $projectRoot 'artifacts\round-penetration-cut-repair'
        Join-Path $projectRoot 'artifacts\round-penetration-cut'
    )

    foreach ($root in $roots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $dir = Get-ChildItem -Path $root -Directory |
            Where-Object {
                (Test-Path (Join-Path $_.FullName 'qc.json')) -or
                (Test-Path (Join-Path $_.FullName 'post-repair-qc.json'))
            } |
            Sort-Object Name -Descending |
            Select-Object -First 1

        if ($null -ne $dir) {
            return $dir.FullName
        }
    }

    throw "Khong tim thay artifact round penetration nao co file qc."
}

function Resolve-QcJsonPath {
    param(
        [string]$RequestedArtifactDir,
        [string]$RequestedQcJsonPath,
        [bool]$UsePostRepairPreferred
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedQcJsonPath)) {
        if (-not (Test-Path $RequestedQcJsonPath)) {
            throw "QcJsonPath khong ton tai: $RequestedQcJsonPath"
        }

        return (Resolve-Path $RequestedQcJsonPath).Path
    }

    $artifactDirResolved = if ([string]::IsNullOrWhiteSpace($RequestedArtifactDir)) {
        Resolve-LatestRoundPenArtifactDir
    }
    else {
        if (-not (Test-Path $RequestedArtifactDir)) {
            throw "ArtifactDir khong ton tai: $RequestedArtifactDir"
        }

        (Resolve-Path $RequestedArtifactDir).Path
    }

    $candidateNames = if ($UsePostRepairPreferred) {
        @('post-repair-qc.json', 'qc.json')
    }
    else {
        @('qc.json', 'post-repair-qc.json')
    }

    foreach ($candidateName in $candidateNames) {
        $candidatePath = Join-Path $artifactDirResolved $candidateName
        if (Test-Path $candidatePath) {
            return $candidatePath
        }
    }

    throw "Artifact '$artifactDirResolved' khong co qc.json hoac post-repair-qc.json."
}

function Normalize-RoundPenStatus {
    param([string]$Status)

    $value = if ($null -eq $Status) { '' } else { [string]$Status }
    return ($value.Trim().ToUpperInvariant())
}

function Get-RoundPenSuggestedAction {
    param([string]$Status)

    switch (Normalize-RoundPenStatus $Status) {
        'CUT_OK' { return 'KEEP_AS_IS' }
        'MISSING_INSTANCE' { return 'TARGETED_RERUN' }
        'CUT_MISSING' { return 'DELETE_OPENING_THEN_TARGETED_RERUN' }
        'AXIS_REVIEW' { return 'DELETE_OPENING_THEN_TARGETED_RERUN' }
        'PLACEMENT_REVIEW' { return 'DELETE_OPENING_THEN_TARGETED_RERUN' }
        'RESIDUAL_PLAN' { return 'MANUAL_MODEL_REVIEW' }
        'ORPHAN_INSTANCE' { return 'DELETE_IF_TRACE_CONFIRMED' }
        default { return 'INVESTIGATE' }
    }
}

$resolvedQcJsonPath = Resolve-QcJsonPath -RequestedArtifactDir $ArtifactDir -RequestedQcJsonPath $QcJsonPath -UsePostRepairPreferred ([bool]$PreferPostRepair)
$resolvedArtifactDir = Split-Path $resolvedQcJsonPath -Parent
$qc = Get-Content -Path $resolvedQcJsonPath -Raw | ConvertFrom-Json
$qcItems = @($qc.Items)

$reviewRows = @(
    $qcItems | ForEach-Object {
        [pscustomobject]@{
            ReviewBucket = if ((Normalize-RoundPenStatus ([string]$_.Status)) -eq 'CUT_OK') { 'OK' } else { 'REVIEW_REQUIRED' }
            SuggestedAction = Get-RoundPenSuggestedAction -Status ([string]$_.Status)
            SourceElementId = [int]$_.SourceElementId
            HostElementId = [int]$_.HostElementId
            PenetrationElementId = if ($null -ne $_.PenetrationElementId) { [int]$_.PenetrationElementId } else { $null }
            HostClass = [string]$_.HostClass
            CassetteId = [string]$_.CassetteId
            Status = [string]$_.Status
            AxisStatus = [string]$_.AxisStatus
            CutStatus = [string]$_.CutStatus
            PenetrationFamilyName = [string]$_.PenetrationFamilyName
            PenetrationTypeName = [string]$_.PenetrationTypeName
            TraceComment = [string]$_.TraceComment
            ResidualNote = [string]$_.ResidualNote
        }
    }
)

$statusCounts = @(
    $qcItems |
        Group-Object -Property Status |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                Status = [string]$_.Name
                Count = [int]$_.Count
            }
        }
)

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $resolvedArtifactDir 'review-sheet.csv'
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

$parent = Split-Path $OutputPath -Parent
if ($parent) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}

if ($reviewRows.Count -gt 0) {
    $reviewRows | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
}
else {
    Set-Content -Path $OutputPath -Value "" -Encoding UTF8
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $resolvedArtifactDir
    QcJsonPath = $resolvedQcJsonPath
    OutputPath = $OutputPath
    RowCount = $reviewRows.Count
    StatusCounts = $statusCounts
}

Write-JsonFile -Path (Join-Path $resolvedArtifactDir 'review-sheet.summary.json') -Data $summary
$summary | ConvertTo-Json -Depth 20
