param(
    [string]$RepoRoot = ""
)

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

$required = @(
    'packs',
    'workspaces\default\workspace.json',
    'catalog',
    'dist'
)

$missing = @($required | Where-Object { -not (Test-Path (Join-Path $RepoRoot $_)) })
[pscustomobject]@{
    RepoRoot = $RepoRoot
    Missing = $missing
    IsHealthy = ($missing.Count -eq 0)
} | ConvertTo-Json -Compress
