param(
    [string]$PackageRoot = "",
    [string]$CompanionRoot = "",
    [string]$QdrantExe = "",
    [switch]$InstallWorkerHostService,
    [switch]$InstallQdrantStartupTask,
    [switch]$StartWorkerHost,
    [switch]$StartQdrantTask,
    [switch]$OverwriteExisting,
    [switch]$ForceConfig,
    [ValidateSet('LocalSystem', 'LocalService', 'NetworkService')]
    [string]$WorkerHostServiceAccount = "LocalSystem"
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Target
    )

    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Copy-Item (Join-Path $Source '*') $Target -Recurse -Force
}

$repoRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    try {
        $PackageRoot = Resolve-PackagedBuildRoot
    }
    catch {
        $PackageRoot = $repoRoot
    }
}
else {
    $PackageRoot = (Resolve-Path $PackageRoot).Path
}

if ([string]::IsNullOrWhiteSpace($CompanionRoot)) {
    $CompanionRoot = Join-Path $env:LOCALAPPDATA 'Programs\BIM765T.Revit.Agent\Companion'
}

$sourceWorkerHostDir = if (Test-Path (Join-Path $PackageRoot 'WorkerHost\BIM765T.Revit.WorkerHost.exe')) {
    Join-Path $PackageRoot 'WorkerHost'
}
else {
    Join-Path $repoRoot 'src\BIM765T.Revit.WorkerHost\bin\Release\net8.0'
}

if (-not (Test-Path (Join-Path $sourceWorkerHostDir 'BIM765T.Revit.WorkerHost.exe'))) {
    throw "Khong tim thay WorkerHost build output trong package/repo."
}

$sourceToolsDir = if (Test-Path (Join-Path $PackageRoot 'Companion\Tools')) {
    Join-Path $PackageRoot 'Companion\Tools'
}
else {
    Join-Path $repoRoot 'tools'
}

$targetWorkerHostDir = Join-Path $CompanionRoot 'WorkerHost'
$targetToolsDir = Join-Path $CompanionRoot 'Tools'
$targetQdrantDir = Join-Path $CompanionRoot 'Qdrant'

if ($OverwriteExisting -and (Test-Path $CompanionRoot)) {
    Remove-Item -Path $CompanionRoot -Recurse -Force
}

Copy-DirectoryContents -Source $sourceWorkerHostDir -Target $targetWorkerHostDir
Copy-DirectoryContents -Source $sourceToolsDir -Target $targetToolsDir

$resolvedQdrantExe = $null
if (-not [string]::IsNullOrWhiteSpace($QdrantExe)) {
    if (-not (Test-Path $QdrantExe)) {
        throw "Qdrant exe khong ton tai: $QdrantExe"
    }

    New-Item -ItemType Directory -Force -Path $targetQdrantDir | Out-Null
    $resolvedQdrantExe = (Resolve-Path $QdrantExe).Path
    Copy-Item $resolvedQdrantExe (Join-Path $targetQdrantDir 'qdrant.exe') -Force
    $resolvedQdrantExe = Join-Path $targetQdrantDir 'qdrant.exe'
}
elseif (Test-Path (Join-Path $PackageRoot 'Companion\Qdrant\qdrant.exe')) {
    New-Item -ItemType Directory -Force -Path $targetQdrantDir | Out-Null
    Copy-Item (Join-Path $PackageRoot 'Companion\Qdrant\qdrant.exe') (Join-Path $targetQdrantDir 'qdrant.exe') -Force
    $resolvedQdrantExe = Join-Path $targetQdrantDir 'qdrant.exe'
}

$workerHostExe = Join-Path $targetWorkerHostDir 'BIM765T.Revit.WorkerHost.exe'
$workerHostInstall = $null
$qdrantInstall = $null

if ($InstallWorkerHostService) {
    $workerHostInstallArgs = @{
        WorkerHostExe = $workerHostExe
        ServiceAccount = $WorkerHostServiceAccount
    }
    if ($OverwriteExisting) {
        $workerHostInstallArgs.OverwriteExisting = $true
    }
    if ($StartWorkerHost) {
        $workerHostInstallArgs.StartAfterInstall = $true
    }

    $workerHostInstall = & (Join-Path $targetToolsDir 'install_workerhost_service.ps1') @workerHostInstallArgs | ConvertFrom-Json
}

if ($InstallQdrantStartupTask) {
    if (-not $resolvedQdrantExe -or -not (Test-Path $resolvedQdrantExe)) {
        throw "Khong the cai Qdrant startup task vi chua co qdrant.exe trong companion root."
    }

    $qdrantInstallArgs = @{
        QdrantExe = $resolvedQdrantExe
    }
    if ($OverwriteExisting) {
        $qdrantInstallArgs.OverwriteExisting = $true
    }
    if ($StartQdrantTask) {
        $qdrantInstallArgs.StartNow = $true
    }
    if ($ForceConfig) {
        $qdrantInstallArgs.ForceConfig = $true
    }

    $qdrantInstall = & (Join-Path $targetToolsDir 'install_qdrant_startup_task.ps1') @qdrantInstallArgs | ConvertFrom-Json
}

[pscustomobject]@{
    PackageRoot = $PackageRoot
    CompanionRoot = $CompanionRoot
    WorkerHostExe = $workerHostExe
    QdrantExe = if ($resolvedQdrantExe) { $resolvedQdrantExe } else { '' }
    InstalledWorkerHostService = [bool]$InstallWorkerHostService
    InstalledQdrantStartupTask = [bool]$InstallQdrantStartupTask
    WorkerHostInstall = $workerHostInstall
    QdrantInstall = $qdrantInstall
} | ConvertTo-Json -Depth 10
