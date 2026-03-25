$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe
function Invoke-BridgeTool { param([string]$Tool,[object]$Payload)
  $tmp = [System.IO.Path]::GetTempFileName() + '.json'
  try { $Payload | ConvertTo-Json -Depth 20 | Set-Content -Path $tmp -Encoding UTF8; (& $bridge $Tool --payload $tmp) | ConvertFrom-Json }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
$resp = Invoke-BridgeTool -Tool 'report.penetration_alpha_inventory' -Payload @{ FamilyName = 'Penetration Alpha'; MaxResults = 20; IncludeAxisStatus = $true }
$payload = $resp.PayloadJson | ConvertFrom-Json
$payload.Items | Select-Object -First 20 ElementId,TypeName,Mark,MiiDiameter,MiiDimLength,MiiElementClass,MiiElementTier | Format-Table -AutoSize | Out-String -Width 220
