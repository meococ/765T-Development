param(
    [string]$BridgeExe = "",
    [string]$FamilyName = "",
    [string]$CommentContains = "",
    [int]$MaxResults = 10000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not [string]::IsNullOrWhiteSpace($BridgeExe) -and (Test-Path $BridgeExe)) {
    $BridgeExe = (Resolve-Path $BridgeExe).Path
}
else {
    $repoBridge = Join-Path $projectRoot 'src\BIM765T.Revit.Bridge\bin\Release\net8.0\BIM765T.Revit.Bridge.exe'
    if (-not (Test-Path $repoBridge)) {
        throw "Khong tim thay BIM765T.Revit.Bridge.exe tai: $repoBridge"
    }

    $BridgeExe = (Resolve-Path $repoBridge).Path
}

function Assert-BridgeSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Response,
        [Parameter(Mandatory = $true)]
        [string]$Tool
    )

    if ($null -eq $Response) {
        throw "Tool $Tool tra ve null."
    }

    if (-not $Response.Succeeded) {
        $diag = @($Response.Diagnostics | ForEach-Object { $_.Code + ':' + $_.Message }) -join ' | '
        if ([string]::IsNullOrWhiteSpace($diag)) {
            $diag = '<khong co diagnostics>'
        }
        throw "Tool $Tool that bai. Status=$($Response.StatusCode). Diag=$diag"
    }
}

function ConvertFrom-PayloadJson {
    param([Parameter(Mandatory = $true)][object]$Response)
    if ([string]::IsNullOrWhiteSpace([string]$Response.PayloadJson)) {
        return $null
    }

    return ($Response.PayloadJson | ConvertFrom-Json)
}

function Invoke-ReadTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [object]$Payload = $null,
        [string]$TargetDocument = ""
    )

    $payloadJson = if ($null -eq $Payload) { "" } else { $Payload | ConvertTo-Json -Depth 100 }
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

$doc = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')

$payload = @{
    DocumentKey = $doc.DocumentKey
    ViewScopeOnly = $false
    SelectedOnly = $false
    ElementIds = @()
    MaxResults = $MaxResults
    IncludeParameters = $true
}

$query = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload $payload -TargetDocument $doc.DocumentKey)
$items = @($query.Items)

if (-not [string]::IsNullOrWhiteSpace($FamilyName)) {
    $items = @($items | Where-Object { [string]$_.FamilyName -eq $FamilyName })
}

if (-not [string]::IsNullOrWhiteSpace($CommentContains)) {
    $items = @($items | Where-Object {
        $comment = @($_.Parameters | Where-Object { [string]$_.Name -eq 'Comments' } | Select-Object -First 1)[0]
        $commentValue = if ($null -ne $comment) { [string]$comment.Value } else { '' }
        $commentValue -like ('*' + $CommentContains + '*')
    })
}

$result = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    FamilyName = $FamilyName
    CommentContains = $CommentContains
    Count = $items.Count
    Items = $items
}

$result | ConvertTo-Json -Depth 20
