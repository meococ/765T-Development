param(
    [string]$BridgeExe = "",
    [switch]$ExportImage
)

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function Invoke-Bridge {
    param(
        [string]$Tool,
        [string]$TargetDocument = "",
        [object]$Payload = $null
    )

    $args = @($Tool)
    if ($TargetDocument) { $args += @('--target-document', $TargetDocument) }

    $tempPayload = $null
    if ($null -ne $Payload) {
        $tempPayload = [System.IO.Path]::GetTempFileName() + ".json"
        $Payload | ConvertTo-Json -Depth 30 | Set-Content -Path $tempPayload -Encoding UTF8
        $args += @('--payload', $tempPayload)
    }

    try {
        $raw = & $BridgeExe @args
        if (-not $raw) { throw "Bridge tra ve rong cho tool $Tool" }
        return $raw | ConvertFrom-Json
    }
    finally {
        if ($tempPayload -and (Test-Path $tempPayload)) {
            Remove-Item $tempPayload -Force -ErrorAction SilentlyContinue
        }
    }
}

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

$active = Invoke-Bridge -Tool 'document.get_active'
if (-not $active.Succeeded) { throw "document.get_active failed: $($active.StatusCode)" }
$doc = $active.PayloadJson | ConvertFrom-Json

$sheetQuery = Invoke-Bridge -Tool 'element.query' -TargetDocument $doc.DocumentKey -Payload @{
    ViewScopeOnly = $false
    ClassName = 'ViewSheet'
    MaxResults = 5
}
if (-not $sheetQuery.Succeeded) { throw "element.query failed: $($sheetQuery.StatusCode)" }
$sheetItems = ($sheetQuery.PayloadJson | ConvertFrom-Json).Items
if (-not $sheetItems -or $sheetItems.Count -eq 0) {
    throw "Khong tim thay ViewSheet nao trong document hien tai."
}

$sheetId = $sheetItems[0].ElementId

$workset = Invoke-Bridge -Tool 'review.workset_health' -TargetDocument $doc.DocumentKey
$sheetSummary = Invoke-Bridge -Tool 'review.sheet_summary' -TargetDocument $doc.DocumentKey -Payload @{
    SheetId = $sheetId
    RequiredParameterNames = @('Sheet Number', 'Sheet Name')
}
$sheetRule = Invoke-Bridge -Tool 'review.run_rule_set' -TargetDocument $doc.DocumentKey -Payload @{
    RuleSetName = 'sheet_qc_v1'
    SheetId = $sheetId
    RequiredParameterNames = @('Sheet Number', 'Sheet Name')
}
$snapshot = Invoke-Bridge -Tool 'review.capture_snapshot' -TargetDocument $doc.DocumentKey -Payload @{
    Scope = 'sheet'
    SheetId = $sheetId
    IncludeParameters = $false
    MaxElements = 50
    ExportImage = [bool]$ExportImage
}

[pscustomobject]@{
    BridgeExe = $BridgeExe
    DocumentTitle = $doc.Title
    SheetId = $sheetId
    WorksetStatus = $workset.StatusCode
    SheetSummaryStatus = $sheetSummary.StatusCode
    SheetRuleStatus = $sheetRule.StatusCode
    SnapshotStatus = $snapshot.StatusCode
    SnapshotArtifacts = if ($snapshot.Artifacts) { $snapshot.Artifacts } else { @() }
} | ConvertTo-Json -Depth 20
