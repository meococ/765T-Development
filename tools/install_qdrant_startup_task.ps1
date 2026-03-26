param(
    [string]$TaskName = "BIM765T.Qdrant.Local",
    [string]$QdrantExe = "",
    [string]$ConfigPath = "",
    [string]$StoragePath = "",
    [string]$SnapshotsPath = "",
    [int]$HttpPort = 6333,
    [int]$GrpcPort = 6334,
    [string]$UserId = "",
    [switch]$ForceConfig,
    [switch]$StartNow,
    [switch]$OverwriteExisting
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function Resolve-QdrantExe {
    param(
        [string]$RequestedPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath) -and (Test-Path $RequestedPath)) {
        return (Resolve-Path $RequestedPath).Path
    }

    $candidates = @(
        $env:QDRANT_EXE,
        (Join-Path $env:APPDATA 'BIM765T.Revit.Agent\companion\qdrant\qdrant.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Qdrant\qdrant.exe')
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Khong tim thay qdrant.exe. Set -QdrantExe hoac env:QDRANT_EXE truoc."
}

$QdrantExe = Resolve-QdrantExe -RequestedPath $QdrantExe
$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)

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
if ([string]::IsNullOrWhiteSpace($UserId)) {
    $UserId = if ([string]::IsNullOrWhiteSpace($env:USERDOMAIN)) {
        $env:USERNAME
    }
    else {
        '{0}\{1}' -f $env:USERDOMAIN, $env:USERNAME
    }
}

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    if (-not $OverwriteExisting) {
        throw "Scheduled task '$TaskName' da ton tai. Dung -OverwriteExisting neu muon tao lai."
    }

    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

$scriptPath = Join-Path $projectRoot 'tools\start_qdrant_local.ps1'
$quotedScriptPath = '"' + $scriptPath + '"'
$quotedQdrantExe = '"' + $QdrantExe + '"'
$quotedConfigPath = '"' + $ConfigPath + '"'
$quotedStoragePath = '"' + $StoragePath + '"'
$quotedSnapshotsPath = '"' + $SnapshotsPath + '"'

$argumentParts = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', $quotedScriptPath,
    '-QdrantExe', $quotedQdrantExe,
    '-ConfigPath', $quotedConfigPath,
    '-StoragePath', $quotedStoragePath,
    '-SnapshotsPath', $quotedSnapshotsPath,
    '-HttpPort', $HttpPort,
    '-GrpcPort', $GrpcPort,
    '-Foreground'
)
if ($ForceConfig) {
    $argumentParts += '-ForceConfig'
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ($argumentParts -join ' ')
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserId
$principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null

if ($StartNow) {
    Start-ScheduledTask -TaskName $TaskName
}

[pscustomobject]@{
    TaskName = $TaskName
    UserId = $UserId
    QdrantExe = $QdrantExe
    ConfigPath = $ConfigPath
    StoragePath = $StoragePath
    SnapshotsPath = $SnapshotsPath
    HttpPort = $HttpPort
    GrpcPort = $GrpcPort
    ForceConfig = [bool]$ForceConfig
    StartNow = [bool]$StartNow
    LaunchCommand = "powershell.exe $($argumentParts -join ' ')"
} | ConvertTo-Json -Depth 10
