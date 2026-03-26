param(
    [string]$BridgeExe = "",
    [switch]$IncludeAlignedItems,
    [switch]$NoHighlight
)

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$payload = @{
    AngleToleranceDegrees = 5.0
    TreatMirroredAsMismatch = $true
    TreatAntiParallelAsMismatch = $false
    HighlightInUi = (-not $NoHighlight)
    IncludeAlignedItems = [bool]$IncludeAlignedItems
    MaxElements = 2000
    MaxIssues = 200
    ZoomToHighlighted = $false
    AnalyzeNestedFamilies = $true
    MaxFamilyDefinitionsToInspect = 150
    MaxNestedInstancesPerFamily = 200
    MaxNestedFindingsPerFamily = 20
    TreatNonSharedNestedAsRisk = $true
    TreatNestedMirroredAsRisk = $true
    TreatNestedRotatedAsRisk = $true
    TreatNestedTiltedAsRisk = $true
    IncludeNestedFindings = $false
} | ConvertTo-Json -Depth 20

$tmp = Join-Path $env:TEMP ("bim765t_family_axis_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
Set-Content -Path $tmp -Value $payload -Encoding UTF8
try {
    & $BridgeExe 'review.family_axis_alignment' --payload $tmp
}
finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}
