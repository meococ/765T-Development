$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
$bridge = Resolve-BridgeExe
function Invoke-BridgeTool { param([string]$Tool,[object]$Payload)
  $tmp = [System.IO.Path]::GetTempFileName() + '.json'
  try { $Payload | ConvertTo-Json -Depth 20 | Set-Content -Path $tmp -Encoding UTF8; (& $bridge $Tool --payload $tmp) | ConvertFrom-Json }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}
$docResp = (& $bridge 'document.get_active') | ConvertFrom-Json
$doc = $docResp.PayloadJson | ConvertFrom-Json
$resp = Invoke-BridgeTool -Tool 'type.list_element_types' -Payload @{ DocumentKey = $doc.DocumentKey; CategoryNames=@(); ClassName=''; NameContains=''; IncludeParameters=$false; OnlyInUse=$false; MaxResults=10000 }
if (-not $resp.Succeeded) { throw ($resp | ConvertTo-Json -Depth 20) }
$payload = $resp.PayloadJson | ConvertFrom-Json
$payload.Items | Group-Object FamilyName | Sort-Object Count -Descending | Select-Object Count,Name | Format-Table -AutoSize | Out-String -Width 220
