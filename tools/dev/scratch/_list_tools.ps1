. (Join-Path $PSScriptRoot "Assistant.Common.ps1")
$b = Resolve-BridgeExe
$r = & $b "session.list_tools" --payload "{}" 2>$null | ConvertFrom-Json
$tools = ($r.PayloadJson | ConvertFrom-Json).Tools
Write-Host "=== Inspector + Query tools ==="
$tools | Where-Object { $_.ToolName -match "explain|inspect|graph|query|trace|placement" } | ForEach-Object {
    Write-Host "  $($_.ToolName)"
}
Write-Host ""
Write-Host "=== Element tools ==="
$tools | Where-Object { $_.ToolName -like "element.*" } | ForEach-Object {
    Write-Host "  $($_.ToolName)"
}
