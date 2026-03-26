param(
    [string]$BridgeExe = ""
)

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function Invoke-BridgeJson {
    param(
        [string]$Tool,
        [string]$TargetDocument = "",
        [string]$PayloadFile = ""
    )

    $args = @($Tool)
    if ($TargetDocument) { $args += @('--target-document', $TargetDocument) }
    if ($PayloadFile) { $args += @('--payload', $PayloadFile) }

    $raw = & $BridgeExe @args
    if (-not $raw) {
        throw "Bridge tra ve rong cho tool $Tool"
    }

    return $raw | ConvertFrom-Json
}

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

$active = Invoke-BridgeJson -Tool 'document.get_active'
if (-not $active.Succeeded) { throw "document.get_active failed: $($active.StatusCode)" }
$doc = $active.PayloadJson | ConvertFrom-Json

$view = Invoke-BridgeJson -Tool 'view.get_active_context'
if (-not $view.Succeeded) { throw "view.get_active_context failed: $($view.StatusCode)" }

$cap = Invoke-BridgeJson -Tool 'session.get_capabilities'
if (-not $cap.Succeeded) { throw "session.get_capabilities failed: $($cap.StatusCode)" }

$review = Invoke-BridgeJson -Tool 'review.active_view_summary' -TargetDocument $doc.DocumentKey
if (-not $review.Succeeded) { throw "review.active_view_summary failed: $($review.StatusCode)" }

[pscustomobject]@{
    BridgeExe = $BridgeExe
    DocumentTitle = $doc.Title
    DocumentKey = $doc.DocumentKey
    ViewName = (($view.PayloadJson | ConvertFrom-Json).ViewName)
    WriteEnabled = (($cap.PayloadJson | ConvertFrom-Json).AllowWriteTools)
    ReviewStatus = $review.StatusCode
} | ConvertTo-Json -Depth 10
