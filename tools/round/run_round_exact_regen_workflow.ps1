param(
    [string]$BridgeExe = "",
    [string]$WrapperFamilySuffix = "_EXACTA",
    [string]$CommentValue = "NEW",
    [switch]$SkipCleanup,
    [string]$CleanupResultsArtifactDir = "",
    [switch]$SkipWrapperQc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host "=== $Label ===" -ForegroundColor Cyan
    & $Action
}

function Build-CommonArgs {
    param([string]$BridgeExePath)
    $args = @()
    if (-not [string]::IsNullOrWhiteSpace($BridgeExePath)) {
        $args += @('-BridgeExe', $BridgeExePath)
    }
    return $args
}

$commonArgs = Build-CommonArgs -BridgeExePath $BridgeExe
$artifactDir = Join-Path $projectRoot ("artifacts\\round-exact-regen-workflow\\{0}" -f ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$stepOutputs = [ordered]@{}

if (-not $SkipCleanup.IsPresent) {
    Invoke-Step -Label 'Cleanup previous bad NEW batch' -Action {
        $cleanupArgs = @()
        $cleanupArgs += $commonArgs
        if (-not [string]::IsNullOrWhiteSpace($CleanupResultsArtifactDir)) {
            $cleanupArgs += @('-ResultsArtifactDir', $CleanupResultsArtifactDir)
        }

        $cleanupOutput = & (Join-Path $PSScriptRoot 'cleanup_round_externalization_batch.ps1') @cleanupArgs
        $stepOutputs.Cleanup = ($cleanupOutput | ConvertFrom-Json)
    }
}

Invoke-Step -Label 'Build exact-size wrapper families' -Action {
    $buildArgs = @()
    $buildArgs += $commonArgs
    $buildArgs += @('-WrapperFamilySuffix', $WrapperFamilySuffix, '-GenerateSizeSpecificVariants')
    $buildOutput = & (Join-Path $PSScriptRoot 'build_round_project_wrappers.ps1') @buildArgs
    $stepOutputs.BuildWrappers = ($buildOutput | ConvertFrom-Json)
}

Invoke-Step -Label 'Externalize Round using exact-size wrappers' -Action {
    $execArgs = @()
    $execArgs += $commonArgs
    $execArgs += @('-WrapperFamilySuffix', $WrapperFamilySuffix, '-UseSizeSpecificVariants', '-SkipAxisAudit', '-CommentValue', $CommentValue)
    $execOutput = & (Join-Path $PSScriptRoot 'externalize_round_from_plan.ps1') @execArgs
    $stepOutputs.Externalize = ($execOutput | ConvertFrom-Json)
}

Invoke-Step -Label 'Pair/QC comments + schedule review' -Action {
    $resultsArtifactDir = [string]$stepOutputs.Externalize.ArtifactDirectory
    $reconcileArgs = @()
    $reconcileArgs += $commonArgs
    $reconcileArgs += @('-ResultsArtifactDir', $resultsArtifactDir)
    $qcOutput = & (Join-Path $PSScriptRoot 'reconcile_round_pairs_and_comments.ps1') @reconcileArgs
    $stepOutputs.Reconcile = ($qcOutput | ConvertFrom-Json)
}

if (-not $SkipWrapperQc.IsPresent) {
    Invoke-Step -Label 'Wrapper-level QC + wrapper review schedule' -Action {
        $resultsArtifactDir = [string]$stepOutputs.Externalize.ArtifactDirectory
        $wrapperQcArgs = @()
        $wrapperQcArgs += $commonArgs
        $wrapperQcArgs += @('-ResultsArtifactDir', $resultsArtifactDir)
        $wrapperQcOutput = & (Join-Path $PSScriptRoot 'reconcile_round_wrapper_qc.ps1') @wrapperQcArgs
        $stepOutputs.WrapperQc = ($wrapperQcOutput | ConvertFrom-Json)
    }
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    WrapperFamilySuffix = $WrapperFamilySuffix
    CommentValue = $CommentValue
    Steps = $stepOutputs
}

$summary | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $artifactDir 'summary.json') -Encoding UTF8
$summary | ConvertTo-Json -Depth 100
