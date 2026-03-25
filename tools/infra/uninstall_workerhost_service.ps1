param(
    [string]$ServiceName = "BIM765T.Revit.WorkerHost"
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if (-not (Test-IsAdministrator)) {
    throw "Script nay can chay PowerShell voi quyen Administrator de go Windows Service."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    [pscustomobject]@{
        ServiceName = $ServiceName
        Removed = $false
        Message = "Service khong ton tai."
    } | ConvertTo-Json -Depth 10
    exit 0
}

if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}

& sc.exe delete $ServiceName | Out-Null

for ($i = 0; $i -lt 30; $i++) {
    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        [pscustomobject]@{
            ServiceName = $ServiceName
            Removed = $true
        } | ConvertTo-Json -Depth 10
        exit 0
    }

    Start-Sleep -Milliseconds 500
}

throw "Service '$ServiceName' van chua bi xoa sau timeout."
