Get-Content (Join-Path $PSScriptRoot 'axis_audit_full.ps1') -Encoding UTF8 |
    Select-String '[^\x00-\x7F]' |
    Select-Object -First 5 |
    ForEach-Object { Write-Host "Line $($_.LineNumber): $($_.Line)" }
