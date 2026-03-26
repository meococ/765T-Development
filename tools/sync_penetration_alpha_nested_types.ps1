param(
    [string]$BridgeExe = "",
    [string]$ParentFamilyName = "Penetration Alpha M",
    [string]$NestedFamilyName = "Penetration Alpha",
    [string]$FamilyDocumentSelector = "",
    [string]$ProjectDocumentSelector = "",
    [string]$PreferredSeedTypeName = "",
    [switch]$ReloadIntoProject,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$artifactRoot = Join-Path $projectRoot 'artifacts\penetration-alpha-nested-type-sync'
$runDir = Join-Path $artifactRoot ([DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

function Assert-BridgeSuccess {
    param(
        [Parameter(Mandatory = $true)][object]$Response,
        [Parameter(Mandatory = $true)][string]$Tool
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

function Invoke-BridgeWithPayloadFile {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [object]$Payload = $null,
        [string]$TargetDocument = "",
        [switch]$DryRun,
        [switch]$ForceExecute,
        [string]$ApprovalToken = "",
        [string]$PreviewRunId = "",
        [object]$ExpectedContext = $null
    )

    $payloadPath = $null
    $contextPath = $null
    try {
        $args = New-Object System.Collections.Generic.List[string]
        $args.Add($Tool) | Out-Null

        if ($DryRun.IsPresent) {
            $args.Add('--dry-run') | Out-Null
            $args.Add('true') | Out-Null
        }
        elseif ($ForceExecute.IsPresent) {
            $args.Add('--dry-run') | Out-Null
            $args.Add('false') | Out-Null
        }

        if (-not [string]::IsNullOrWhiteSpace($TargetDocument)) {
            $args.Add('--target-document') | Out-Null
            $args.Add($TargetDocument) | Out-Null
        }

        if ($null -ne $Payload) {
            $payloadPath = Join-Path $env:TEMP ("payload_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
            $Payload | ConvertTo-Json -Depth 100 | Set-Content -Path $payloadPath -Encoding UTF8
            $args.Add('--payload') | Out-Null
            $args.Add($payloadPath) | Out-Null
        }

        if (-not [string]::IsNullOrWhiteSpace($ApprovalToken)) {
            $args.Add('--approval-token') | Out-Null
            $args.Add($ApprovalToken) | Out-Null
        }

        if (-not [string]::IsNullOrWhiteSpace($PreviewRunId)) {
            $args.Add('--preview-run-id') | Out-Null
            $args.Add($PreviewRunId) | Out-Null
        }

        if ($null -ne $ExpectedContext) {
            $contextPath = Join-Path $env:TEMP ("expected_context_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
            $ExpectedContext | ConvertTo-Json -Depth 100 | Set-Content -Path $contextPath -Encoding UTF8
            $args.Add('--expected-context') | Out-Null
            $args.Add($contextPath) | Out-Null
        }

        $raw = & $BridgeExe @args
        if (-not $raw) {
            throw "Bridge tra ve rong cho tool $Tool."
        }

        return ($raw | ConvertFrom-Json)
    }
    finally {
        if ($payloadPath) { Remove-Item $payloadPath -Force -ErrorAction SilentlyContinue }
        if ($contextPath) { Remove-Item $contextPath -Force -ErrorAction SilentlyContinue }
    }
}

function Invoke-ReadTool {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [object]$Payload = $null,
        [string]$TargetDocument = ""
    )

    $response = Invoke-BridgeWithPayloadFile -Tool $Tool -Payload $Payload -TargetDocument $TargetDocument
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-MutationPreview {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$Payload,
        [string]$TargetDocument = ""
    )

    $response = Invoke-BridgeWithPayloadFile -Tool $Tool -Payload $Payload -TargetDocument $TargetDocument -DryRun
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-MutationExecute {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$Payload,
        [Parameter(Mandatory = $true)][string]$ApprovalToken,
        [Parameter(Mandatory = $true)][string]$PreviewRunId,
        [object]$ExpectedContext = $null,
        [string]$TargetDocument = ""
    )

    $response = Invoke-BridgeWithPayloadFile -Tool $Tool -Payload $Payload -TargetDocument $TargetDocument -ForceExecute -ApprovalToken $ApprovalToken -PreviewRunId $PreviewRunId -ExpectedContext $ExpectedContext
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Get-ArtifactValue {
    param(
        [string[]]$Artifacts,
        [string]$Key
    )

    foreach ($artifact in @($Artifacts)) {
        if ($artifact -like "$Key=*") {
            return $artifact.Substring($Key.Length + 1)
        }
    }

    return $null
}

$toolCatalogResponse = Invoke-ReadTool -Tool 'session.list_tools'
$toolCatalog = ConvertFrom-PayloadJson -Response $toolCatalogResponse
$toolNames = @($toolCatalog.Tools | ForEach-Object { [string]$_.ToolName })
if ($toolNames -notcontains 'family.sync_penetration_alpha_nested_types_safe') {
    throw "Runtime chua load tool family.sync_penetration_alpha_nested_types_safe. Can restart Revit de nap DLL moi nhat."
}

$openDocsResponse = Invoke-ReadTool -Tool 'session.list_open_documents'
$openDocs = ConvertFrom-PayloadJson -Response $openDocsResponse
$documents = @($openDocs.Documents)
if ($documents.Count -eq 0) {
    throw 'Khong co document nao dang mo trong Revit.'
}

$familyDoc = $null
if (-not [string]::IsNullOrWhiteSpace($FamilyDocumentSelector)) {
    $familyDoc = @($documents | Where-Object { [string]$_.DocumentKey -eq $FamilyDocumentSelector -or [string]$_.Title -eq $FamilyDocumentSelector }) | Select-Object -First 1
}
if ($null -eq $familyDoc) {
    $familyDoc = @($documents | Where-Object { $_.IsFamilyDocument -and ([string]$_.Title -like "$ParentFamilyName*") }) | Select-Object -First 1
}
if ($null -eq $familyDoc) {
    throw "Khong tim thay family document dang mo cho '$ParentFamilyName'."
}

$projectDoc = $null
if ($ReloadIntoProject.IsPresent -or $Execute.IsPresent) {
    if (-not [string]::IsNullOrWhiteSpace($ProjectDocumentSelector)) {
        $projectDoc = @($documents | Where-Object { (-not $_.IsFamilyDocument) -and ([string]$_.DocumentKey -eq $ProjectDocumentSelector -or [string]$_.Title -eq $ProjectDocumentSelector) }) | Select-Object -First 1
    }
    if ($null -eq $projectDoc) {
        $projectDoc = @($documents | Where-Object { (-not $_.IsFamilyDocument) -and $_.IsActive }) | Select-Object -First 1
    }
    if ($null -eq $projectDoc) {
        $projectDoc = @($documents | Where-Object { -not $_.IsFamilyDocument }) | Select-Object -First 1
    }
}

$payload = [ordered]@{
    DocumentKey = [string]$familyDoc.DocumentKey
    ParentFamilyName = $ParentFamilyName
    NestedFamilyName = $NestedFamilyName
    ProjectDocumentKey = if ($projectDoc) { [string]$projectDoc.DocumentKey } else { '' }
    ReloadIntoProject = [bool]($ReloadIntoProject.IsPresent -or $Execute.IsPresent)
    OverwriteExistingProjectFamily = $true
    RequireSingleNestedInstance = $true
    PreferredSeedTypeName = $PreferredSeedTypeName
}

$payload | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir 'payload.json') -Encoding UTF8
$documents | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir 'open-documents.json') -Encoding UTF8

$previewResponse = Invoke-MutationPreview -Tool 'family.sync_penetration_alpha_nested_types_safe' -Payload $payload -TargetDocument ([string]$familyDoc.DocumentKey)
$previewPayload = ConvertFrom-PayloadJson -Response $previewResponse
$previewResponse | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runDir 'preview-envelope.json') -Encoding UTF8
$previewPayload | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runDir 'preview-result.json') -Encoding UTF8

$summary = [ordered]@{
    FamilyDocument = [string]$familyDoc.Title
    FamilyDocumentKey = [string]$familyDoc.DocumentKey
    ProjectDocument = if ($projectDoc) { [string]$projectDoc.Title } else { '' }
    ProjectDocumentKey = if ($projectDoc) { [string]$projectDoc.DocumentKey } else { '' }
    ParentTypeCount = [int](Get-ArtifactValue -Artifacts $previewPayload.Artifacts -Key 'parentTypeCount')
    NestedInstanceCount = [int](Get-ArtifactValue -Artifacts $previewPayload.Artifacts -Key 'nestedInstanceCount')
    MissingChildTypeCount = [int](Get-ArtifactValue -Artifacts $previewPayload.Artifacts -Key 'missingChildTypeCount')
    AssignCount = [int](Get-ArtifactValue -Artifacts $previewPayload.Artifacts -Key 'assignCount')
    ExecuteRequested = [bool]$Execute.IsPresent
    ArtifactDir = $runDir
}

if (-not $Execute.IsPresent) {
    $summary | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir 'summary.json') -Encoding UTF8
    [pscustomobject]$summary | Format-List
    return
}

$executeResponse = Invoke-MutationExecute -Tool 'family.sync_penetration_alpha_nested_types_safe' -Payload $payload -ApprovalToken ([string]$previewPayload.ApprovalToken) -PreviewRunId ([string]$previewPayload.PreviewRunId) -ExpectedContext $previewPayload.ResolvedContext -TargetDocument ([string]$familyDoc.DocumentKey)
$executePayload = ConvertFrom-PayloadJson -Response $executeResponse
$executeResponse | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runDir 'execute-envelope.json') -Encoding UTF8
$executePayload | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runDir 'execute-result.json') -Encoding UTF8

$verifyPreviewResponse = Invoke-MutationPreview -Tool 'family.sync_penetration_alpha_nested_types_safe' -Payload $payload -TargetDocument ([string]$familyDoc.DocumentKey)
$verifyPreviewPayload = ConvertFrom-PayloadJson -Response $verifyPreviewResponse
$verifyPreviewResponse | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runDir 'verify-preview-envelope.json') -Encoding UTF8
$verifyPreviewPayload | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $runDir 'verify-preview-result.json') -Encoding UTF8

$summary.ExecuteStatus = [string]$executeResponse.StatusCode
$summary.CreatedNestedTypeCount = [int](Get-ArtifactValue -Artifacts $executePayload.Artifacts -Key 'createdNestedTypeCount')
$summary.ModifiedElementCount = [int](Get-ArtifactValue -Artifacts $executePayload.Artifacts -Key 'modifiedElementCount')
$summary.VerifyMissingChildTypeCount = [int](Get-ArtifactValue -Artifacts $verifyPreviewPayload.Artifacts -Key 'missingChildTypeCount')
$summary.VerifyAssignCount = [int](Get-ArtifactValue -Artifacts $verifyPreviewPayload.Artifacts -Key 'assignCount')
$summary.VerifyPreviewRunId = [string]$verifyPreviewPayload.PreviewRunId

$summary | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir 'summary.json') -Encoding UTF8
[pscustomobject]$summary | Format-List
