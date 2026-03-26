param(
    [string]$TaskName = "BIM765T.Qdrant.Local"
)

$ErrorActionPreference = 'Stop'

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $task) {
    [pscustomobject]@{
        TaskName = $TaskName
        Removed = $false
        Message = "Scheduled task khong ton tai."
    } | ConvertTo-Json -Depth 10
    exit 0
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false

[pscustomobject]@{
    TaskName = $TaskName
    Removed = $true
} | ConvertTo-Json -Depth 10
