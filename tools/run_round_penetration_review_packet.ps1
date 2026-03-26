param(
    [string]$BridgeExe = "",
    [string]$ArtifactDir = "",
    [string[]]$Statuses = @("PLACEMENT_REVIEW", "CUT_MISSING", "AXIS_REVIEW", "ORPHAN_INSTANCE"),
    [int]$MaxItems = 12,
    [string]$SheetNumber = "BIM765T-RP-01",
    [string]$SheetName = "Round Penetration Review",
    [string]$ViewNamePrefix = "BIM765T_RoundPen_Review",
    [string]$TitleBlockTypeName = "",
    [double]$SectionBoxPaddingFeet = 0.75,
    [string]$ImageOutputPath = "",
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$artifactRoot = Join-Path $projectRoot 'artifacts\round-penetration-review-packet'
$runArtifactDir = Join-Path $artifactRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runArtifactDir | Out-Null

function Assert-BridgeSuccess {
    param(
        [Parameter(Mandatory = $true)][object]$Response,
        [Parameter(Mandatory = $true)][string]$Tool
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
        [Parameter(Mandatory = $true)][string]$Tool,
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
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$Payload,
        [string]$TargetDocument = ""
    )

    $payloadJson = $Payload | ConvertTo-Json -Depth 100
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson -DryRun
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-MutationExecute {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$Payload,
        [Parameter(Mandatory = $true)][string]$ApprovalToken,
        [Parameter(Mandatory = $true)][string]$PreviewRunId,
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

function Resolve-LatestRoundPenetrationArtifactDir {
    $roots = @(
        Join-Path $projectRoot 'artifacts\round-penetration-cut-repair',
        Join-Path $projectRoot 'artifacts\round-penetration-cut'
    )

    $candidates = foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem -Path $root -Directory | Where-Object {
            (Test-Path (Join-Path $_.FullName 'qc.json')) -or
            (Test-Path (Join-Path $_.FullName 'post-repair-qc.json'))
        }
    }

    $latest = $candidates |
        Sort-Object @{ Expression = { $_.LastWriteTimeUtc }; Descending = $true }, @{ Expression = { $_.FullName }; Descending = $true } |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "Khong tim thay artifact round penetration co QC."
    }

    return $latest.FullName
}

function Normalize-Status {
    param([string]$Status)

    $value = if ($null -eq $Status) { '' } else { [string]$Status }
    return ($value.Trim().ToUpperInvariant())
}

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Resolve-LatestRoundPenetrationArtifactDir
}

if (-not (Test-Path $ArtifactDir)) {
    throw "ArtifactDir khong ton tai: $ArtifactDir"
}

$batchRequestPath = Join-Path $ArtifactDir 'batch-request.json'
$qcPath = if (Test-Path (Join-Path $ArtifactDir 'post-repair-qc.json')) {
    Join-Path $ArtifactDir 'post-repair-qc.json'
}
else {
    Join-Path $ArtifactDir 'qc.json'
}

if (-not (Test-Path $batchRequestPath)) {
    throw "Khong tim thay batch-request.json trong $ArtifactDir"
}
if (-not (Test-Path $qcPath)) {
    throw "Khong tim thay qc json trong $ArtifactDir"
}

$batchRequest = Get-Content -Path $batchRequestPath -Raw | ConvertFrom-Json
$qc = Get-Content -Path $qcPath -Raw | ConvertFrom-Json
$normalizedStatuses = @($Statuses | ForEach-Object { Normalize-Status $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
$selectedQcItems = @($qc.Items | Where-Object {
    $status = Normalize-Status ([string]$_.Status)
    ($normalizedStatuses.Count -eq 0) -or ($normalizedStatuses -contains $status)
})

$penetrationIds = @(
    $selectedQcItems |
        Where-Object { $null -ne $_.PenetrationElementId -and [int]$_.PenetrationElementId -gt 0 } |
        ForEach-Object { [int]$_.PenetrationElementId } |
        Sort-Object -Unique
)

$sourceIds = @(
    $selectedQcItems |
        Where-Object { [int]$_.SourceElementId -gt 0 } |
        ForEach-Object { [int]$_.SourceElementId } |
        Sort-Object -Unique
)

if ([string]::IsNullOrWhiteSpace($ImageOutputPath)) {
    $ImageOutputPath = Join-Path $runArtifactDir 'review-sheet.png'
}

$request = [ordered]@{
    DocumentKey = [string]$batchRequest.DocumentKey
    TargetFamilyName = [string]$batchRequest.TargetFamilyName
    SourceElementClasses = @($batchRequest.SourceElementClasses)
    HostElementClasses = @($batchRequest.HostElementClasses)
    SourceFamilyNameContains = @($batchRequest.SourceFamilyNameContains)
    SourceElementIds = @($sourceIds)
    PenetrationElementIds = @($penetrationIds)
    GybClearancePerSideInches = [double]$batchRequest.GybClearancePerSideInches
    WfrClearancePerSideInches = [double]$batchRequest.WfrClearancePerSideInches
    AxisToleranceDegrees = [double]$batchRequest.AxisToleranceDegrees
    TraceCommentPrefix = [string]$batchRequest.TraceCommentPrefix
    MaxResults = [int]$batchRequest.MaxResults
    MaxItems = [int]([Math]::Max(1, $MaxItems))
    ViewNamePrefix = $ViewNamePrefix
    SheetNumber = $SheetNumber
    SheetName = $SheetName
    TitleBlockTypeName = $TitleBlockTypeName
    SectionBoxPaddingFeet = [double]$SectionBoxPaddingFeet
    CopyActive3DOrientation = $false
    ReuseExistingViews = $true
    ReuseExistingSheet = $true
    ExportSheetImage = $true
    ImageOutputPath = $ImageOutputPath
    ActivateSheetAfterCreate = $false
    IncludeOnlyNonOkQcItems = $false
}

$request | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runArtifactDir 'review-packet.request.json') -Encoding UTF8
$selectedQcItems | Export-Csv -Path (Join-Path $runArtifactDir 'selected-qc-items.csv') -NoTypeInformation -Encoding UTF8

$preview = Invoke-MutationPreview -Tool 'review.round_penetration_packet_safe' -Payload $request -TargetDocument ([string]$batchRequest.DocumentKey)
$preview | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runArtifactDir 'review-packet.preview.json') -Encoding UTF8

$previewBody = ConvertFrom-PayloadJson -Response $preview
$summary = [ordered]@{
    ArtifactDir = $ArtifactDir
    ReviewPacketArtifactDir = $runArtifactDir
    SelectedQcCount = @($selectedQcItems).Count
    SelectedStatuses = @($normalizedStatuses)
    PenetrationElementCount = @($penetrationIds).Count
    SourceElementCount = @($sourceIds).Count
    SheetNumber = $SheetNumber
    SheetName = $SheetName
    ImageOutputPath = $ImageOutputPath
    PreviewStatus = [string]$preview.StatusCode
    ExecuteRequested = [bool]$Execute.IsPresent
}

if ($Execute) {
    $executeResponse = Invoke-MutationExecute `
        -Tool 'review.round_penetration_packet_safe' `
        -Payload $request `
        -ApprovalToken ([string]$preview.ApprovalToken) `
        -PreviewRunId ([string]$preview.PreviewRunId) `
        -ExpectedContext $previewBody.ResolvedContext `
        -TargetDocument ([string]$batchRequest.DocumentKey)

    $executeResponse | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runArtifactDir 'review-packet.execute.json') -Encoding UTF8
    $summary.ExecuteStatus = [string]$executeResponse.StatusCode
    $summary.ChangedIds = @($executeResponse.ChangedIds)
    $summary.Artifacts = @($executeResponse.Artifacts)
}

$summary | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runArtifactDir 'review-packet.summary.json') -Encoding UTF8
$summary
