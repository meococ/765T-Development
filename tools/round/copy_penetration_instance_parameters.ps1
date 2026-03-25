param(
    [string]$BridgeExe = "",
    [string]$DocumentSelector = "",
    [string]$SourceFamilyName = "Penetration Alpha M",
    [string]$TargetFamilyName = "Penetration Alpha",
    [string[]]$ParameterNames = @(),
    [double]$MaxPairDistanceFeet = 10.0,
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
        -ForceExecute `
        -ApprovalToken $ApprovalToken `
        -PreviewRunId $PreviewRunId `
        -ExpectedContext $ExpectedContext

    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Get-Inventory {
    param(
        [Parameter(Mandatory = $true)][string]$FamilyName,
        [Parameter(Mandatory = $true)][string]$PayloadDocumentKey,
        [Parameter(Mandatory = $true)][string]$TargetDocument
    )

    $payload = @{
        DocumentKey = $PayloadDocumentKey
        FamilyName = $FamilyName
        MaxResults = 5000
        IncludeAxisStatus = $true
    }

    return ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'report.penetration_alpha_inventory' -Payload $payload -TargetDocument $TargetDocument)
}

function Get-ElementDetails {
    param(
        [Parameter(Mandatory = $true)][int[]]$ElementIds,
        [Parameter(Mandatory = $true)][string]$PayloadDocumentKey,
        [Parameter(Mandatory = $true)][string]$TargetDocument
    )

    $payload = @{
        DocumentKey = $PayloadDocumentKey
        ViewScopeOnly = $false
        SelectedOnly = $false
        ElementIds = @($ElementIds)
        CategoryNames = @()
        ClassName = ''
        MaxResults = [Math]::Max(10, $ElementIds.Count + 20)
        IncludeParameters = $true
    }

    return ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload $payload -TargetDocument $TargetDocument)
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

function Get-ElementCenter {
    param([object]$Element)

    if ($null -ne $Element.LocationPoint) {
        return [pscustomobject]@{
            X = [double]$Element.LocationPoint.X
            Y = [double]$Element.LocationPoint.Y
            Z = [double]$Element.LocationPoint.Z
            Source = 'location_point'
        }
    }

    if (($null -ne $Element.LocationCurveStart) -and ($null -ne $Element.LocationCurveEnd)) {
        return [pscustomobject]@{
            X = (([double]$Element.LocationCurveStart.X) + ([double]$Element.LocationCurveEnd.X)) / 2.0
            Y = (([double]$Element.LocationCurveStart.Y) + ([double]$Element.LocationCurveEnd.Y)) / 2.0
            Z = (([double]$Element.LocationCurveStart.Z) + ([double]$Element.LocationCurveEnd.Z)) / 2.0
            Source = 'location_curve_mid'
        }
    }

    if ($null -ne $Element.BoundingBox) {
        return [pscustomobject]@{
            X = (([double]$Element.BoundingBox.MinX) + ([double]$Element.BoundingBox.MaxX)) / 2.0
            Y = (([double]$Element.BoundingBox.MinY) + ([double]$Element.BoundingBox.MaxY)) / 2.0
            Z = (([double]$Element.BoundingBox.MinZ) + ([double]$Element.BoundingBox.MaxZ)) / 2.0
            Source = 'bbox_center'
        }
    }

    return $null
}

function Get-PairDistance {
    param(
        [object]$SourceCenter,
        [object]$TargetCenter
    )

    if (($null -eq $SourceCenter) -or ($null -eq $TargetCenter)) {
        return [double]::PositiveInfinity
    }

    $dx = ([double]$SourceCenter.X) - ([double]$TargetCenter.X)
    $dy = ([double]$SourceCenter.Y) - ([double]$TargetCenter.Y)
    $dz = ([double]$SourceCenter.Z) - ([double]$TargetCenter.Z)
    return [Math]::Sqrt(($dx * $dx) + ($dy * $dy) + ($dz * $dz))
}

function Get-StrictSignature {
    param([object]$InventoryItem)

    return @(
        [string]$InventoryItem.HostElementId,
        [string]$InventoryItem.LevelName,
        [string]$InventoryItem.MiiDiameter,
        [string]$InventoryItem.MiiDimLength,
        [string]$InventoryItem.MiiElementClass,
        [string]$InventoryItem.MiiElementTier,
        [string]$InventoryItem.Mark
    ) -join '|'
}

function Get-RelaxedSignature {
    param([object]$InventoryItem)

    return @(
        [string]$InventoryItem.HostElementId,
        [string]$InventoryItem.LevelName,
        [string]$InventoryItem.MiiDiameter,
        [string]$InventoryItem.MiiDimLength,
        [string]$InventoryItem.MiiElementClass,
        [string]$InventoryItem.MiiElementTier
    ) -join '|'
}

function Get-MatchScore {
    param(
        [object]$SourceItem,
        [object]$TargetItem,
        [double]$DistanceFeet
    )

    $score = 0
    if ([string]::Equals([string]$SourceItem.HostElementId, [string]$TargetItem.HostElementId, [System.StringComparison]::OrdinalIgnoreCase)) { $score += 100 }
    if ([string]::Equals([string]$SourceItem.LevelName, [string]$TargetItem.LevelName, [System.StringComparison]::OrdinalIgnoreCase)) { $score += 30 }
    if (-not [string]::IsNullOrWhiteSpace([string]$SourceItem.Mark) -and [string]::Equals([string]$SourceItem.Mark, [string]$TargetItem.Mark, [System.StringComparison]::OrdinalIgnoreCase)) { $score += 40 }
    if ([string]::Equals([string]$SourceItem.MiiDiameter, [string]$TargetItem.MiiDiameter, [System.StringComparison]::OrdinalIgnoreCase)) { $score += 15 }
    if ([string]::Equals([string]$SourceItem.MiiDimLength, [string]$TargetItem.MiiDimLength, [System.StringComparison]::OrdinalIgnoreCase)) { $score += 15 }
    if ([string]::Equals([string]$SourceItem.MiiElementClass, [string]$TargetItem.MiiElementClass, [System.StringComparison]::OrdinalIgnoreCase)) { $score += 10 }
    if ([string]::Equals([string]$SourceItem.MiiElementTier, [string]$TargetItem.MiiElementTier, [System.StringComparison]::OrdinalIgnoreCase)) { $score += 10 }
    if ([double]::IsInfinity($DistanceFeet)) {
        $score -= 1000
    }
    else {
        $score -= [int][Math]::Round($DistanceFeet * 1000.0, [System.MidpointRounding]::AwayFromZero)
    }

    return $score
}

function Resolve-CandidateParameterNames {
    param(
        [Parameter(Mandatory = $true)][hashtable]$SourceParameters,
        [Parameter(Mandatory = $true)][hashtable]$TargetParameters
    )

    $blockedNames = @(
        'Family',
        'Family and Type',
        'Type',
        'Level',
        'Schedule Level',
        'Reference Level',
        'Workset',
        'Edited By',
        'Phase Created',
        'Phase Demolished',
        'Assembly Code',
        'Image'
    )

    $preferredExact = @('Mark', 'Comments')
    $names = New-Object System.Collections.Generic.List[string]

    foreach ($name in @($SourceParameters.Keys | Sort-Object)) {
        if ($blockedNames -contains $name) {
            continue
        }

        if (-not $TargetParameters.ContainsKey($name)) {
            continue
        }

        $target = $TargetParameters[$name]
        if ($target.IsReadOnly) {
            continue
        }

        if ($name.StartsWith('Mii_', [System.StringComparison]::OrdinalIgnoreCase) -or $preferredExact -contains $name) {
            $names.Add($name) | Out-Null
        }
    }

    return @($names | Select-Object -Unique)
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

$sourceInventory = Get-Inventory -FamilyName $SourceFamilyName -PayloadDocumentKey $payloadDocumentKey -TargetDocument $targetDocument
$targetInventory = Get-Inventory -FamilyName $TargetFamilyName -PayloadDocumentKey $payloadDocumentKey -TargetDocument $targetDocument

$sourceItems = @($sourceInventory.Items)
$targetItems = @($targetInventory.Items)

if ($sourceItems.Count -eq 0) {
    throw "Khong tim thay instance nao cua source family '$SourceFamilyName'."
}

if ($targetItems.Count -eq 0) {
    throw "Khong tim thay instance nao cua target family '$TargetFamilyName'."
}

if ($sourceItems.Count -ne $targetItems.Count) {
    throw ("Source count {0} khac target count {1}. Chua an toan de copy batch." -f $sourceItems.Count, $targetItems.Count)
}

$sourceDetails = Get-ElementDetails -ElementIds @($sourceItems | ForEach-Object { [int]$_.ElementId }) -PayloadDocumentKey $payloadDocumentKey -TargetDocument $targetDocument
$targetDetails = Get-ElementDetails -ElementIds @($targetItems | ForEach-Object { [int]$_.ElementId }) -PayloadDocumentKey $payloadDocumentKey -TargetDocument $targetDocument

$sourceDetailMap = @{}
foreach ($item in @($sourceDetails.Items)) {
    $sourceDetailMap[[int]$item.ElementId] = $item
}

$targetDetailMap = @{}
foreach ($item in @($targetDetails.Items)) {
    $targetDetailMap[[int]$item.ElementId] = $item
}

$pairs = New-Object System.Collections.Generic.List[object]
$pairDiagnostics = New-Object System.Collections.Generic.List[object]

$sourceGroups = @($sourceItems | Group-Object { Get-RelaxedSignature -InventoryItem $_ } | Sort-Object Name)

foreach ($group in $sourceGroups) {
    $relaxedKeyString = [string]$group.Name
    $sourceGroup = @($group.Group)
    $targetGroup = @($targetItems | Where-Object { (Get-RelaxedSignature -InventoryItem $_) -eq $relaxedKeyString })

    if ($sourceGroup.Count -ne $targetGroup.Count) {
        $pairDiagnostics.Add([pscustomobject]@{
            Signature = $relaxedKeyString
            Status = 'group_count_mismatch'
            SourceCount = $sourceGroup.Count
            TargetCount = $targetGroup.Count
        }) | Out-Null
        continue
    }

    $orderedSources = @(
        $sourceGroup |
            Sort-Object `
                @{ Expression = {
                        $center = Get-ElementCenter -Element $sourceDetailMap[[int]$_.ElementId]
                        if ($null -eq $center) { [double]::PositiveInfinity } else { [double]$center.X }
                    } }, `
                @{ Expression = {
                        $center = Get-ElementCenter -Element $sourceDetailMap[[int]$_.ElementId]
                        if ($null -eq $center) { [double]::PositiveInfinity } else { [double]$center.Y }
                    } }, `
                @{ Expression = {
                        $center = Get-ElementCenter -Element $sourceDetailMap[[int]$_.ElementId]
                        if ($null -eq $center) { [double]::PositiveInfinity } else { [double]$center.Z }
                    } }, `
                @{ Expression = { [string]$_.Mark } }, `
                @{ Expression = { [int]$_.ElementId } }
    )

    $orderedTargets = @(
        $targetGroup |
            Sort-Object `
                @{ Expression = {
                        $center = Get-ElementCenter -Element $targetDetailMap[[int]$_.ElementId]
                        if ($null -eq $center) { [double]::PositiveInfinity } else { [double]$center.X }
                    } }, `
                @{ Expression = {
                        $center = Get-ElementCenter -Element $targetDetailMap[[int]$_.ElementId]
                        if ($null -eq $center) { [double]::PositiveInfinity } else { [double]$center.Y }
                    } }, `
                @{ Expression = {
                        $center = Get-ElementCenter -Element $targetDetailMap[[int]$_.ElementId]
                        if ($null -eq $center) { [double]::PositiveInfinity } else { [double]$center.Z }
                    } }, `
                @{ Expression = { [string]$_.Mark } }, `
                @{ Expression = { [int]$_.ElementId } }
    )

    for ($index = 0; $index -lt $orderedSources.Count; $index++) {
        $source = $orderedSources[$index]
        $target = $orderedTargets[$index]
        $sourceCenter = Get-ElementCenter -Element $sourceDetailMap[[int]$source.ElementId]
        $targetCenter = Get-ElementCenter -Element $targetDetailMap[[int]$target.ElementId]
        $distance = Get-PairDistance -SourceCenter $sourceCenter -TargetCenter $targetCenter

        $pairs.Add([pscustomobject]@{
            SourceElementId = [int]$source.ElementId
            TargetElementId = [int]$target.ElementId
            SourceTypeName = [string]$source.TypeName
            TargetTypeName = [string]$target.TypeName
            HostElementId = [string]$source.HostElementId
            LevelName = [string]$source.LevelName
            Mark = [string]$source.Mark
            MiiDiameter = [string]$source.MiiDiameter
            MiiDimLength = [string]$source.MiiDimLength
            DistanceFeet = $distance
            PairingScore = Get-MatchScore -SourceItem $source -TargetItem $target -DistanceFeet $distance
        }) | Out-Null
    }
}

$unmatchedSources = @($sourceItems | Where-Object {
    $sourceId = [int]$_.ElementId
    -not @($pairs | Where-Object { [int]$_.SourceElementId -eq $sourceId }).Count
})

$unmatchedTargets = @($targetItems | Where-Object {
    $targetId = [int]$_.ElementId
    -not @($pairs | Where-Object { [int]$_.TargetElementId -eq $targetId }).Count
})

$maxDistance = if ($pairs.Count -gt 0) { (@($pairs | ForEach-Object { [double]$_.DistanceFeet }) | Measure-Object -Maximum).Maximum } else { [double]::PositiveInfinity }

if ($unmatchedSources.Count -gt 0 -or $unmatchedTargets.Count -gt 0) {
    throw ("Pairing chua day du. UnmatchedSources={0}, UnmatchedTargets={1}" -f $unmatchedSources.Count, $unmatchedTargets.Count)
}

if ($maxDistance -gt $MaxPairDistanceFeet) {
    throw ("Pairing vuot tolerance. MaxDistanceFeet={0:0.####}, Allowed={1:0.####}" -f $maxDistance, $MaxPairDistanceFeet)
}

$firstPair = $pairs[0]
$firstSourceParams = Normalize-ParameterMap -Parameters @($sourceDetailMap[[int]$firstPair.SourceElementId].Parameters)
$firstTargetParams = Normalize-ParameterMap -Parameters @($targetDetailMap[[int]$firstPair.TargetElementId].Parameters)
$suggestedParameterNames = Resolve-CandidateParameterNames -SourceParameters $firstSourceParams -TargetParameters $firstTargetParams

$effectiveParameterNames = if ($ParameterNames.Count -gt 0) { @($ParameterNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) } else { @($suggestedParameterNames) }

if ($effectiveParameterNames.Count -eq 0) {
    throw "Khong resolve duoc parameter nao de copy. Hay truyen -ParameterNames ro rang."
}

$changes = New-Object System.Collections.Generic.List[object]
$changePreview = New-Object System.Collections.Generic.List[object]

foreach ($pair in $pairs) {
    $sourceParamMap = Normalize-ParameterMap -Parameters @($sourceDetailMap[[int]$pair.SourceElementId].Parameters)
    $targetParamMap = Normalize-ParameterMap -Parameters @($targetDetailMap[[int]$pair.TargetElementId].Parameters)

    foreach ($parameterName in $effectiveParameterNames) {
        if (-not $sourceParamMap.ContainsKey($parameterName)) {
            continue
        }
        if (-not $targetParamMap.ContainsKey($parameterName)) {
            continue
        }

        $sourceParam = $sourceParamMap[$parameterName]
        $targetParam = $targetParamMap[$parameterName]
        if ($targetParam.IsReadOnly) {
            continue
        }

        $sourceValue = [string]$sourceParam.Value
        $targetValue = [string]$targetParam.Value
        $willChange = -not [string]::Equals($sourceValue, $targetValue, [System.StringComparison]::Ordinal)
        $changePreview.Add([pscustomobject]@{
            SourceElementId = [int]$pair.SourceElementId
            TargetElementId = [int]$pair.TargetElementId
            ParameterName = $parameterName
            SourceValue = $sourceValue
            TargetBefore = $targetValue
            WillChange = $willChange
        }) | Out-Null

        if ($willChange) {
            $changes.Add([pscustomobject]@{
                ElementId = [int]$pair.TargetElementId
                ParameterName = $parameterName
                NewValue = $sourceValue
            }) | Out-Null
        }
    }
}

$preview = $null
$previewPayload = $null
$executeResponse = $null
$verification = @()

if ($changes.Count -gt 0) {
    $setPayload = @{
        DocumentKey = $payloadDocumentKey
        Changes = @($changes.ToArray())
    }

    $preview = Invoke-MutationPreview -Tool 'parameter.set_safe' -Payload $setPayload -TargetDocument $targetDocument
    $previewPayload = ConvertFrom-PayloadJson -Response $preview

    if ($Execute.IsPresent) {
        $executeResponse = Invoke-MutationExecute `
            -Tool 'parameter.set_safe' `
            -Payload $setPayload `
            -ApprovalToken $preview.ApprovalToken `
            -PreviewRunId $preview.PreviewRunId `
            -ExpectedContext $previewPayload.ResolvedContext `
            -TargetDocument $targetDocument
    }
}

$verifyTargetDetails = if ($Execute.IsPresent) {
    Get-ElementDetails -ElementIds @($targetItems | ForEach-Object { [int]$_.ElementId }) -PayloadDocumentKey $payloadDocumentKey -TargetDocument $targetDocument
}
else {
    $targetDetails
}

$verifyTargetDetailMap = @{}
foreach ($item in @($verifyTargetDetails.Items)) {
    $verifyTargetDetailMap[[int]$item.ElementId] = $item
}

foreach ($row in $changePreview) {
    $afterParams = Normalize-ParameterMap -Parameters @($verifyTargetDetailMap[[int]$row.TargetElementId].Parameters)
    $afterValue = if ($afterParams.ContainsKey([string]$row.ParameterName)) { [string]$afterParams[[string]$row.ParameterName].Value } else { '' }
    $verification += [pscustomobject]@{
        SourceElementId = [int]$row.SourceElementId
        TargetElementId = [int]$row.TargetElementId
        ParameterName = [string]$row.ParameterName
        SourceValue = [string]$row.SourceValue
        TargetBefore = [string]$row.TargetBefore
        TargetAfter = $afterValue
        WillChange = [bool]$row.WillChange
        MatchedAfter = [string]::Equals([string]$row.SourceValue, [string]$afterValue, [System.StringComparison]::Ordinal)
    }
}

$artifactDir = Join-Path $projectRoot ("artifacts\\penetration-parameter-copy\\{0}" -f ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    SourceFamilyName = $SourceFamilyName
    TargetFamilyName = $TargetFamilyName
    SourceCount = $sourceItems.Count
    TargetCount = $targetItems.Count
    PairCount = $pairs.Count
    MaxPairDistanceFeet = $maxDistance
    ParameterNames = @($effectiveParameterNames)
    ChangeCount = $changes.Count
    VerificationMatchedAfterCount = @($verification | Where-Object { $_.MatchedAfter }).Count
    VerificationTotalCount = @($verification).Count
    Executed = [bool]$Execute.IsPresent
    PreviewStatus = if ($preview) { [string]$preview.StatusCode } else { '' }
    ExecuteStatus = if ($executeResponse) { [string]$executeResponse.StatusCode } else { '' }
    ArtifactDirectory = $artifactDir
}

$summary | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'summary.json') -Encoding UTF8
$sourceInventory | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $artifactDir 'source-inventory.json') -Encoding UTF8
$targetInventory | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $artifactDir 'target-inventory.json') -Encoding UTF8
$pairs | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'pairs.json') -Encoding UTF8
$changePreview | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'change-preview.json') -Encoding UTF8
$verification | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'verification.json') -Encoding UTF8
$pairDiagnostics | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'pair-diagnostics.json') -Encoding UTF8
if ($preview) {
    $preview | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $artifactDir 'preview.json') -Encoding UTF8
}
if ($executeResponse) {
    $executeResponse | ConvertTo-Json -Depth 100 | Set-Content -Path (Join-Path $artifactDir 'execute.json') -Encoding UTF8
}

$report = [ordered]@{
    DocumentTitle = [string]$doc.Title
    SourceFamilyName = $SourceFamilyName
    TargetFamilyName = $TargetFamilyName
    SourceCount = $sourceItems.Count
    TargetCount = $targetItems.Count
    PairCount = $pairs.Count
    MaxPairDistanceFeet = $maxDistance
    SuggestedParameterNames = @($suggestedParameterNames)
    EffectiveParameterNames = @($effectiveParameterNames)
    ChangeCount = $changes.Count
    Executed = [bool]$Execute.IsPresent
    PreviewStatus = if ($preview) { [string]$preview.StatusCode } else { '' }
    ExecuteStatus = if ($executeResponse) { [string]$executeResponse.StatusCode } else { '' }
    ArtifactDirectory = $artifactDir
    PairSample = @($pairs | Select-Object -First 10)
}

if ($VerboseReport.IsPresent) {
    $report.ChangePreviewSample = @($changePreview | Select-Object -First 50)
    $report.VerificationSample = @($verification | Select-Object -First 50)
}

$report | ConvertTo-Json -Depth 50
