param(
    [string]$Greeting = "Chào anh - em là 765T Revit Bridge",
    [string]$BridgeExe = ""
)

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function Invoke-Bridge {
    param(
        [string]$Tool,
        [string]$PayloadJson = "",
        [string]$TargetDocument = "",
        [string]$ExpectedContextJson = "",
        [string]$ApprovalToken = "",
        [bool]$DryRun = $true
    )

    $args = @($Tool, '--dry-run', $DryRun.ToString().ToLowerInvariant())
    if ($PayloadJson) { $args += @('--payload', $PayloadJson) }
    if ($TargetDocument) { $args += @('--target-document', $TargetDocument) }
    if ($ApprovalToken) { $args += @('--approval-token', $ApprovalToken) }
    if ($ExpectedContextJson) { $args += @('--expected-context', $ExpectedContextJson) }

    $raw = & $BridgeExe @args
    return $raw | ConvertFrom-Json
}

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

$active = Invoke-Bridge -Tool 'document.get_active' -DryRun $true
if (-not $active.Succeeded) { throw "document.get_active failed: $($active.StatusCode)" }
$activeDoc = $active.PayloadJson | ConvertFrom-Json
$docKey = $activeDoc.DocumentKey

$fingerprintResponse = Invoke-Bridge -Tool 'document.get_context_fingerprint' -TargetDocument $docKey -DryRun $true
if (-not $fingerprintResponse.Succeeded) { throw "document.get_context_fingerprint failed: $($fingerprintResponse.StatusCode)" }
$expectedContext = $fingerprintResponse.PayloadJson

$payload = @{ Text = $Greeting; UseViewCenterWhenPossible = $true } | ConvertTo-Json -Compress
$payloadFile = Join-Path $env:TEMP ("bim765t_textnote_payload_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
$expectedContextFile = Join-Path $env:TEMP ("bim765t_expected_context_{0}.json" -f ([Guid]::NewGuid().ToString('N')))

try {
    Set-Content -Path $payloadFile -Value $payload -Encoding UTF8
    Set-Content -Path $expectedContextFile -Value $expectedContext -Encoding UTF8

    $preview = & $BridgeExe 'annotation.add_text_note_safe' '--target-document' $docKey '--dry-run' 'true' '--payload' $payloadFile '--expected-context' $expectedContextFile
    $previewObj = $preview | ConvertFrom-Json
    if (-not $previewObj.Succeeded) { throw "preview failed: $($previewObj.StatusCode)" }
    if (-not $previewObj.ApprovalToken) { throw 'preview did not return approval token.' }

    $execute = & $BridgeExe 'annotation.add_text_note_safe' '--target-document' $docKey '--dry-run' 'false' '--payload' $payloadFile '--expected-context' $expectedContextFile '--approval-token' $previewObj.ApprovalToken
    $executeObj = $execute | ConvertFrom-Json
    $executeObj | ConvertTo-Json -Depth 20
}
finally {
    Remove-Item $payloadFile, $expectedContextFile -ErrorAction SilentlyContinue
}
