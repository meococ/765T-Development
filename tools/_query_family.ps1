param([string]$FamilyName = "Mii_Pen-Rectangle")
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe

function Invoke-BridgeTool([string]$tool, [string]$payload) {
    $raw = & $bridge $tool --payload $payload 2>$null
    if (-not $raw) { return $null }
    return $raw | ConvertFrom-Json
}

Write-Host "=== Query family: $FamilyName ===" -ForegroundColor Cyan

$payload = "{`"filter_family_name`":`"$FamilyName`",`"include_type_info`":true,`"include_location`":true,`"max_results`":50}"
$result = Invoke-BridgeTool 'element.query' $payload
$result | ConvertTo-Json -Depth 10
