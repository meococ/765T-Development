param(
    [string]$WorkerHostExe = "",
    [string[]]$ExtraArgs = @(),
    [string]$LogRoot = "",
    [switch]$Foreground
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$WorkerHostExe = Resolve-WorkerHostExe -RequestedPath $WorkerHostExe

if ([string]::IsNullOrWhiteSpace($LogRoot)) {
    $LogRoot = Join-Path $projectRoot 'artifacts\workerhost\logs'
}

New-Item -ItemType Directory -Force -Path $LogRoot | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stdout = Join-Path $LogRoot "workerhost-$stamp.out.log"
$stderr = Join-Path $LogRoot "workerhost-$stamp.err.log"

if ($Foreground) {
    & $WorkerHostExe @ExtraArgs
    exit $LASTEXITCODE
}

$startParams = @{
    FilePath = $WorkerHostExe
    WorkingDirectory = (Split-Path $WorkerHostExe -Parent)
    RedirectStandardOutput = $stdout
    RedirectStandardError = $stderr
    PassThru = $true
}

if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $startParams.ArgumentList = $ExtraArgs
}

$process = Start-Process @startParams

[pscustomobject]@{
    WorkerHostExe = $WorkerHostExe
    ProcessId = $process.Id
    StartedUtc = [DateTime]::UtcNow.ToString('O')
    StdOutLog = $stdout
    StdErrLog = $stderr
    Arguments = @($ExtraArgs)
} | ConvertTo-Json -Depth 10
