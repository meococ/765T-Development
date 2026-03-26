$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe
function Invoke-BridgeTool { param([string]$Tool,[object]$Payload)
  $tmp = [System.IO.Path]::GetTempFileName() + '.json'
  try { $Payload | ConvertTo-Json -Depth 50 | Set-Content -Path $tmp -Encoding UTF8; (& $bridge $Tool --payload $tmp) | ConvertFrom-Json }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
$docResp = (& $bridge 'document.get_active') | ConvertFrom-Json
$doc = $docResp.PayloadJson | ConvertFrom-Json
$resp = Invoke-BridgeTool -Tool 'type.list_element_types' -Payload @{ DocumentKey = $doc.DocumentKey; CategoryNames=@(); ClassName=''; NameContains='1" cable'; IncludeParameters=$true; OnlyInUse=$false; MaxResults=100 }
$payload = $resp.PayloadJson | ConvertFrom-Json
$item = @($payload.Items | Where-Object { $_.FamilyName -eq 'Penetration Alpha M' -and $_.TypeName -eq '1" cable' } | Select-Object -First 1)
$item | ConvertTo-Json -Depth 20
