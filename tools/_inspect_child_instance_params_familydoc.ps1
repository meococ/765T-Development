$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe
$target = 'title:penetration alpha m.rfa'
function Invoke-BridgeTool { param([string]$Tool,[object]$Payload)
  $tmp = [System.IO.Path]::GetTempFileName() + '.json'
  try { $Payload | ConvertTo-Json -Depth 50 | Set-Content -Path $tmp -Encoding UTF8; (& $bridge $Tool --target-document $target --payload $tmp) | ConvertFrom-Json }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
$resp = Invoke-BridgeTool -Tool 'element.query' -Payload @{ DocumentKey = $target; ViewScopeOnly=$false; SelectedOnly=$false; ElementIds=@(); CategoryNames=@('Generic Models'); ClassName='FamilyInstance'; MaxResults=1000; IncludeParameters=$true }
$payload = $resp.PayloadJson | ConvertFrom-Json
$item = @($payload.Items | Where-Object { $_.FamilyName -eq 'Penetration Alpha' } | Select-Object -First 1)
$item | ConvertTo-Json -Depth 30
