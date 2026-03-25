$ErrorActionPreference = "Stop"

param(
    [string]$BridgeExe = ""
)

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

$active = (& $BridgeExe document.get_active | ConvertFrom-Json)
if (-not $active.Succeeded)
{
    throw "document.get_active failed: $($active.StatusCode)"
}

$doc = $active.PayloadJson | ConvertFrom-Json

Write-Host "Document: $($doc.Title)" -ForegroundColor Cyan

$textTypes = (& $BridgeExe annotation.list_text_note_types --target-document $doc.DocumentKey | ConvertFrom-Json)
$textUsage = (& $BridgeExe annotation.get_text_type_usage --target-document $doc.DocumentKey | ConvertFrom-Json)

[PSCustomObject]@{
    DocumentTitle    = $doc.Title
    TextTypeStatus   = $textTypes.StatusCode
    TextTypeCount    = (($textTypes.PayloadJson | ConvertFrom-Json).Count)
    UsageStatus      = $textUsage.StatusCode
    UsageCount       = (($textUsage.PayloadJson | ConvertFrom-Json).Count)
} | ConvertTo-Json -Depth 10
