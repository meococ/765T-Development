param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$repoRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$issues = New-Object System.Collections.Generic.List[string]

function Add-Issue {
    param([string]$Message)
    $issues.Add($Message) | Out-Null
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseFullPath = [IO.Path]::GetFullPath($BasePath).TrimEnd('\')
    $targetFullPath = [IO.Path]::GetFullPath($TargetPath)

    if ($targetFullPath.StartsWith($baseFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relative = $targetFullPath.Substring($baseFullPath.Length).TrimStart('\')
        if (-not [string]::IsNullOrWhiteSpace($relative)) {
            return $relative.Replace('\', '/')
        }
    }

    $baseUri = [Uri]($baseFullPath + '\')
    $targetUri = [Uri]$targetFullPath
    return $baseUri.MakeRelativeUri($targetUri).OriginalString.Replace('\', '/')
}

$forbiddenPaths = @(
    'AGENTS.md',
    'ASSISTANT.md',
    'CLAUDE.md',
    '.assistant',
    '.claude',
    'README.BIM765T.Revit.Agent.md',
    'docs/agent',
    'docs/architecture',
    'docs/ba',
    'docs/archive',
    'docs/assets',
    'docs/assistant',
    'docs/765T_BLUEPRINT.md',
    'docs/765T_CRITICAL_REVIEW.md',
    'docs/765T_PRODUCT_VISION.md',
    'docs/765T_SYSTEM_DIAGRAMS.md',
    'docs/765T_TECHNICAL_RESEARCH.md',
    'docs/765T_TOOL_LIBRARY_BLUEPRINT.md',
    'docs/ARCHITECTURE.md',
    'docs/PATTERNS.md',
    'docs/INDEX.md',
    'docs/PRODUCT_REVIEW.md'
)

foreach ($relativePath in $forbiddenPaths) {
    $fullPath = Join-Path $repoRoot ($relativePath -replace '/', '\')
    if (Test-Path -LiteralPath $fullPath) {
        Add-Issue "Forbidden path still present: $relativePath"
    }
}

$docsRoot = Join-Path $repoRoot 'docs'
$allowedDocTopLevels = @('integration', 'reference', 'troubleshooting', 'release')
if (-not (Test-Path -LiteralPath (Join-Path $docsRoot 'README.md'))) {
    Add-Issue 'Missing docs/README.md'
}

if (Test-Path -LiteralPath $docsRoot) {
    foreach ($file in Get-ChildItem -LiteralPath $docsRoot -File -Recurse) {
        $relative = Get-RelativePath -BasePath $docsRoot -TargetPath $file.FullName
        if ($relative -eq 'README.md') {
            continue
        }

        $topLevel = $relative.Split('/')[0]
        if ($allowedDocTopLevels -notcontains $topLevel) {
            Add-Issue "Doc is outside allowed product categories: docs/$relative"
        }
    }
}

$contentFiles = New-Object System.Collections.Generic.List[string]
$contentFiles.Add((Join-Path $repoRoot 'README.md')) | Out-Null
$contentFiles.Add((Join-Path $repoRoot 'README.en.md')) | Out-Null
$contentFiles.Add((Join-Path $repoRoot 'tools\README.md')) | Out-Null

if (Test-Path -LiteralPath $docsRoot) {
    foreach ($file in Get-ChildItem -LiteralPath $docsRoot -File -Recurse) {
        $contentFiles.Add($file.FullName) | Out-Null
    }
}

$brokerAssets = Join-Path $repoRoot 'packs\agents\external-broker\assets'
if (Test-Path -LiteralPath $brokerAssets) {
    foreach ($file in Get-ChildItem -LiteralPath $brokerAssets -File -Recurse -Include *.md) {
        $contentFiles.Add($file.FullName) | Out-Null
    }
}

$bannedContentPatterns = @(
    'AGENTS.md',
    'ASSISTANT.md',
    'CLAUDE.md',
    'docs/assistant/',
    'docs/ARCHITECTURE.md',
    'docs/PATTERNS.md',
    'docs/INDEX.md',
    'README.BIM765T.Revit.Agent.md',
    'git clone',
    'dotnet build'
)

$bannedExamplePatterns = @(
    'C:\Users\',
    'D:\',
    'Users\ADMIN',
    'GITHUB_PERSONAL_ACCESS_TOKEN',
    '<REPLACE_WITH_',
    'sk-'
)

foreach ($file in ($contentFiles | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $file)) {
        Add-Issue "Expected file is missing: $(Get-RelativePath -BasePath $repoRoot -TargetPath $file)"
        continue
    }

    $content = Get-Content -LiteralPath $file -Raw
    foreach ($pattern in $bannedContentPatterns) {
        if ($content.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Add-Issue "Banned content pattern '$pattern' found in $(Get-RelativePath -BasePath $repoRoot -TargetPath $file)"
        }
    }

    if ($file.StartsWith((Join-Path $docsRoot 'integration\examples'), [System.StringComparison]::OrdinalIgnoreCase)) {
        foreach ($pattern in $bannedExamplePatterns) {
            if ($content.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                Add-Issue "Banned example pattern '$pattern' found in $(Get-RelativePath -BasePath $repoRoot -TargetPath $file)"
            }
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host '[boundary] Product repo boundary validation passed.'
