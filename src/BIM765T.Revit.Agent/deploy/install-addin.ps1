param(
    [string]$Configuration = "Release",
    [string]$RevitYear = "2024",
    [string]$AssemblyPath = "",
    [string]$DeployRoot = "",
    [switch]$SkipShadowCopy
)

function Get-ManifestTagValues {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$TagName
    )

    return @(
        [regex]::Matches($Content, "<$TagName>\s*([^<]+?)\s*</$TagName>", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) |
            ForEach-Object { $_.Groups[1].Value.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedAssemblyPath = if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    Join-Path $projectRoot "bin\$Configuration\BIM765T.Revit.Agent.dll"
} else {
    $AssemblyPath
}
$templatePath = Join-Path $PSScriptRoot "BIM765T.Revit.Agent.addin.template"
$targetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$targetAddin = Join-Path $targetDir "BIM765T.Revit.Agent.addin"
$agentConfigDir = Join-Path $env:APPDATA "BIM765T.Revit.Agent"
$settingsTarget = Join-Path $agentConfigDir "settings.json"
$policyTarget = Join-Path $agentConfigDir "policy.json"
$sourceDir = Split-Path -Parent $resolvedAssemblyPath
$shadowRoot = if ([string]::IsNullOrWhiteSpace($DeployRoot)) {
    Join-Path $env:LOCALAPPDATA "BIM765T.Revit.Agent\shadow\$RevitYear"
} else {
    $DeployRoot
}

if (-not (Test-Path $resolvedAssemblyPath)) {
    throw "Assembly not found: $resolvedAssemblyPath. Build the solution first."
}

$resolvedAssemblyPath = (Resolve-Path $resolvedAssemblyPath).Path

$manifestAssemblyPath = $resolvedAssemblyPath
$deployedDir = $sourceDir
$archivedConflicts = @()

if (-not $SkipShadowCopy) {
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedAssemblyPath)
    $versionTag = if ([string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) { "0.0.0" } else { $versionInfo.FileVersion }
    $versionTag = ($versionTag -replace '[^0-9A-Za-z\.-]', '-')
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $deployedDir = Join-Path $shadowRoot ("{0}-{1}-{2}" -f $Configuration, $versionTag, $stamp)

    New-Item -ItemType Directory -Force -Path $deployedDir | Out-Null
    Copy-Item (Join-Path $sourceDir '*') $deployedDir -Recurse -Force

    $metadata = [pscustomobject]@{
        SourceAssemblyPath = $resolvedAssemblyPath
        SourceDirectory = $sourceDir
        InstalledAtUtc = [DateTime]::UtcNow.ToString('o')
        RevitYear = $RevitYear
        Configuration = $Configuration
    } | ConvertTo-Json -Depth 10
    Set-Content -Path (Join-Path $deployedDir 'deploy-metadata.json') -Value $metadata -Encoding UTF8

    $manifestAssemblyPath = Join-Path $deployedDir (Split-Path -Leaf $resolvedAssemblyPath)
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$content = Get-Content $templatePath -Raw
$content = $content.Replace('{ASSEMBLY_PATH}', $manifestAssemblyPath)
Set-Content -Path $targetAddin -Value $content -Encoding UTF8

$manifestIds = @(Get-ManifestTagValues -Content $content -TagName 'AddInId')
$manifestClasses = @(Get-ManifestTagValues -Content $content -TagName 'FullClassName')
$manifestDisplayNames = @(
    @(Get-ManifestTagValues -Content $content -TagName 'Name')
    @(Get-ManifestTagValues -Content $content -TagName 'Text')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
$disabledDir = Join-Path $targetDir '_disabled'
$conflictStamp = Get-Date -Format 'yyyyMMdd-HHmmss'

Get-ChildItem $targetDir -Filter *.addin -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $targetAddin } |
    ForEach-Object {
        $otherContent = Get-Content $_.FullName -Raw
        $idHits = @($manifestIds | Where-Object { $otherContent -match [regex]::Escape($_) })
        $classHits = @($manifestClasses | Where-Object { $otherContent -match [regex]::Escape($_) })
        $displayHits = @($manifestDisplayNames | Where-Object { $otherContent -match [regex]::Escape($_) })

        if ($idHits.Count -gt 0 -or $classHits.Count -gt 0 -or $displayHits.Count -gt 0) {
            New-Item -ItemType Directory -Force -Path $disabledDir | Out-Null
            $archivedPath = Join-Path $disabledDir ("{0}.conflict-{1}{2}.disabled" -f $_.BaseName, $conflictStamp, $_.Extension)
            Move-Item $_.FullName $archivedPath -Force
            $archivedConflicts += [pscustomobject]@{
                OriginalPath = $_.FullName
                ArchivedPath = $archivedPath
                MatchingAddInIds = $idHits
                MatchingClasses = $classHits
                MatchingDisplayNames = $displayHits
            }
    }
}

$revitProcess = Get-Process -Name Revit -ErrorAction SilentlyContinue
$revitProcessCount = @($revitProcess).Count
$requiresRevitRestart = $revitProcessCount -gt 0
if ($requiresRevitRestart) {
    Write-Warning "Revit dang chay. Ban build moi se co hieu luc sau khi Revit duoc khoi dong lai."
}

New-Item -ItemType Directory -Force -Path $agentConfigDir | Out-Null
if (-not (Test-Path $settingsTarget)) {
    Copy-Item (Join-Path $PSScriptRoot 'settings.template.json') $settingsTarget -Force
}
if (-not (Test-Path $policyTarget)) {
    Copy-Item (Join-Path $PSScriptRoot 'policy.template.json') $policyTarget -Force
}

[pscustomobject]@{
    AddinManifest = $targetAddin
    AssemblyPath = $manifestAssemblyPath
    DeployDirectory = $deployedDir
    ShadowCopyEnabled = (-not $SkipShadowCopy)
    RevitProcessCount = $revitProcessCount
    RequiresRevitRestart = $requiresRevitRestart
    ArchivedConflicts = $archivedConflicts
} | ConvertTo-Json -Depth 10
