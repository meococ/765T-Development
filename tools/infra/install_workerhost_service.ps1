param(
    [string]$WorkerHostExe = "",
    [string]$ServiceName = "BIM765T.Revit.WorkerHost",
    [string]$DisplayName = "BIM765T Revit WorkerHost",
    [string]$Description = "Public gRPC named-pipe control plane sidecar for BIM765T Revit Agent.",
    [ValidateSet('Automatic', 'Manual')]
    [string]$StartupType = "Automatic",
    [ValidateSet('LocalSystem', 'LocalService', 'NetworkService')]
    [string]$ServiceAccount = "LocalSystem",
    [string[]]$ExtraArgs = @(),
    [switch]$StartAfterInstall,
    [switch]$OverwriteExisting
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if (-not (Test-IsAdministrator)) {
    throw "Script nay can chay PowerShell voi quyen Administrator de cai Windows Service."
}

function Join-CommandArguments {
    param(
        [string[]]$Values
    )

    return ($Values | ForEach-Object {
        if ($_ -match '\s') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }) -join ' '
}

function Wait-ServiceDeletion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    for ($i = 0; $i -lt 30; $i++) {
        if (-not (Get-Service -Name $Name -ErrorAction SilentlyContinue)) {
            return
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Service '$Name' van chua bi xoa sau timeout."
}

$WorkerHostExe = Resolve-WorkerHostExe -RequestedPath $WorkerHostExe
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if (-not $OverwriteExisting) {
        throw "Service '$ServiceName' da ton tai. Dung -OverwriteExisting neu muon cai lai."
    }

    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }

    & sc.exe delete $ServiceName | Out-Null
    Wait-ServiceDeletion -Name $ServiceName
}

$binaryPath = if ($ExtraArgs.Count -gt 0) {
    ('"{0}" {1}' -f $WorkerHostExe, (Join-CommandArguments -Values $ExtraArgs))
}
else {
    ('"{0}"' -f $WorkerHostExe)
}

$startupToken = if ($StartupType -eq 'Automatic') { 'auto' } else { 'demand' }
$accountToken = switch ($ServiceAccount) {
    'LocalService' { 'NT AUTHORITY\LocalService' }
    'NetworkService' { 'NT AUTHORITY\NetworkService' }
    default { 'LocalSystem' }
}

& sc.exe create $ServiceName `
    "binPath= $binaryPath" `
    "start= $startupToken" `
    "obj= $accountToken" `
    "DisplayName= $DisplayName" | Out-Null

& sc.exe description $ServiceName $Description | Out-Null
& sc.exe failure $ServiceName "reset= 86400" "actions= restart/5000/restart/5000/restart/15000" | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

if ($StartAfterInstall) {
    Start-Service -Name $ServiceName
}

$service = Get-Service -Name $ServiceName -ErrorAction Stop
[pscustomobject]@{
    ServiceName = $ServiceName
    DisplayName = $DisplayName
    WorkerHostExe = $WorkerHostExe
    StartupType = $StartupType
    ServiceAccount = $ServiceAccount
    BinaryPath = $binaryPath
    StartAfterInstall = [bool]$StartAfterInstall
    Status = [string]$service.Status
} | ConvertTo-Json -Depth 10
