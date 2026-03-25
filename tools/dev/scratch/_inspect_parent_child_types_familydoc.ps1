$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe
$target = 'title:penetration alpha m.rfa'
function Invoke-BridgeTool { param([string]$Tool,[object]$Payload)
  $tmp = [System.IO.Path]::GetTempFileName() + '.json'
  try { $Payload | ConvertTo-Json -Depth 50 | Set-Content -Path $tmp -Encoding UTF8; (& $bridge $Tool --target-document $target --payload $tmp) | ConvertFrom-Json }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
$resp = Invoke-BridgeTool -Tool 'type.list_element_types' -Payload @{ DocumentKey = $target; CategoryNames=@(); ClassName=''; NameContains=''; IncludeParameters=$true; OnlyInUse=$false; MaxResults=10000 }
if (-not $resp.Succeeded) { throw ($resp | ConvertTo-Json -Depth 20) }
$payload = $resp.PayloadJson | ConvertFrom-Json
'--- Parent types ---'
$payload.Items | Where-Object { $_.FamilyName -eq 'Penetration Alpha M' } | Sort-Object TypeName | Select-Object TypeId,TypeName,UsageCount | Format-Table -AutoSize
'--- Child types ---'
$payload.Items | Where-Object { $_.FamilyName -eq 'Penetration Alpha' } | Sort-Object TypeName | Select-Object TypeId,TypeName,UsageCount | Format-Table -AutoSize
