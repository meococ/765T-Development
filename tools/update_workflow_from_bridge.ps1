param(
    [string]$BridgeExe = "",
    [string]$WorkflowPath = ".\revit-agent-workflow.html"
)

if ([string]::IsNullOrWhiteSpace($BridgeExe)) {
    powershell -ExecutionPolicy Bypass -File ".\tools\generate_tool_catalog.ps1" | Out-Null
    powershell -ExecutionPolicy Bypass -File ".\tools\sync_workflow_from_catalog.ps1" -WorkflowPath $WorkflowPath | Out-Null
}
else {
    powershell -ExecutionPolicy Bypass -File ".\tools\generate_tool_catalog.ps1" -BridgeExe $BridgeExe | Out-Null
    powershell -ExecutionPolicy Bypass -File ".\tools\sync_workflow_from_catalog.ps1" -BridgeExe $BridgeExe -WorkflowPath $WorkflowPath | Out-Null
}

[pscustomobject]@{
    WorkflowPath = (Resolve-Path $WorkflowPath).Path
    CatalogJson = (Resolve-Path ".\docs\generated\revit-tool-catalog.json").Path
    CatalogMarkdown = (Resolve-Path ".\docs\generated\revit-tool-catalog.md").Path
} | ConvertTo-Json -Compress
