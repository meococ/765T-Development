param(
    [string]$BridgeExe = "",
    [string]$ResultsArtifactDir = ""
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

function Invoke-MutationPreview {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [object]$Payload,
        [string]$TargetDocument = ""
    )

    $payloadJson = $Payload | ConvertTo-Json -Depth 100
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson -DryRun
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-MutationExecute {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [object]$Payload,
        [Parameter(Mandatory = $true)]
        [string]$ApprovalToken,
        [Parameter(Mandatory = $true)]
        [string]$PreviewRunId,
        [object]$ExpectedContext = $null,
        [string]$TargetDocument = ""
    )

    $tmpPayload = Join-Path $env:TEMP ("bridge_exec_payload_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
    $tmpExpectedContext = $null
    try {
        $Payload | ConvertTo-Json -Depth 100 | Set-Content -Path $tmpPayload -Encoding UTF8
        $args = @($Tool, '--dry-run', 'false')
        if (-not [string]::IsNullOrWhiteSpace($TargetDocument)) {
            $args += @('--target-document', $TargetDocument)
        }
        if ($null -ne $ExpectedContext) {
            $tmpExpectedContext = Join-Path $env:TEMP ("bridge_exec_context_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
            $ExpectedContext | ConvertTo-Json -Depth 100 | Set-Content -Path $tmpExpectedContext -Encoding UTF8
            $args += @('--expected-context', $tmpExpectedContext)
        }
        $args += @('--payload', $tmpPayload, '--approval-token', $ApprovalToken, '--preview-run-id', $PreviewRunId)
        $raw = & $BridgeExe @args
        if (-not $raw) {
            throw "Bridge tra ve rong cho tool $Tool khi execute."
        }

        $response = $raw | ConvertFrom-Json
        Assert-BridgeSuccess -Response $response -Tool $Tool
        return $response
    }
    finally {
        Remove-Item $tmpPayload -Force -ErrorAction SilentlyContinue
        if ($tmpExpectedContext) {
            Remove-Item $tmpExpectedContext -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-LatestSuccessfulResultsArtifactDir {
    $root = Join-Path $projectRoot 'artifacts\round-externalization-execute'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    foreach ($dir in (Get-ChildItem -Path $root -Directory | Sort-Object Name -Descending)) {
        $summaryPath = Join-Path $dir.FullName 'summary.json'
        if (-not (Test-Path $summaryPath)) {
            continue
        }

        try {
            $summary = Get-Content -Path $summaryPath -Raw | ConvertFrom-Json
            if ([int]$summary.CreatedCount -gt 0 -and [int]$summary.CreatedCount -eq [int]$summary.RequestedCount) {
                return $dir.FullName
            }
        }
        catch {
            continue
        }
    }

    throw "Khong tim thay externalization artifact thanh cong trong $root"
}

if ([string]::IsNullOrWhiteSpace($ResultsArtifactDir)) {
    $ResultsArtifactDir = Get-LatestSuccessfulResultsArtifactDir
}
elseif (-not (Test-Path $ResultsArtifactDir)) {
    throw "Results artifact dir khong ton tai: $ResultsArtifactDir"
}

$resultsPath = Join-Path $ResultsArtifactDir 'results.json'
if (-not (Test-Path $resultsPath)) {
    throw "Khong tim thay results.json tai: $resultsPath"
}

$results = Get-Content -Path $resultsPath -Raw | ConvertFrom-Json
$elementIds = @($results | Where-Object { [bool]$_.Succeeded -and $null -ne $_.CreatedElementId } | ForEach-Object { [int]$_.CreatedElementId } | Sort-Object -Unique)

$doc = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')
$docKey = [string]$doc.DocumentKey

$artifactDir = Join-Path $projectRoot ("artifacts\\round-externalization-cleanup\\{0}" -f ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

if ($elementIds.Count -eq 0) {
    $summary = [pscustomobject]@{
        GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        ArtifactDirectory = $artifactDir
        ResultsArtifactDir = $ResultsArtifactDir
        DocumentTitle = [string]$doc.Title
        DocumentKey = $docKey
        DeletedCount = 0
        ElementIds = @()
        Status = 'NOTHING_TO_DELETE'
    }
    Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
    $summary | ConvertTo-Json -Depth 20
    exit 0
}

$payload = @{
    DocumentKey = $docKey
    ElementIds = $elementIds
}

$preview = Invoke-MutationPreview -Tool 'element.delete_safe' -Payload $payload -TargetDocument $docKey
$previewPayload = ConvertFrom-PayloadJson -Response $preview
$execute = Invoke-MutationExecute -Tool 'element.delete_safe' -Payload $payload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $docKey
$execPayload = ConvertFrom-PayloadJson -Response $execute

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    ResultsArtifactDir = $ResultsArtifactDir
    DocumentTitle = [string]$doc.Title
    DocumentKey = $docKey
    RequestedDeleteCount = $elementIds.Count
    DeletedCount = @($execPayload.ChangedIds).Count
    ElementIds = $elementIds
    DeletedIds = @($execPayload.ChangedIds)
}

Write-JsonFile -Path (Join-Path $artifactDir 'document.json') -Data $doc
Write-JsonFile -Path (Join-Path $artifactDir 'preview.json') -Data $preview
Write-JsonFile -Path (Join-Path $artifactDir 'execute.json') -Data $execute
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary

$summary | ConvertTo-Json -Depth 20
