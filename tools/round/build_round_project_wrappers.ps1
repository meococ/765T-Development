param(
    [string]$BridgeExe = "",
    [string]$DocumentSelector = "",
    [string]$OutputDirectory = "",
    [switch]$NoLoadIntoProject,
    [string]$WrapperFamilyName = "Round_Project",
    [string]$WrapperFamilySuffix = "",
    [switch]$GenerateSizeSpecificVariants,
    [string]$PlanWrapperTypeName = "AXIS_X",
    [string]$ElevXWrapperTypeName = "AXIS_Z",
    [string]$ElevYWrapperTypeName = "AXIS_Y"
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

$doc = if ([string]::IsNullOrWhiteSpace($DocumentSelector)) {
    ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')
}
else {
    [pscustomobject]@{
        DocumentKey = $DocumentSelector
        Title = $DocumentSelector
        PathName = ''
        IsActive = $false
        IsModified = $null
        IsWorkshared = $null
        IsLinked = $null
        IsFamilyDocument = $null
        CanSave = $null
        CanSynchronize = $null
    }
}

$docKey = [string]$doc.DocumentKey
$docSelector = if (-not [string]::IsNullOrWhiteSpace($DocumentSelector)) { $DocumentSelector } elseif (-not [string]::IsNullOrWhiteSpace([string]$doc.Title)) { [string]$doc.Title } else { $docKey }
$payloadDocumentKey = if (-not [string]::IsNullOrWhiteSpace([string]$doc.Title)) { [string]$doc.Title } else { $docSelector }
$targetDocument = $docSelector

function Resolve-WrapperFamilyName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseName
    )

    if ([string]::IsNullOrWhiteSpace($WrapperFamilySuffix)) {
        return $BaseName
    }

    if ($BaseName.EndsWith($WrapperFamilySuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $BaseName
    }

    return ($BaseName + $WrapperFamilySuffix)
}

function Convert-LengthStringToSizeToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $text = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return '0'
    }

    $sign = 1.0
    if ($text.StartsWith('-')) {
        $sign = -1.0
        $text = $text.Substring(1).Trim()
    }

    $feet = 0.0
    $feetMarker = $text.IndexOf("'")
    if ($feetMarker -ge 0) {
        $feetPart = $text.Substring(0, $feetMarker).Trim()
        if (-not [string]::IsNullOrWhiteSpace($feetPart)) {
            $feet = [double]::Parse($feetPart, [System.Globalization.CultureInfo]::InvariantCulture)
        }
        $text = $text.Substring($feetMarker + 1).Trim()
    }

    $text = $text.Replace('"', '').Trim()
    if ($text.StartsWith('-')) {
        $text = $text.Substring(1).Trim()
    }

    $inches = 0.0
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        foreach ($token in ($text -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            if ($token.Contains('/')) {
                $parts = $token.Split('/')
                if ($parts.Length -eq 2) {
                    $inches += ([double]::Parse($parts[0], [System.Globalization.CultureInfo]::InvariantCulture) / [double]::Parse($parts[1], [System.Globalization.CultureInfo]::InvariantCulture))
                }
            }
            else {
                $inches += [double]::Parse($token, [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
    }

    $totalFeet = $sign * ($feet + ($inches / 12.0))
    $tokenValue = [Math]::Round($totalFeet * 12.0 * 256.0, [System.MidpointRounding]::AwayFromZero)
    return ([int]$tokenValue).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Resolve-VariantFamilyName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseName,
        [Parameter(Mandatory = $true)]
        [string]$LengthValue,
        [Parameter(Mandatory = $true)]
        [string]$DiameterValue
    )

    $resolvedBaseName = Resolve-WrapperFamilyName -BaseName $BaseName
    return $resolvedBaseName
}

function Resolve-VariantTypeName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseTypeName,
        [Parameter(Mandatory = $true)]
        [string]$LengthValue,
        [Parameter(Mandatory = $true)]
        [string]$DiameterValue
    )

    if (-not $GenerateSizeSpecificVariants.IsPresent) {
        return $BaseTypeName
    }

    return ('{0}__L{1}__D{2}' -f $BaseTypeName, (Convert-LengthStringToSizeToken -Value $LengthValue), (Convert-LengthStringToSizeToken -Value $DiameterValue))
}

$planFamilyName = Resolve-WrapperFamilyName -BaseName $WrapperFamilyName
$elevXFamilyName = Resolve-WrapperFamilyName -BaseName $WrapperFamilyName
$elevYFamilyName = Resolve-WrapperFamilyName -BaseName $WrapperFamilyName

$payload = @{
    DocumentKey = $payloadDocumentKey
    SourceFamilyName = 'Round'
    OutputDirectory = $OutputDirectory
    OverwriteFamilyFiles = $true
    LoadIntoProject = (-not $NoLoadIntoProject.IsPresent)
    OverwriteExistingProjectFamilies = $true
    PlanWrapperFamilyName = $planFamilyName
    PlanWrapperTypeName = $PlanWrapperTypeName
    ElevXWrapperFamilyName = $elevXFamilyName
    ElevXWrapperTypeName = $ElevXWrapperTypeName
    ElevYWrapperFamilyName = $elevYFamilyName
    ElevYWrapperTypeName = $ElevYWrapperTypeName
    GenerateSizeSpecificVariants = [bool]$GenerateSizeSpecificVariants
}

$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\\round-wrapper-build\\{0}" -f $runId)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$preview = Invoke-MutationPreview -Tool 'family.build_round_project_wrappers_safe' -Payload $payload -TargetDocument $targetDocument
$previewPayload = ConvertFrom-PayloadJson -Response $preview
$executed = Invoke-MutationExecute -Tool 'family.build_round_project_wrappers_safe' -Payload $payload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $targetDocument
$execPayload = ConvertFrom-PayloadJson -Response $executed

$targets = @()
if ($GenerateSizeSpecificVariants.IsPresent) {
    $planRoot = Join-Path $projectRoot 'artifacts\round-externalization-plan-run'
    $latestPlanDir = Get-ChildItem -Path $planRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $latestPlanDir) {
        throw "Khong tim thay plan artifact de verify size-specific wrapper."
    }

    $plan = Get-Content -Path (Join-Path $latestPlanDir.FullName 'plan.json') -Raw | ConvertFrom-Json
    $seenTargets = @{}
    foreach ($item in @($plan.Items | Where-Object { [bool]$_.CanExternalize })) {
        $actualTypeName = [string]$item.ProposedTargetTypeName
        switch -Regex ($actualTypeName) {
            '^AXIS_X(?:__|$)' {
                $baseFamily = $planFamilyName
                $baseTypeName = $PlanWrapperTypeName
                break
            }
            '^AXIS_Y(?:__|$)' {
                $baseFamily = $elevYFamilyName
                $baseTypeName = $ElevYWrapperTypeName
                break
            }
            '^AXIS_Z(?:__|$)' {
                $baseFamily = $elevXFamilyName
                $baseTypeName = $ElevXWrapperTypeName
                break
            }
            default {
                $baseFamily = [string]$item.ProposedTargetFamilyName
                $baseTypeName = $actualTypeName
            }
        }

        $familyName = Resolve-VariantFamilyName -BaseName $baseFamily -LengthValue ([string]$item.ParentMiiDimLength) -DiameterValue ([string]$item.ParentMiiDiameter)
        $typeName = Resolve-VariantTypeName -BaseTypeName $baseTypeName -LengthValue ([string]$item.ParentMiiDimLength) -DiameterValue ([string]$item.ParentMiiDiameter)
        $key = '{0}|{1}' -f $familyName, $typeName
        if ($seenTargets.ContainsKey($key)) {
            continue
        }
        $seenTargets[$key] = $true
        $targets += @{ FamilyName = $familyName; TypeName = $typeName }
    }
}
else {
    $targets = @(
        @{ FamilyName = $planFamilyName; TypeName = $PlanWrapperTypeName },
        @{ FamilyName = $elevXFamilyName; TypeName = $ElevXWrapperTypeName },
        @{ FamilyName = $elevYFamilyName; TypeName = $ElevYWrapperTypeName }
    )
}

$verification = @()
foreach ($target in $targets) {
    $typePayload = @{
        DocumentKey = $payloadDocumentKey
        CategoryNames = @('Generic Models')
        ClassName = ''
        NameContains = $target.TypeName
        IncludeParameters = $false
        OnlyInUse = $false
        MaxResults = 200
    }

    $typeResponse = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'type.list_element_types' -Payload $typePayload -TargetDocument $targetDocument)
    $match = @($typeResponse.Items | Where-Object {
        [string]$_.FamilyName -eq $target.FamilyName -and [string]$_.TypeName -eq $target.TypeName
    })

    $verification += [pscustomobject]@{
        FamilyName = $target.FamilyName
        TypeName = $target.TypeName
        FoundCount = $match.Count
        TypeIds = @($match | ForEach-Object { [int]$_.TypeId })
    }
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    DocumentTitle = [string]$doc.Title
    DocumentKey = $docKey
    OutputDirectory = ($execPayload.Artifacts | Where-Object { $_ -like 'outputDirectory=*' } | Select-Object -First 1)
    LoadIntoProject = (-not $NoLoadIntoProject.IsPresent)
    WrapperFamilySuffix = $WrapperFamilySuffix
    GenerateSizeSpecificVariants = [bool]$GenerateSizeSpecificVariants
    ChangedIds = @($execPayload.ChangedIds)
    Verification = $verification
}

Write-JsonFile -Path (Join-Path $artifactDir 'document.json') -Data $doc
Write-JsonFile -Path (Join-Path $artifactDir 'preview.json') -Data $preview
Write-JsonFile -Path (Join-Path $artifactDir 'execute.json') -Data $executed
Write-JsonFile -Path (Join-Path $artifactDir 'verification.json') -Data $verification
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary

$summary | ConvertTo-Json -Depth 50
