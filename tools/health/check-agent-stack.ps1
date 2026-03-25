param(
    [switch]$AsJson
)

$script = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'audit_agent_stack.ps1'
if (-not (Test-Path $script)) {
    throw "Missing audit_agent_stack.ps1"
}

if ($AsJson) {
    powershell -ExecutionPolicy Bypass -File $script -AsJson
} else {
    powershell -ExecutionPolicy Bypass -File $script
}
