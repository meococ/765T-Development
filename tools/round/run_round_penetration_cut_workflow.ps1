param(
    [string]$BridgeExe = "",
    [string]$TargetFamilyName = "Mii_Pen-Round_Project",
    [string[]]$SourceElementClasses = @("PIP", "PPF", "PPG"),
    [string[]]$HostElementClasses = @("GYB", "WFR"),
    [string[]]$SourceFamilyNameContains = @("PIP", "PPF", "PPG"),
    [int[]]$SourceElementIds = @(),
    [double]$GybClearancePerSideInches = 0.25,
    [double]$WfrClearancePerSideInches = 0.125,
    [double]$AxisToleranceDegrees = 5.0,
    [string]$TraceCommentPrefix = "BIM765T_PEN_ROUND",
    [int]$MaxResults = 5000,
    [string]$OutputDirectory = "",
    [switch]$ForceRebuildFamilies,
    [switch]$Execute,
    [int]$MaxCutRetries = 2,
    [int]$RetryBackoffMs = 150
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$artifactRoot = Join-Path $projectRoot 'artifacts\round-penetration-cut'
$artifactDir = Join-Path $artifactRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

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
                ReviewPriority = 'LOW'
                AutoRepairEligible = $false
                DeleteBeforeRerun = $false
            }
        }
        'MISSING_INSTANCE' {
            return [pscustomobject]@{
                SuggestedAction = 'TARGETED_RERUN'
                ReviewPriority = 'HIGH'
                AutoRepairEligible = $true
                DeleteBeforeRerun = $false
            }
        }
        'CUT_MISSING' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_OPENING_THEN_TARGETED_RERUN'
                ReviewPriority = 'HIGH'
                AutoRepairEligible = $true
                DeleteBeforeRerun = $true
            }
        }
        'AXIS_REVIEW' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_OPENING_THEN_TARGETED_RERUN'
                ReviewPriority = 'HIGH'
                AutoRepairEligible = $true
                DeleteBeforeRerun = $true
            }
        }
        'PLACEMENT_REVIEW' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_OPENING_THEN_TARGETED_RERUN'
                ReviewPriority = 'HIGH'
                AutoRepairEligible = $true
                DeleteBeforeRerun = $true
            }
        }
        'RESIDUAL_PLAN' {
            return [pscustomobject]@{
                SuggestedAction = 'MANUAL_MODEL_REVIEW'
                ReviewPriority = 'MEDIUM'
                AutoRepairEligible = $false
                DeleteBeforeRerun = $false
            }
        }
        'ORPHAN_INSTANCE' {
            return [pscustomobject]@{
                SuggestedAction = 'DELETE_IF_TRACE_CONFIRMED'
                ReviewPriority = 'MEDIUM'
                AutoRepairEligible = $false
                DeleteBeforeRerun = $true
            }
        }
        default {
            return [pscustomobject]@{
                SuggestedAction = 'INVESTIGATE'
                ReviewPriority = 'MEDIUM'
                AutoRepairEligible = $false
                DeleteBeforeRerun = $false
            }
        }
    }
}

function Export-CsvOrBlank {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Rows
    )

    if (@($Rows).Count -gt 0) {
        $Rows | Export-Csv -Path $Path -NoTypeInformation -Encoding UTF8
    }
    else {
        Set-Content -Path $Path -Value "" -Encoding UTF8
    }
}

$documentResponse = Invoke-ReadTool -Tool 'document.get_active'
$doc = ConvertFrom-PayloadJson -Response $documentResponse
$docKey = [string]$doc.DocumentKey

$commonRequest = [ordered]@{
    DocumentKey = $docKey
    TargetFamilyName = $TargetFamilyName
    SourceElementClasses = @($SourceElementClasses)
    HostElementClasses = @($HostElementClasses)
    SourceFamilyNameContains = @($SourceFamilyNameContains)
    SourceElementIds = @($SourceElementIds)
    GybClearancePerSideInches = $GybClearancePerSideInches
    WfrClearancePerSideInches = $WfrClearancePerSideInches
    AxisToleranceDegrees = $AxisToleranceDegrees
    TraceCommentPrefix = $TraceCommentPrefix
    MaxResults = $MaxResults
}

$planRequest = [ordered]@{}
$commonRequest.GetEnumerator() | ForEach-Object { $planRequest[$_.Key] = $_.Value }
$planRequest.IncludeExisting = $true

$batchRequest = [ordered]@{}
$commonRequest.GetEnumerator() | ForEach-Object { $batchRequest[$_.Key] = $_.Value }
$batchRequest.OutputDirectory = $OutputDirectory
$batchRequest.OverwriteFamilyFiles = $true
$batchRequest.OverwriteExistingProjectFamilies = $true
$batchRequest.ForceRebuildFamilies = [bool]$ForceRebuildFamilies
$batchRequest.SetCommentsTrace = $true
$batchRequest.RequireAxisAlignedResult = $true
$batchRequest.MaxCutRetries = $MaxCutRetries
$batchRequest.RetryBackoffMs = $RetryBackoffMs

$qcRequest = [ordered]@{}
$commonRequest.GetEnumerator() | ForEach-Object { $qcRequest[$_.Key] = $_.Value }

$planResponse = Invoke-ReadTool -Tool 'report.round_penetration_cut_plan' -Payload $planRequest -TargetDocument $docKey
$plan = ConvertFrom-PayloadJson -Response $planResponse

$previewResponse = Invoke-MutationPreview -Tool 'batch.create_round_penetration_cut_safe' -Payload $batchRequest -TargetDocument $docKey
$preview = ConvertFrom-PayloadJson -Response $previewResponse

$executeResponse = $null
$executePayload = $null
if ($Execute.IsPresent) {
    $executeResponse = Invoke-MutationExecute `
        -Tool 'batch.create_round_penetration_cut_safe' `
        -Payload $batchRequest `
        -ApprovalToken $previewResponse.ApprovalToken `
        -PreviewRunId $previewResponse.PreviewRunId `
        -ExpectedContext $preview.ResolvedContext `
        -TargetDocument $docKey
    $executePayload = ConvertFrom-PayloadJson -Response $executeResponse
}

$qcResponse = Invoke-ReadTool -Tool 'report.round_penetration_cut_qc' -Payload $qcRequest -TargetDocument $docKey
$qc = ConvertFrom-PayloadJson -Response $qcResponse

$planItems = @($plan.Items)
$qcItems = @($qc.Items)

$planRows = @(
    $planItems | ForEach-Object {
        [pscustomobject]@{
            SourceElementId = [int]$_.SourceElementId
            HostElementId = [int]$_.HostElementId
            SourceFamilyName = [string]$_.SourceFamilyName
            SourceTypeName = [string]$_.SourceTypeName
            HostFamilyName = [string]$_.HostFamilyName
            HostTypeName = [string]$_.HostTypeName
            HostClass = [string]$_.HostClass
            CassetteId = [string]$_.CassetteId
            NominalOD = [string]$_.NominalOD
            OpeningDiameterFeet = [double]$_.OpeningDiameterFeet
            CutLengthFeet = [double]$_.CutLengthFeet
            TypeName = [string]$_.TypeName
            ExistingPenetrationElementId = if ($null -ne $_.ExistingPenetrationElementId) { [int]$_.ExistingPenetrationElementId } else { $null }
            CanPlace = [bool]$_.CanPlace
            CanCut = [bool]$_.CanCut
            TraceComment = [string]$_.TraceComment
            ResidualNote = [string]$_.ResidualNote
        }
    }
)

$qcRows = @(
    $qcItems | ForEach-Object {
        $profile = Get-RoundPenActionProfile -Status ([string]$_.Status)
        [pscustomobject]@{
            SourceElementId = [int]$_.SourceElementId
            HostElementId = [int]$_.HostElementId
            PenetrationElementId = if ($null -ne $_.PenetrationElementId) { [int]$_.PenetrationElementId } else { $null }
            PenetrationFamilyName = [string]$_.PenetrationFamilyName
            PenetrationTypeName = [string]$_.PenetrationTypeName
            HostClass = [string]$_.HostClass
            CassetteId = [string]$_.CassetteId
            Status = [string]$_.Status
            AxisStatus = [string]$_.AxisStatus
            CutStatus = [string]$_.CutStatus
            SuggestedAction = [string]$profile.SuggestedAction
            ReviewPriority = [string]$profile.ReviewPriority
            AutoRepairEligible = [bool]$profile.AutoRepairEligible
            DeleteBeforeRerun = [bool]$profile.DeleteBeforeRerun
            TraceComment = [string]$_.TraceComment
            ResidualNote = [string]$_.ResidualNote
        }
    }
)

$reviewSheetRows = @(
    $qcItems | ForEach-Object {
        $profile = Get-RoundPenActionProfile -Status ([string]$_.Status)
        [pscustomobject]@{
            ReviewBucket = if ((Normalize-RoundPenStatus ([string]$_.Status)) -eq 'CUT_OK') { 'OK' } else { 'REVIEW_REQUIRED' }
            ReviewPriority = [string]$profile.ReviewPriority
            SuggestedAction = [string]$profile.SuggestedAction
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

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    DocumentTitle = [string]$doc.Title
    DocumentKey = $docKey
    ExecuteRequested = [bool]$Execute
    PlanCount = [int]$plan.Count
    PlanCreatableCount = [int]$plan.CreatableCount
    PlanExistingCount = [int]$plan.ExistingCount
    PlanResidualCount = [int]$plan.ResidualCount
    PreviewIssueCount = if ($null -ne $preview -and $null -ne $preview.ReviewSummary) { [int]$preview.ReviewSummary.IssueCount } else { 0 }
    ExecuteChangedCount = if ($null -ne $executePayload -and $null -ne $executePayload.ChangedIds) { @($executePayload.ChangedIds).Count } else { 0 }
    QcCount = [int]$qc.Count
    QcPlacedCount = [int]$qc.PlacedCount
    QcCutSuccessCount = [int]$qc.CutSuccessCount
    QcResidualCount = [int]$qc.ResidualCount
    QcOrphanCount = [int]$qc.OrphanCount
    QcStatusCounts = $statusCounts
    ReviewSheetPath = (Join-Path $artifactDir 'review-sheet.csv')
}

Write-JsonFile -Path (Join-Path $artifactDir 'document.response.json') -Data $documentResponse
Write-JsonFile -Path (Join-Path $artifactDir 'document.json') -Data $doc
Write-JsonFile -Path (Join-Path $artifactDir 'plan-request.json') -Data $planRequest
Write-JsonFile -Path (Join-Path $artifactDir 'batch-request.json') -Data $batchRequest
Write-JsonFile -Path (Join-Path $artifactDir 'qc-request.json') -Data $qcRequest
Write-JsonFile -Path (Join-Path $artifactDir 'plan.response.json') -Data $planResponse
Write-JsonFile -Path (Join-Path $artifactDir 'plan.json') -Data $plan
Write-JsonFile -Path (Join-Path $artifactDir 'preview.response.json') -Data $previewResponse
Write-JsonFile -Path (Join-Path $artifactDir 'preview.json') -Data $preview
if ($null -ne $preview -and $null -ne $preview.ResolvedContext) {
    Write-JsonFile -Path (Join-Path $artifactDir 'preview-context.json') -Data $preview.ResolvedContext
}
if ($null -ne $executeResponse) {
    Write-JsonFile -Path (Join-Path $artifactDir 'execute.response.json') -Data $executeResponse
}
if ($null -ne $executePayload) {
    Write-JsonFile -Path (Join-Path $artifactDir 'execute.json') -Data $executePayload
}
Write-JsonFile -Path (Join-Path $artifactDir 'qc.response.json') -Data $qcResponse
Write-JsonFile -Path (Join-Path $artifactDir 'qc.json') -Data $qc
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary

Export-CsvOrBlank -Path (Join-Path $artifactDir 'plan-items.csv') -Rows $planRows
Export-CsvOrBlank -Path (Join-Path $artifactDir 'qc-items.csv') -Rows $qcRows
Export-CsvOrBlank -Path (Join-Path $artifactDir 'review-sheet.csv') -Rows $reviewSheetRows

$summary | ConvertTo-Json -Depth 20
