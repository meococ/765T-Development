param(
    [string]$WorkerHostExe = "",
    [switch]$DryRun,
    [switch]$Force,
    [switch]$IncludeHealth,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$WorkerHostExe = Resolve-WorkerHostExe -RequestedPath $WorkerHostExe
$args = @('--migrate-legacy-state')
if ($DryRun) {
    $args += '--dry-run'
}
if ($Force) {
    $args += '--force-migrate'
}
if ($IncludeHealth) {
    $args += '--health-json'
}

$raw = & $WorkerHostExe @args
$exitCode = $LASTEXITCODE
if ([string]::IsNullOrWhiteSpace($raw)) {
    throw "WorkerHost khong tra ve payload migration."
}

$payload = $raw | ConvertFrom-Json
$migration = if ($IncludeHealth) { $payload.migration } else { $payload }

if ($AsJson) {
    $raw
}
else {
    [pscustomobject]@{
        WorkerHostExe = $WorkerHostExe
        DryRun = [bool]$migration.DryRun
        ForceRequested = [bool]$migration.ForceRequested
        Succeeded = [bool]$migration.Succeeded
        TaskRuns = [pscustomobject]@{
            Scanned = [int]$migration.TaskRuns.Scanned
            WouldImport = [int]$migration.TaskRuns.WouldImport
            Imported = [int]$migration.TaskRuns.Imported
            Skipped = [int]$migration.TaskRuns.Skipped
            Failed = [int]$migration.TaskRuns.Failed
            SampleIds = @($migration.TaskRuns.SampleIds)
        }
        Promotions = [pscustomobject]@{
            Scanned = [int]$migration.Promotions.Scanned
            WouldImport = [int]$migration.Promotions.WouldImport
            Imported = [int]$migration.Promotions.Imported
            Skipped = [int]$migration.Promotions.Skipped
            Failed = [int]$migration.Promotions.Failed
            SampleIds = @($migration.Promotions.SampleIds)
        }
        Episodes = [pscustomobject]@{
            Scanned = [int]$migration.Episodes.Scanned
            WouldImport = [int]$migration.Episodes.WouldImport
            Imported = [int]$migration.Episodes.Imported
            Skipped = [int]$migration.Episodes.Skipped
            Failed = [int]$migration.Episodes.Failed
            SampleIds = @($migration.Episodes.SampleIds)
        }
        QueueItems = [pscustomobject]@{
            Scanned = [int]$migration.QueueItems.Scanned
            WouldImport = [int]$migration.QueueItems.WouldImport
            Imported = [int]$migration.QueueItems.Imported
            Skipped = [int]$migration.QueueItems.Skipped
            Failed = [int]$migration.QueueItems.Failed
            SampleIds = @($migration.QueueItems.SampleIds)
        }
        Errors = @($migration.Errors)
    } | ConvertTo-Json -Depth 10
}

exit $exitCode
