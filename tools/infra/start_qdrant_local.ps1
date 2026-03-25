param(
    [string]$QdrantExe = "",
    [string]$ConfigPath = "",
    [string]$StoragePath = "",
    [string]$SnapshotsPath = "",
    [int]$HttpPort = 6333,
    [int]$GrpcPort = 6334,
    [switch]$ForceConfig,
    [switch]$Foreground
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($QdrantExe)) {
    $candidates = @(
        $env:QDRANT_EXE,
        (Join-Path $env:APPDATA 'BIM765T.Revit.Agent\companion\qdrant\qdrant.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Qdrant\qdrant.exe')
    )

    $QdrantExe = $candidates |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($QdrantExe) -or -not (Test-Path $QdrantExe)) {
    throw "Khong tim thay qdrant.exe. Set -QdrantExe hoac env:QDRANT_EXE truoc."
}

$companionRoot = Join-Path $env:APPDATA 'BIM765T.Revit.Agent\companion\qdrant'
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $companionRoot 'config.local.yaml'
}
if ([string]::IsNullOrWhiteSpace($StoragePath)) {
    $StoragePath = Join-Path $companionRoot 'storage'
}
if ([string]::IsNullOrWhiteSpace($SnapshotsPath)) {
    $SnapshotsPath = Join-Path $companionRoot 'snapshots'
}

New-Item -ItemType Directory -Force -Path (Split-Path $ConfigPath -Parent) | Out-Null
New-Item -ItemType Directory -Force -Path $StoragePath | Out-Null
New-Item -ItemType Directory -Force -Path $SnapshotsPath | Out-Null

if ($ForceConfig -or -not (Test-Path $ConfigPath)) {
    $yaml = @"
service:
  http_port: $HttpPort
  grpc_port: $GrpcPort
storage:
  storage_path: "$($StoragePath -replace '\\','/')"
  snapshots_path: "$($SnapshotsPath -replace '\\','/')"
"@
    Set-Content -Path $ConfigPath -Value $yaml -NoNewline
}

$arguments = @('--config-path', $ConfigPath)
if ($Foreground) {
    & $QdrantExe @arguments
    exit $LASTEXITCODE
}

$stdout = Join-Path (Split-Path $ConfigPath -Parent) 'qdrant.out.log'
$stderr = Join-Path (Split-Path $ConfigPath -Parent) 'qdrant.err.log'
$process = Start-Process -FilePath $QdrantExe `
    -ArgumentList $arguments `
    -WorkingDirectory (Split-Path $QdrantExe -Parent) `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru

[pscustomobject]@{
    QdrantExe = (Resolve-Path $QdrantExe).Path
    ProcessId = $process.Id
    ConfigPath = $ConfigPath
    StoragePath = $StoragePath
    SnapshotsPath = $SnapshotsPath
    HttpPort = $HttpPort
    GrpcPort = $GrpcPort
    StdOutLog = $stdout
    StdErrLog = $stderr
} | ConvertTo-Json -Depth 10
