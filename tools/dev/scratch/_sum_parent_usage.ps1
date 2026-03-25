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
$payload = $resp.PayloadJson | ConvertFrom-Json
$parent = @($payload.Items | Where-Object { $_.FamilyName -eq 'Penetration Alpha M' })
$sum = ($parent | Measure-Object -Property UsageCount -Sum).Sum
[pscustomobject]@{ ParentTypeCount=$parent.Count; ParentUsageSum=$sum } | Format-List
