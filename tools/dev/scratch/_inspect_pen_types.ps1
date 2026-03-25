param([string]$FamilyName='Penetration Alpha')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe
function Invoke-BridgeTool { param([string]$Tool,[object]$Payload)
  $tmp = [System.IO.Path]::GetTempFileName() + '.json'
  try { $Payload | ConvertTo-Json -Depth 20 | Set-Content -Path $tmp -Encoding UTF8; (& $bridge $Tool --payload $tmp) | ConvertFrom-Json }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
$resp = Invoke-BridgeTool -Tool 'report.penetration_alpha_inventory' -Payload @{ FamilyName = $FamilyName; MaxResults = 5000; IncludeAxisStatus = $true }
if (-not $resp.Succeeded) { throw ($resp | ConvertTo-Json -Depth 20) }
$payload = $resp.PayloadJson | ConvertFrom-Json
$payload.Items | Group-Object TypeName | Sort-Object Count -Descending | Select-Object Count,Name | Format-Table -AutoSize | Out-String -Width 220
