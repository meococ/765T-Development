param(
    [string]$Configuration = "Release",
    [string]$TargetRoot = "",
    [string]$RevitYear = "2024",
    [switch]$InstallManifest,
    [string]$DeployRoot = "",
    [switch]$SkipShadowCopy,
    [string]$QdrantExe = ""
)

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function Reset-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    New-Item -ItemType Directory -Force -Path $Path | Out-Null

    Get-ChildItem $Path -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction Stop
}

if ([string]::IsNullOrWhiteSpace($TargetRoot)) {
    $TargetRoot = Resolve-DefaultPackageTargetRoot
}

$agentSource = Join-Path $repoRoot "src\BIM765T.Revit.Agent\bin\$Configuration"
$bridgeSource = Join-Path $repoRoot "src\BIM765T.Revit.Bridge\bin\$Configuration\net8.0"
$mcpSource = Join-Path $repoRoot "src\BIM765T.Revit.McpHost\bin\$Configuration\net8.0"
$workerHostSource = Join-Path $repoRoot "src\BIM765T.Revit.WorkerHost\bin\$Configuration\net8.0"

$agentTarget = Join-Path $TargetRoot "Agent"
$bridgeTarget = Join-Path $TargetRoot "Bridge"
$mcpTarget = Join-Path $TargetRoot "McpHost"
$workerHostTarget = Join-Path $TargetRoot "WorkerHost"
$companionTarget = Join-Path $TargetRoot "Companion"
$companionToolsTarget = Join-Path $companionTarget "Tools"
$companionQdrantTarget = Join-Path $companionTarget "Qdrant"

foreach ($path in @($agentSource, $bridgeSource, $mcpSource, $workerHostSource)) {
    if (-not (Test-Path $path)) {
        throw "Build output not found: $path"
    }
}

foreach ($path in @($agentTarget, $bridgeTarget, $mcpTarget, $workerHostTarget, $companionTarget)) {
    Reset-Directory -Path $path
}

Copy-Item (Join-Path $agentSource '*') $agentTarget -Recurse -Force
Copy-Item (Join-Path $bridgeSource '*') $bridgeTarget -Recurse -Force
Copy-Item (Join-Path $mcpSource '*') $mcpTarget -Recurse -Force
Copy-Item (Join-Path $workerHostSource '*') $workerHostTarget -Recurse -Force

$companionScripts = @(
    'Assistant.Common.ps1',
    'start_workerhost.ps1',
    'check_workerhost_health.ps1',
    'start_qdrant_local.ps1',
    'install_workerhost_service.ps1',
    'uninstall_workerhost_service.ps1',
    'install_qdrant_startup_task.ps1',
    'uninstall_qdrant_startup_task.ps1',
    'migrate_workerhost_legacy_state.ps1',
    'install_enterprise_companion.ps1'
)

New-Item -ItemType Directory -Force -Path $companionToolsTarget | Out-Null
foreach ($scriptName in $companionScripts) {
    $sourceScript = Join-Path $repoRoot "tools\$scriptName"
    if (-not (Test-Path $sourceScript)) {
        throw "Companion tool script not found: $sourceScript"
    }

    Copy-Item $sourceScript (Join-Path $companionToolsTarget $scriptName) -Force
}

$resolvedQdrantExe = $null
if (-not [string]::IsNullOrWhiteSpace($QdrantExe)) {
    if (-not (Test-Path $QdrantExe)) {
        throw "Qdrant exe not found: $QdrantExe"
    }

    New-Item -ItemType Directory -Force -Path $companionQdrantTarget | Out-Null
    $resolvedQdrantExe = (Resolve-Path $QdrantExe).Path
    Copy-Item $resolvedQdrantExe (Join-Path $companionQdrantTarget 'qdrant.exe') -Force
}

$manifestPath = Join-Path $companionTarget 'companion.manifest.json'
[pscustomobject]@{
    GeneratedUtc = [DateTime]::UtcNow.ToString('O')
    WorkerHostExe = 'WorkerHost\BIM765T.Revit.WorkerHost.exe'
    QdrantExe = if ($resolvedQdrantExe) { 'Companion\Qdrant\qdrant.exe' } else { '' }
    WorkerHostServiceName = 'BIM765T.Revit.WorkerHost'
    QdrantTaskName = 'BIM765T.Qdrant.Local'
    IncludedScripts = $companionScripts
} | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding UTF8

$requiredAgentFiles = @(
    'BIM765T.Revit.Agent.dll',
    'BIM765T.Revit.Contracts.dll'
)

$missing = @($requiredAgentFiles | Where-Object { -not (Test-Path (Join-Path $agentTarget $_)) })
if ($missing.Count -gt 0) {
    throw "Agent package is missing required files: $($missing -join ', ')"
}

$installResult = $null
if ($InstallManifest) {
    $installArgs = @{
        Configuration = $Configuration
        RevitYear = $RevitYear
        AssemblyPath = (Join-Path $agentTarget 'BIM765T.Revit.Agent.dll')
    }

    if (-not [string]::IsNullOrWhiteSpace($DeployRoot)) {
        $installArgs.DeployRoot = $DeployRoot
    }

    if ($SkipShadowCopy) {
        $installArgs.SkipShadowCopy = $true
    }

    $installResult = & (Join-Path $repoRoot 'src\BIM765T.Revit.Agent\deploy\install-addin.ps1') @installArgs | ConvertFrom-Json
}

[pscustomobject]@{
    TargetRoot = $TargetRoot
    AgentFiles = (Get-ChildItem $agentTarget | Select-Object -ExpandProperty Name)
    BridgeFiles = (Get-ChildItem $bridgeTarget | Select-Object -ExpandProperty Name | Select-Object -First 5)
    McpFiles = (Get-ChildItem $mcpTarget | Select-Object -ExpandProperty Name | Select-Object -First 5)
    WorkerHostFiles = (Get-ChildItem $workerHostTarget | Select-Object -ExpandProperty Name | Select-Object -First 5)
    CompanionFiles = (Get-ChildItem $companionTarget | Select-Object -ExpandProperty Name)
    IncludedQdrantExe = [bool]$resolvedQdrantExe
    InstallManifest = [bool]$InstallManifest
    ShadowCopyEnabled = (-not $SkipShadowCopy)
    InstallResult = $installResult
} | ConvertTo-Json -Depth 10
