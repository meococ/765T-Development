param(
    [string]$BridgeExe = "",
    [string]$SourceFamilyName = "Penetration Alpha",
    [string]$RoundFamilyName = "Round",
    [string]$PreferredReferenceMark = "test",
    [int]$MaxResults = 5000,
    [switch]$Execute
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

function Invoke-BridgeTool {
    param(
        [string]$Tool,
        [object]$Payload,
        [switch]$DryRun,
        [string]$TargetDocument,
        [string]$ExpectedContext,
        [string]$ApprovalToken
    )

    $tempPayload = [System.IO.Path]::GetTempFileName() + '.json'
    try {
        $Payload | ConvertTo-Json -Depth 20 | Set-Content -Path $tempPayload -Encoding UTF8
        $args = @($Tool)
        if ($TargetDocument) { $args += @('--target-document', $TargetDocument) }
        if ($DryRun) { $args += @('--dry-run', 'true') } else { $args += @('--dry-run', 'false') }
        $args += @('--payload', $tempPayload)
        if ($ExpectedContext) { $args += @('--expected-context', $ExpectedContext) }
        if ($ApprovalToken) { $args += @('--approval-token', $ApprovalToken) }
        (& $BridgeExe @args) | ConvertFrom-Json
    }
    finally {
        Remove-Item $tempPayload -Force -ErrorAction SilentlyContinue
    }
}

$active = (& $BridgeExe 'document.get_active') | ConvertFrom-Json
$doc = $active.PayloadJson | ConvertFrom-Json
$fingerprint = (& $BridgeExe 'document.get_context_fingerprint' --target-document $doc.DocumentKey) | ConvertFrom-Json
$tempContext = [System.IO.Path]::GetTempFileName() + '.json'
try {
    $fingerprint.PayloadJson | Set-Content -Path $tempContext -Encoding UTF8

    $payload = @{
        SourceFamilyName = $SourceFamilyName
        RoundFamilyName = $RoundFamilyName
        PreferredReferenceMark = $PreferredReferenceMark
        MaxResults = $MaxResults
        TraceCommentPrefix = 'BIM765T_SHADOW_ROUND'
        SetCommentsTrace = $true
        CopyDiameter = $true
        CopyLength = $true
        CopyElementClass = $true
        CopyElementTier = $true
        SkipIfTraceExists = $true
        PlacementMode = 'host_face_project_aligned'
        RequireAxisAlignedResult = $true
    }

    $preview = Invoke-BridgeTool -Tool 'batch.create_round_shadow_safe' -Payload $payload -DryRun -TargetDocument $doc.DocumentKey -ExpectedContext $tempContext
    $previewPayload = if ($preview.PayloadJson) { $preview.PayloadJson | ConvertFrom-Json } else { $null }

    [pscustomobject]@{
        PreviewStatus   = $preview.StatusCode
        Confirmation    = $preview.ConfirmationRequired
        ApprovalToken   = $preview.ApprovalToken
        CandidateCount  = if ($previewPayload) { $previewPayload.ChangedIds.Count } else { 0 }
    } | Format-List

    if ($Execute -and $preview.ConfirmationRequired -and $preview.ApprovalToken) {
        $run = Invoke-BridgeTool -Tool 'batch.create_round_shadow_safe' -Payload $payload -TargetDocument $doc.DocumentKey -ExpectedContext $tempContext -ApprovalToken $preview.ApprovalToken
        $runPayload = if ($run.PayloadJson) { $run.PayloadJson | ConvertFrom-Json } else { $null }
        [pscustomobject]@{
            ExecuteStatus = $run.StatusCode
            CreatedCount  = if ($runPayload) { $runPayload.ChangedIds.Count } else { 0 }
            ReviewJson    = $run.ReviewSummaryJson
        } | Format-List
    }
}
finally {
    Remove-Item $tempContext -Force -ErrorAction SilentlyContinue
}
