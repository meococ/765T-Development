param(
    [string]$BridgeExe = "",
    [string]$DocumentSelector = "",
    [string]$SourceFamilyName = "Penetraion Alpha M",
    [string]$TargetFamilyName = "Penetration Alpha",
    [string]$TypeNameContains = "",
    [switch]$Execute,
    [switch]$VerboseReport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)

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

function Invoke-BridgeWithPayloadFile {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [object]$Payload = $null,
        [string]$TargetDocument = "",
        [switch]$DryRun,
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
        if ($payloadPath) {
            Remove-Item $payloadPath -Force -ErrorAction SilentlyContinue
        }
        if ($contextPath) {
            Remove-Item $contextPath -Force -ErrorAction SilentlyContinue
        }
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

    $response = Invoke-BridgeWithPayloadFile `
        -Tool $Tool `
        -Payload $Payload `
        -TargetDocument $TargetDocument `
        -ApprovalToken $ApprovalToken `
        -PreviewRunId $PreviewRunId `
        -ExpectedContext $ExpectedContext

    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Normalize-ParameterMap {
    param([object[]]$Parameters)

    $map = @{}
    foreach ($parameter in @($Parameters)) {
        if ($null -eq $parameter) {
            continue
        }

        $name = [string]$parameter.Name
        if ([string]::IsNullOrWhiteSpace($name)) {
            continue
        }

        $map[$name] = [pscustomobject]@{
            Name = $name
            StorageType = [string]$parameter.StorageType
            Value = [string]$parameter.Value
            IsReadOnly = [bool]$parameter.IsReadOnly
        }
    }

    return $map
}

function Get-TypeCatalog {
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDocumentKey,
        [Parameter(Mandatory = $true)][string]$TargetDocument
    )

    $payload = @{
        DocumentKey = $PayloadDocumentKey
        CategoryNames = @()
        NameContains = $TypeNameContains
        IncludeParameters = $true
        OnlyInUse = $false
        MaxResults = 10000
    }

    return ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'type.list_element_types' -Payload $payload -TargetDocument $TargetDocument)
}

function Resolve-FamilyTypes {
    param(
        [AllowEmptyCollection()][object[]]$Items = @(),
        [Parameter(Mandatory = $true)][string]$FamilyName
    )

    return @(
        $Items |
            Where-Object { [string]$_.FamilyName -eq $FamilyName } |
            Sort-Object TypeName, TypeId
    )
}

$active = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')
$doc = if ([string]::IsNullOrWhiteSpace($DocumentSelector)) {
    $active
}
else {
    [pscustomobject]@{
        DocumentKey = $DocumentSelector
        Title = $DocumentSelector
        PathName = ''
    }
}

$targetDocument = if (-not [string]::IsNullOrWhiteSpace($DocumentSelector)) { $DocumentSelector } elseif (-not [string]::IsNullOrWhiteSpace([string]$doc.Title)) { [string]$doc.Title } else { [string]$doc.DocumentKey }
$payloadDocumentKey = if (-not [string]::IsNullOrWhiteSpace([string]$doc.Title)) { [string]$doc.Title } else { [string]$doc.DocumentKey }

$catalog = Get-TypeCatalog -PayloadDocumentKey $payloadDocumentKey -TargetDocument $targetDocument
$items = @($catalog.Items)
$sourceTypes = @(Resolve-FamilyTypes -Items $items -FamilyName $SourceFamilyName)
$targetTypes = @(Resolve-FamilyTypes -Items $items -FamilyName $TargetFamilyName)

if ($sourceTypes.Count -eq 0) {
    $candidateFamilies = @(
        $items |
            Where-Object {
                $family = [string]$_.FamilyName
                $family.IndexOf('penet', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                $family.IndexOf('alpha', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            } |
            Select-Object -ExpandProperty FamilyName -Unique |
            Sort-Object
    )
    throw ("Khong tim thay source family '{0}' trong type catalog. CandidateFamilies={1}" -f $SourceFamilyName, ($candidateFamilies -join ', '))
}

if ($targetTypes.Count -eq 0) {
    $candidateFamilies = @(
        $items |
            Where-Object {
                $family = [string]$_.FamilyName
                $family.IndexOf('penet', [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                $family.IndexOf('alpha', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            } |
            Select-Object -ExpandProperty FamilyName -Unique |
            Sort-Object
    )
    throw ("Khong tim thay target family '{0}' trong type catalog. CandidateFamilies={1}" -f $TargetFamilyName, ($candidateFamilies -join ', '))
}

$sourceTypeNames = @($sourceTypes | ForEach-Object { [string]$_.TypeName })
$targetTypeNames = @($targetTypes | ForEach-Object { [string]$_.TypeName })
$sharedTypeNames = @($sourceTypeNames | Where-Object { $targetTypeNames -contains $_ } | Select-Object -Unique | Sort-Object)

if ($sourceTypes.Count -ne 1 -or $targetTypes.Count -ne 1) {
    if ($sharedTypeNames.Count -ne 1) {
        throw ("Source family '{0}' co {1} type, target family '{2}' co {3} type. Chua map duoc 1-1 an toan. SharedTypeNames={4}" -f `
            $SourceFamilyName, $sourceTypes.Count, $TargetFamilyName, $targetTypes.Count, ($sharedTypeNames -join ', '))
    }

    $resolvedTypeName = [string]$sharedTypeNames[0]
    $sourceTypes = @($sourceTypes | Where-Object { [string]$_.TypeName -eq $resolvedTypeName })
    $targetTypes = @($targetTypes | Where-Object { [string]$_.TypeName -eq $resolvedTypeName })
}

if ($sourceTypes.Count -ne 1 -or $targetTypes.Count -ne 1) {
    throw "Khong resolve duoc duy nhat 1 source type va 1 target type de copy."
}

$sourceType = $sourceTypes[0]
$targetType = $targetTypes[0]

$sourceParams = Normalize-ParameterMap -Parameters @($sourceType.Parameters)
$targetParams = Normalize-ParameterMap -Parameters @($targetType.Parameters)

$blockedNames = @(
    'Type Name',
    'Family Name',
    'Type',
    'Family',
    'Image',
    'Keynote',
    'Assembly Code',
    'OmniClass Number',
    'OmniClass Title',
    'Type Comments'
)

$parameterNamesToCopy = New-Object System.Collections.Generic.List[string]
$parameterDiffPreview = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[object]

foreach ($entry in $sourceParams.GetEnumerator() | Sort-Object Name) {
    $name = [string]$entry.Key
    $src = $entry.Value

    if ($blockedNames -contains $name) {
        $skipped.Add([pscustomobject]@{ Name = $name; Reason = 'blocked_name'; SourceValue = $src.Value; TargetValue = if ($targetParams.ContainsKey($name)) { $targetParams[$name].Value } else { '' } }) | Out-Null
        continue
    }

    if (-not $targetParams.ContainsKey($name)) {
        $skipped.Add([pscustomobject]@{ Name = $name; Reason = 'missing_on_target'; SourceValue = $src.Value; TargetValue = '' }) | Out-Null
        continue
    }

    $tgt = $targetParams[$name]
    if ($tgt.IsReadOnly) {
        $skipped.Add([pscustomobject]@{ Name = $name; Reason = 'target_read_only'; SourceValue = $src.Value; TargetValue = $tgt.Value }) | Out-Null
        continue
    }

    $parameterNamesToCopy.Add($name) | Out-Null
    $parameterDiffPreview.Add([pscustomobject]@{
        Name = $name
        SourceValue = $src.Value
        TargetValue = $tgt.Value
        WillChange = -not [string]::Equals([string]$src.Value, [string]$tgt.Value, [System.StringComparison]::Ordinal)
        StorageType = $src.StorageType
    }) | Out-Null
}

if ($parameterNamesToCopy.Count -eq 0) {
    throw "Khong co parameter hop le nao de copy."
}

$copyPayload = @{
    DocumentKey = $payloadDocumentKey
    SourceElementId = [int]$sourceType.TypeId
    TargetElementIds = @([int]$targetType.TypeId)
    ParameterNames = @($parameterNamesToCopy.ToArray())
    SkipReadOnly = $true
}

$preview = Invoke-MutationPreview -Tool 'parameter.copy_between_safe' -Payload $copyPayload -TargetDocument $targetDocument
$previewPayload = ConvertFrom-PayloadJson -Response $preview

$executeResponse = $null
$verifyCatalog = $catalog

if ($Execute.IsPresent) {
    $executeResponse = Invoke-MutationExecute `
        -Tool 'parameter.copy_between_safe' `
        -Payload $copyPayload `
        -ApprovalToken $preview.ApprovalToken `
        -PreviewRunId $preview.PreviewRunId `
        -ExpectedContext $previewPayload.ResolvedContext `
        -TargetDocument $targetDocument

    $verifyCatalog = Get-TypeCatalog -PayloadDocumentKey $payloadDocumentKey -TargetDocument $targetDocument
}

$verifiedTargetType = @($verifyCatalog.Items | Where-Object {
    [string]$_.FamilyName -eq $TargetFamilyName -and [int]$_.TypeId -eq [int]$targetType.TypeId
}) | Select-Object -First 1

$verifiedTargetParams = if ($null -ne $verifiedTargetType) {
    Normalize-ParameterMap -Parameters @($verifiedTargetType.Parameters)
}
else {
    @{}
}

$verification = foreach ($row in $parameterDiffPreview) {
    $afterValue = if ($verifiedTargetParams.ContainsKey([string]$row.Name)) { [string]$verifiedTargetParams[[string]$row.Name].Value } else { '' }
    [pscustomobject]@{
        Name = [string]$row.Name
        SourceValue = [string]$row.SourceValue
        TargetBefore = [string]$row.TargetValue
        TargetAfter = $afterValue
        StorageType = [string]$row.StorageType
        MatchedAfter = [string]::Equals([string]$row.SourceValue, [string]$afterValue, [System.StringComparison]::Ordinal)
    }
}

$artifactDir = Join-Path $projectRoot ("artifacts\\penetration-alpha-parameter-copy\\{0}" -f ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    SourceFamilyName = $SourceFamilyName
    SourceTypeId = [int]$sourceType.TypeId
    SourceTypeName = [string]$sourceType.TypeName
    TargetFamilyName = $TargetFamilyName
    TargetTypeId = [int]$targetType.TypeId
    TargetTypeName = [string]$targetType.TypeName
    ParameterCountRequested = $parameterNamesToCopy.Count
    ParameterCountChanged = @($verification | Where-Object { -not $_.MatchedAfter }).Count
    ParameterCountMatchedAfter = @($verification | Where-Object { $_.MatchedAfter }).Count
    Executed = [bool]$Execute.IsPresent
    PreviewStatus = [string]$preview.StatusCode
    ExecuteStatus = if ($executeResponse) { [string]$executeResponse.StatusCode } else { '' }
    ArtifactDirectory = $artifactDir
}

$summary | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'summary.json') -Encoding UTF8
$preview | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $artifactDir 'preview.json') -Encoding UTF8
$copyPayload | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'copy-payload.json') -Encoding UTF8
$parameterDiffPreview | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'parameter-diff-preview.json') -Encoding UTF8
$verification | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'verification.json') -Encoding UTF8
$skipped | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'skipped.json') -Encoding UTF8
if ($executeResponse) {
    $executeResponse | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $artifactDir 'execute.json') -Encoding UTF8
}

$report = [ordered]@{
    DocumentTitle = [string]$doc.Title
    Source = [pscustomobject]@{
        FamilyName = $SourceFamilyName
        TypeName = [string]$sourceType.TypeName
        TypeId = [int]$sourceType.TypeId
    }
    Target = [pscustomobject]@{
        FamilyName = $TargetFamilyName
        TypeName = [string]$targetType.TypeName
        TypeId = [int]$targetType.TypeId
    }
    Executed = [bool]$Execute.IsPresent
    PreviewStatus = [string]$preview.StatusCode
    ExecuteStatus = if ($executeResponse) { [string]$executeResponse.StatusCode } else { '' }
    ParametersRequested = @($parameterNamesToCopy.ToArray())
    Verification = @($verification)
    SkippedCount = $skipped.Count
    ArtifactDirectory = $artifactDir
}

if ($VerboseReport.IsPresent) {
    $report.Skipped = @($skipped)
}

$report | ConvertTo-Json -Depth 50
