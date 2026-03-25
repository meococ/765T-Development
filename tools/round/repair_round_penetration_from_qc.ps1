param(
    [string]$BridgeExe = "",
    [string]$ArtifactDir = "",
    [string[]]$StatusesToRerun = @("MISSING_INSTANCE", "CUT_MISSING", "AXIS_REVIEW", "PLACEMENT_REVIEW"),
    [switch]$DeleteOrphans,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$artifactRoot = Join-Path $projectRoot 'artifacts\round-penetration-cut-repair'
$repairArtifactDir = Join-Path $artifactRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $repairArtifactDir | Out-Null

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
    $root = Join-Path $projectRoot 'artifacts\round-penetration-cut'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    $dir = Get-ChildItem -Path $root -Directory |
        Where-Object {
            (Test-Path (Join-Path $_.FullName 'batch-request.json')) -and
            (Test-Path (Join-Path $_.FullName 'qc-request.json')) -and
            (Test-Path (Join-Path $_.FullName 'qc.json'))
        } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $dir) {
        throw "Khong tim thay round penetration artifact hop le trong $root"
    }

    return $dir.FullName
}

function Normalize-RoundPenStatus {
    param([string]$Status)

    $value = if ($null -eq $Status) { '' } else { [string]$Status }
    return ($value.Trim().ToUpperInvariant())
}

function Get-RoundPenActionProfile {
    param([string]$Status)

    switch (Normalize-RoundPenStatus $Status) {
        'CUT_OK' {
            return [pscustomobject]@{
                SuggestedAction = 'KEEP_AS_IS'
                DeleteBeforeRerun = $false
            }
        }
        'MISSING_INSTANCE' {
            return [pscustomobject]@{
                SuggestedAction = 'TARGETED_RERUN'
                DeleteBeforeRerun = $false
            }
        }
        'CUT_MISSING' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_OPENING_THEN_TARGETED_RERUN'
                DeleteBeforeRerun = $true
            }
        }
        'AXIS_REVIEW' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_OPENING_THEN_TARGETED_RERUN'
                DeleteBeforeRerun = $true
            }
        }
        'PLACEMENT_REVIEW' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_OPENING_THEN_TARGETED_RERUN'
                DeleteBeforeRerun = $true
            }
        }
        'RESIDUAL_PLAN' {
            return [pscustomobject]@{
                SuggestedAction = 'MANUAL_MODEL_REVIEW'
                DeleteBeforeRerun = $false
            }
        }
        'ORPHAN_INSTANCE' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_IF_TRACE_CONFIRMED'
                DeleteBeforeRerun = $true
            }
        }
        default {
            return [pscustomobject]@{
                SuggestedAction = 'INVESTIGATE'
                DeleteBeforeRerun = $false
            }
        }
    }
}

function ConvertTo-OrderedClone {
    param([Parameter(Mandatory = $true)][object]$InputObject)

    $clone = [ordered]@{}
    foreach ($prop in $InputObject.PSObject.Properties) {
        $clone[$prop.Name] = $prop.Value
    }

    return $clone
}

function Export-CsvOrBlank {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object[]]$Rows
    )

    if (@($Rows).Count -gt 0) {
        $Rows | Export-Csv -Path $Path -NoTypeInformation -Encoding UTF8
    }
    else {
        Set-Content -Path $Path -Value "" -Encoding UTF8
    }
}

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Resolve-LatestRoundPenetrationArtifactDir
}
elseif (-not (Test-Path $ArtifactDir)) {
    throw "ArtifactDir khong ton tai: $ArtifactDir"
}

$batchRequestPath = Join-Path $ArtifactDir 'batch-request.json'
$qcRequestPath = Join-Path $ArtifactDir 'qc-request.json'
$qcPath = Join-Path $ArtifactDir 'qc.json'

foreach ($requiredPath in @($batchRequestPath, $qcRequestPath, $qcPath)) {
    if (-not (Test-Path $requiredPath)) {
        throw "Artifact thieu file bat buoc: $requiredPath"
    }
}

$batchRequest = Get-Content -Path $batchRequestPath -Raw | ConvertFrom-Json
$qcRequest = Get-Content -Path $qcRequestPath -Raw | ConvertFrom-Json
$qc = Get-Content -Path $qcPath -Raw | ConvertFrom-Json
$qcItems = @($qc.Items)

$normalizedStatuses = @(
    $StatusesToRerun |
        ForEach-Object { Normalize-RoundPenStatus $_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique
)

$selectedItems = @($qcItems | Where-Object { $normalizedStatuses -contains (Normalize-RoundPenStatus $_.Status) })
$rerunSourceIds = @($selectedItems | ForEach-Object { [int]$_.SourceElementId } | Where-Object { $_ -gt 0 } | Sort-Object -Unique)
$deleteBeforeRerunIds = @(
    $selectedItems |
        Where-Object {
            $profile = Get-RoundPenActionProfile -Status ([string]$_.Status)
            $profile.DeleteBeforeRerun -and $null -ne $_.PenetrationElementId
        } |
        ForEach-Object { [int]$_.PenetrationElementId } |
        Where-Object { $_ -gt 0 } |
        Sort-Object -Unique
)
$orphanIds = @(
    $qcItems |
        Where-Object { (Normalize-RoundPenStatus $_.Status) -eq 'ORPHAN_INSTANCE' -and $null -ne $_.PenetrationElementId } |
        ForEach-Object { [int]$_.PenetrationElementId } |
        Where-Object { $_ -gt 0 } |
        Sort-Object -Unique
)

$repairRows = @(
    $qcItems | ForEach-Object {
        $status = Normalize-RoundPenStatus ([string]$_.Status)
        $profile = Get-RoundPenActionProfile -Status $status
        $openingId = if ($null -ne $_.PenetrationElementId) { [int]$_.PenetrationElementId } else { $null }
        [pscustomobject]@{
            SelectedForRerun = [bool]($normalizedStatuses -contains $status)
            SelectedForOrphanDelete = [bool]($DeleteOrphans.IsPresent -and $status -eq 'ORPHAN_INSTANCE')
            Status = [string]$_.Status
            SuggestedAction = [string]$profile.SuggestedAction
            SourceElementId = [int]$_.SourceElementId
            HostElementId = [int]$_.HostElementId
            PenetrationElementId = $openingId
            DeleteBeforeRerun = [bool]($deleteBeforeRerunIds -contains $openingId)
            HostClass = [string]$_.HostClass
            CassetteId = [string]$_.CassetteId
            AxisStatus = [string]$_.AxisStatus
            CutStatus = [string]$_.CutStatus
            TraceComment = [string]$_.TraceComment
            ResidualNote = [string]$_.ResidualNote
        }
    }
)

$rerunBatchRequest = ConvertTo-OrderedClone -InputObject $batchRequest
$rerunBatchRequest.SourceElementIds = @($rerunSourceIds)
$rerunBatchRequest.ForceRebuildFamilies = $true
$rerunBatchRequest.OverwriteExistingProjectFamilies = $true
$rerunBatchRequest.OverwriteFamilyFiles = $true

$preDeleteRequest = if ($deleteBeforeRerunIds.Count -gt 0) {
    [ordered]@{
        DocumentKey = [string]$batchRequest.DocumentKey
        ElementIds = @($deleteBeforeRerunIds)
    }
}
else {
    $null
}

$orphanDeleteRequest = if ($orphanIds.Count -gt 0) {
    [ordered]@{
        DocumentKey = [string]$batchRequest.DocumentKey
        ElementIds = @($orphanIds)
    }
}
else {
    $null
}

$initialStatusCounts = @(
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

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    SourceArtifactDir = $ArtifactDir
    ArtifactDirectory = $repairArtifactDir
    ExecuteRequested = [bool]$Execute
    DeleteOrphans = [bool]$DeleteOrphans
    SelectedStatuses = @($normalizedStatuses)
    RerunSourceIds = @($rerunSourceIds)
    DeleteBeforeRerunIds = @($deleteBeforeRerunIds)
    OrphanIds = @($orphanIds)
    InitialStatusCounts = $initialStatusCounts
}

Write-JsonFile -Path (Join-Path $repairArtifactDir 'selected-statuses.json') -Data @($normalizedStatuses)
Write-JsonFile -Path (Join-Path $repairArtifactDir 'batch-request.json') -Data $batchRequest
Write-JsonFile -Path (Join-Path $repairArtifactDir 'qc-request.json') -Data $qcRequest
Write-JsonFile -Path (Join-Path $repairArtifactDir 'qc.json') -Data $qc
Write-JsonFile -Path (Join-Path $repairArtifactDir 'rerun-batch-request.json') -Data $rerunBatchRequest
if ($null -ne $preDeleteRequest) {
    Write-JsonFile -Path (Join-Path $repairArtifactDir 'pre-rerun-delete.request.json') -Data $preDeleteRequest
}
if ($null -ne $orphanDeleteRequest) {
    Write-JsonFile -Path (Join-Path $repairArtifactDir 'orphan-delete.request.json') -Data $orphanDeleteRequest
}
Export-CsvOrBlank -Path (Join-Path $repairArtifactDir 'repair-candidates.csv') -Rows $repairRows

if ($Execute.IsPresent) {
    $documentResponse = Invoke-ReadTool -Tool 'document.get_active'
    $doc = ConvertFrom-PayloadJson -Response $documentResponse
    $docKey = [string]$doc.DocumentKey

    if (-not [string]::Equals($docKey, [string]$batchRequest.DocumentKey, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Active document key '$docKey' khong khop artifact document key '$($batchRequest.DocumentKey)'."
    }

    Write-JsonFile -Path (Join-Path $repairArtifactDir 'document.response.json') -Data $documentResponse
    Write-JsonFile -Path (Join-Path $repairArtifactDir 'document.json') -Data $doc

    if ($null -ne $preDeleteRequest) {
        $preDeletePreviewResponse = Invoke-MutationPreview -Tool 'element.delete_safe' -Payload $preDeleteRequest -TargetDocument $docKey
        $preDeletePreview = ConvertFrom-PayloadJson -Response $preDeletePreviewResponse
        $preDeleteExecuteResponse = Invoke-MutationExecute `
            -Tool 'element.delete_safe' `
            -Payload $preDeleteRequest `
            -ApprovalToken $preDeletePreviewResponse.ApprovalToken `
            -PreviewRunId $preDeletePreviewResponse.PreviewRunId `
            -ExpectedContext $preDeletePreview.ResolvedContext `
            -TargetDocument $docKey

        Write-JsonFile -Path (Join-Path $repairArtifactDir 'pre-rerun-delete.preview.response.json') -Data $preDeletePreviewResponse
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'pre-rerun-delete.preview.json') -Data $preDeletePreview
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'pre-rerun-delete.execute.response.json') -Data $preDeleteExecuteResponse
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'pre-rerun-delete.execute.json') -Data (ConvertFrom-PayloadJson -Response $preDeleteExecuteResponse)
    }

    if ($DeleteOrphans.IsPresent -and $null -ne $orphanDeleteRequest) {
        $orphanDeletePreviewResponse = Invoke-MutationPreview -Tool 'element.delete_safe' -Payload $orphanDeleteRequest -TargetDocument $docKey
        $orphanDeletePreview = ConvertFrom-PayloadJson -Response $orphanDeletePreviewResponse
        $orphanDeleteExecuteResponse = Invoke-MutationExecute `
            -Tool 'element.delete_safe' `
            -Payload $orphanDeleteRequest `
            -ApprovalToken $orphanDeletePreviewResponse.ApprovalToken `
            -PreviewRunId $orphanDeletePreviewResponse.PreviewRunId `
            -ExpectedContext $orphanDeletePreview.ResolvedContext `
            -TargetDocument $docKey

        Write-JsonFile -Path (Join-Path $repairArtifactDir 'orphan-delete.preview.response.json') -Data $orphanDeletePreviewResponse
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'orphan-delete.preview.json') -Data $orphanDeletePreview
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'orphan-delete.execute.response.json') -Data $orphanDeleteExecuteResponse
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'orphan-delete.execute.json') -Data (ConvertFrom-PayloadJson -Response $orphanDeleteExecuteResponse)
    }

    if ($rerunSourceIds.Count -gt 0) {
        $rerunPreviewResponse = Invoke-MutationPreview -Tool 'batch.create_round_penetration_cut_safe' -Payload $rerunBatchRequest -TargetDocument $docKey
        $rerunPreview = ConvertFrom-PayloadJson -Response $rerunPreviewResponse
        $rerunExecuteResponse = Invoke-MutationExecute `
            -Tool 'batch.create_round_penetration_cut_safe' `
            -Payload $rerunBatchRequest `
            -ApprovalToken $rerunPreviewResponse.ApprovalToken `
            -PreviewRunId $rerunPreviewResponse.PreviewRunId `
            -ExpectedContext $rerunPreview.ResolvedContext `
            -TargetDocument $docKey

        Write-JsonFile -Path (Join-Path $repairArtifactDir 'rerun.preview.response.json') -Data $rerunPreviewResponse
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'rerun.preview.json') -Data $rerunPreview
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'rerun.execute.response.json') -Data $rerunExecuteResponse
        Write-JsonFile -Path (Join-Path $repairArtifactDir 'rerun.execute.json') -Data (ConvertFrom-PayloadJson -Response $rerunExecuteResponse)
    }

    $postQcResponse = Invoke-ReadTool -Tool 'report.round_penetration_cut_qc' -Payload $qcRequest -TargetDocument $docKey
    $postQc = ConvertFrom-PayloadJson -Response $postQcResponse
    $postQcItems = @($postQc.Items)
    $postReviewRows = @(
        $postQcItems | ForEach-Object {
            $profile = Get-RoundPenActionProfile -Status ([string]$_.Status)
            [pscustomobject]@{
                ReviewBucket = if ((Normalize-RoundPenStatus ([string]$_.Status)) -eq 'CUT_OK') { 'OK' } else { 'REVIEW_REQUIRED' }
                SuggestedAction = [string]$profile.SuggestedAction
                SourceElementId = [int]$_.SourceElementId
                HostElementId = [int]$_.HostElementId
                PenetrationElementId = if ($null -ne $_.PenetrationElementId) { [int]$_.PenetrationElementId } else { $null }
                Status = [string]$_.Status
                AxisStatus = [string]$_.AxisStatus
                CutStatus = [string]$_.CutStatus
                HostClass = [string]$_.HostClass
                CassetteId = [string]$_.CassetteId
                TraceComment = [string]$_.TraceComment
                ResidualNote = [string]$_.ResidualNote
            }
        }
    )
    $postStatusCounts = @(
        $postQcItems |
            Group-Object -Property Status |
            Sort-Object Name |
            ForEach-Object {
                [pscustomobject]@{
                    Status = [string]$_.Name
                    Count = [int]$_.Count
                }
            }
    )

    Write-JsonFile -Path (Join-Path $repairArtifactDir 'post-repair-qc.response.json') -Data $postQcResponse
    Write-JsonFile -Path (Join-Path $repairArtifactDir 'post-repair-qc.json') -Data $postQc
    Export-CsvOrBlank -Path (Join-Path $repairArtifactDir 'post-repair-review-sheet.csv') -Rows $postReviewRows

    $summary | Add-Member -NotePropertyName PostRepairStatusCounts -NotePropertyValue $postStatusCounts
}

Write-JsonFile -Path (Join-Path $repairArtifactDir 'summary.json') -Data $summary
$summary | ConvertTo-Json -Depth 20
