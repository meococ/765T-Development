param(
    [string]$WorkerHostExe = "",
    [switch]$ProbePublicPipe,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$WorkerHostExe = Resolve-WorkerHostExe -RequestedPath $WorkerHostExe
$args = @('--health-json')
if ($ProbePublicPipe) {
    $args += '--probe-public-pipe'
}

$raw = & $WorkerHostExe @args
$exitCode = $LASTEXITCODE
if ([string]::IsNullOrWhiteSpace($raw)) {
    throw "WorkerHost khong tra ve payload health."
}

$health = $raw | ConvertFrom-Json

if ($AsJson) {
    $raw
}
else {
    [pscustomobject]@{
        WorkerHostExe = $WorkerHostExe
        Ready = [bool]$health.Ready
        Degraded = [bool]$health.Degraded
        PublicPipeName = [string]$health.PublicPipeName
        KernelPipeName = [string]$health.KernelPipeName
        EventCount = [int64]$health.Store.EventCount
        PendingOutboxCount = [int64]$health.Store.PendingOutboxCount
        ProcessingOutboxCount = [int64]$health.Store.ProcessingOutboxCount
        CompletedOutboxCount = [int64]$health.Store.CompletedOutboxCount
        FailedOutboxCount = [int64]$health.Store.FailedOutboxCount
        DeadLetterOutboxCount = [int64]$health.Store.DeadLetterOutboxCount
        IgnoredOutboxCount = [int64]$health.Store.IgnoredOutboxCount
        BackoffPendingOutboxCount = [int64]$health.Store.BackoffPendingOutboxCount
        MemoryProjectionCount = [int64]$health.Store.MemoryProjectionCount
        MigrationCount = [int64]$health.Store.MigrationCount
        QdrantReachable = [bool]$health.Qdrant.Reachable
        KernelReachable = [bool]$health.Kernel.Reachable
        PublicControlPlaneReachable = [bool]$health.PublicControlPlane.Reachable
        Diagnostics = @($health.Diagnostics)
    } | Format-List
}

exit $exitCode
