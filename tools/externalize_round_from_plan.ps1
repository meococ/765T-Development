param(
    [string]$BridgeExe = "",
    [string]$PlanArtifactDir = "",
    [int[]]$RoundElementIds = @(),
    [int]$MaxItems = 0,
    [string]$CommentValue = "NEW",
    [switch]$SkipAxisAudit,
    [switch]$SkipParameterUpdate,
    [string]$WrapperFamilySuffix = "",
    [switch]$UseSizeSpecificVariants
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

function Get-RateLimitDelaySeconds {
    param([object]$Response)

    $messages = @($Response.Diagnostics | ForEach-Object { [string]$_.Message })
    foreach ($message in $messages) {
        if ($message -match 'retry after\s+(\d+)s') {
            return ([int]$Matches[1] + 1)
        }
    }

    return 5
}

function Invoke-ReadTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [object]$Payload = $null,
        [string]$TargetDocument = ""
    )

    $payloadJson = if ($null -eq $Payload) { "" } else { $Payload | ConvertTo-Json -Depth 100 }
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson
        if ($null -ne $response -and [string]$response.StatusCode -eq 'RATE_LIMITED' -and $attempt -lt 10) {
            $delaySeconds = Get-RateLimitDelaySeconds -Response $response
            Write-Host ("[{0}] RATE_LIMITED on {1}. Sleep {2}s then retry {3}/10..." -f (Get-Date -Format 'HH:mm:ss'), $Tool, $delaySeconds, ($attempt + 1)) -ForegroundColor Yellow
            Start-Sleep -Seconds $delaySeconds
            continue
        }

        Assert-BridgeSuccess -Response $response -Tool $Tool
        return $response
    }
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
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson -DryRun
        if ($null -ne $response -and [string]$response.StatusCode -eq 'RATE_LIMITED' -and $attempt -lt 10) {
            $delaySeconds = Get-RateLimitDelaySeconds -Response $response
            Write-Host ("[{0}] RATE_LIMITED on {1} preview. Sleep {2}s then retry {3}/10..." -f (Get-Date -Format 'HH:mm:ss'), $Tool, $delaySeconds, ($attempt + 1)) -ForegroundColor Yellow
            Start-Sleep -Seconds $delaySeconds
            continue
        }

        Assert-BridgeSuccess -Response $response -Tool $Tool
        return $response
    }
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
        for ($attempt = 1; $attempt -le 10; $attempt++) {
            $raw = & $BridgeExe @args
            if (-not $raw) {
                throw "Bridge tra ve rong cho tool $Tool khi execute."
            }
            $response = $raw | ConvertFrom-Json
            if ($null -ne $response -and [string]$response.StatusCode -eq 'RATE_LIMITED' -and $attempt -lt 10) {
                $delaySeconds = Get-RateLimitDelaySeconds -Response $response
                Write-Host ("[{0}] RATE_LIMITED on {1} execute. Sleep {2}s then retry {3}/10..." -f (Get-Date -Format 'HH:mm:ss'), $Tool, $delaySeconds, ($attempt + 1)) -ForegroundColor Yellow
                Start-Sleep -Seconds $delaySeconds
                continue
            }

            Assert-BridgeSuccess -Response $response -Tool $Tool
            return $response
        }
    }
    finally {
        Remove-Item $tmpPayload -Force -ErrorAction SilentlyContinue
        if ($tmpExpectedContext) {
            Remove-Item $tmpExpectedContext -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-ElementExplain {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [int]$ElementId
    )

    $payload = @{
        DocumentKey = $DocumentKey
        ElementId = $ElementId
        IncludeParameters = $true
        ParameterNames = @('Elevation from Level', 'Level', 'Comments')
        IncludeDependents = $false
        IncludeHostRelations = $true
    }

    return (ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.explain' -Payload $payload -TargetDocument $DocumentKey))
}

function Get-ElementSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [int]$ElementId,
        [switch]$IncludeParameters
    )

    $payload = @{
        DocumentKey = $DocumentKey
        ElementIds = @([int]$ElementId)
        ViewScopeOnly = $false
        MaxResults = 1
        IncludeParameters = [bool]$IncludeParameters
    }

    $response = Invoke-ReadTool -Tool 'element.query' -Payload $payload -TargetDocument $DocumentKey
    $data = ConvertFrom-PayloadJson -Response $response
    if ($null -eq $data -or $null -eq $data.Items) {
        return $null
    }

    return @($data.Items | Select-Object -First 1)[0]
}

function Resolve-OutermostContainer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [int]$ElementId
    )

    $visited = New-Object 'System.Collections.Generic.HashSet[int]'
    $currentId = [int]$ElementId
    $lastExplain = $null

    while ($currentId -gt 0 -and $visited.Add($currentId)) {
        $lastExplain = Get-ElementExplain -DocumentKey $DocumentKey -ElementId $currentId
        if ($null -eq $lastExplain.SuperComponentElementId -or [int]$lastExplain.SuperComponentElementId -le 0) {
            break
        }

        $currentId = [int]$lastExplain.SuperComponentElementId
    }

    return [pscustomobject]@{
        ElementId = $currentId
        Explain = $lastExplain
    }
}

function Get-LatestPlanArtifactDir {
    $root = Join-Path $projectRoot 'artifacts\round-externalization-plan-run'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    $dir = Get-ChildItem -Path $root -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $dir) {
        throw "Khong tim thay plan artifact trong $root"
    }

    return $dir.FullName
}

function Get-BoundingBoxLength {
    param(
        [Parameter(Mandatory = $true)]
        [object]$BoundingBox
    )

    $dx = [Math]::Abs([double]$BoundingBox.MaxX - [double]$BoundingBox.MinX)
    $dy = [Math]::Abs([double]$BoundingBox.MaxY - [double]$BoundingBox.MinY)
    $dz = [Math]::Abs([double]$BoundingBox.MaxZ - [double]$BoundingBox.MinZ)
    return [Math]::Max($dx, [Math]::Max($dy, $dz))
}

function Get-ElementLocationPoint {
    param([Parameter(Mandatory = $true)][object]$ElementData)

    $wrappedElement = if ($null -ne $ElementData) { $ElementData.PSObject.Properties['Element'] } else { $null }
    $elementSummary = if ($null -ne $wrappedElement) { $wrappedElement.Value } else { $ElementData }
    if ($null -eq $elementSummary) {
        return $null
    }

    if ($null -ne $elementSummary.LocationCurveStart -and $null -ne $elementSummary.LocationCurveEnd) {
        return [pscustomobject]@{
            X = ([double]$elementSummary.LocationCurveStart.X + [double]$elementSummary.LocationCurveEnd.X) / 2.0
            Y = ([double]$elementSummary.LocationCurveStart.Y + [double]$elementSummary.LocationCurveEnd.Y) / 2.0
            Z = ([double]$elementSummary.LocationCurveStart.Z + [double]$elementSummary.LocationCurveEnd.Z) / 2.0
        }
    }

    if ($null -eq $elementSummary.LocationPoint) {
        if ($null -ne $elementSummary.BoundingBox) {
            return [pscustomobject]@{
                X = ([double]$elementSummary.BoundingBox.MinX + [double]$elementSummary.BoundingBox.MaxX) / 2.0
                Y = ([double]$elementSummary.BoundingBox.MinY + [double]$elementSummary.BoundingBox.MaxY) / 2.0
                Z = ([double]$elementSummary.BoundingBox.MinZ + [double]$elementSummary.BoundingBox.MaxZ) / 2.0
            }
        }

        return $null
    }

    return [pscustomobject]@{
        X = [double]$elementSummary.LocationPoint.X
        Y = [double]$elementSummary.LocationPoint.Y
        Z = [double]$elementSummary.LocationPoint.Z
    }
}

function Get-ExplainParameterValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$ExplainResponse,
        [Parameter(Mandatory = $true)]
        [string]$ParameterName
    )

    $wrappedElement = if ($null -ne $ExplainResponse) { $ExplainResponse.PSObject.Properties['Element'] } else { $null }
    $elementSummary = if ($null -ne $wrappedElement) { $wrappedElement.Value } else { $ExplainResponse }
    if ($null -eq $elementSummary -or $null -eq $elementSummary.Parameters) {
        return ''
    }

    $parameter = @($elementSummary.Parameters | Where-Object { [string]$_.Name -eq $ParameterName } | Select-Object -First 1)[0]
    if ($null -eq $parameter) {
        return ''
    }

    return [string]$parameter.Value
}

function Try-ParseImperialLengthStringToFeet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $text = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    $sign = 1.0
    if ($text.StartsWith('-')) {
        $sign = -1.0
        $text = $text.Substring(1).Trim()
    }

    $text = $text -replace '\s*-\s*', ' '
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
    $inches = 0.0
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        foreach ($token in ($text -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            if ($token.Contains('/')) {
                $parts = $token.Split('/')
                if ($parts.Length -ne 2) {
                    return $null
                }

                $inches += ([double]::Parse($parts[0], [System.Globalization.CultureInfo]::InvariantCulture) / [double]::Parse($parts[1], [System.Globalization.CultureInfo]::InvariantCulture))
            }
            else {
                $inches += [double]::Parse($token, [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
    }

    return ($sign * ($feet + ($inches / 12.0)))
}

function Convert-FeetToImperialLengthString {
    param(
        [Parameter(Mandatory = $true)]
        [double]$ValueFeet
    )

    $signText = if ($ValueFeet -lt 0.0) { '-' } else { '' }
    $absFeet = [Math]::Abs($ValueFeet)
    $totalInches256 = [int][Math]::Round($absFeet * 12.0 * 256.0, [System.MidpointRounding]::AwayFromZero)
    $feetWhole = [int][Math]::Floor($totalInches256 / (12 * 256))
    $remaining256 = $totalInches256 - ($feetWhole * 12 * 256)
    $inchesWhole = [int][Math]::Floor($remaining256 / 256)
    $fraction256 = $remaining256 - ($inchesWhole * 256)

    if ($fraction256 -eq 0) {
        return ('{0}{1}'' - {2}"' -f $signText, $feetWhole, $inchesWhole)
    }

    $gcd = [System.Numerics.BigInteger]::GreatestCommonDivisor([System.Numerics.BigInteger]$fraction256, [System.Numerics.BigInteger]256)
    $numerator = [int]($fraction256 / [int]$gcd)
    $denominator = [int](256 / [int]$gcd)
    return ('{0}{1}'' - {2} {3}/{4}"' -f $signText, $feetWhole, $inchesWhole, $numerator, $denominator)
}

function Set-SingleParameterValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [int]$ElementId,
        [Parameter(Mandatory = $true)]
        [string]$ParameterName,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $setPayload = @{
        DocumentKey = $DocumentKey
        Changes = @(
            @{
                ElementId = [int]$ElementId
                ParameterName = $ParameterName
                NewValue = $Value
            }
        )
    }

    $preview = Invoke-MutationPreview -Tool 'parameter.set_safe' -Payload $setPayload -TargetDocument $DocumentKey
    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $null = Invoke-MutationExecute -Tool 'parameter.set_safe' -Payload $setPayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $DocumentKey
}

function Move-ElementToTargetPoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [int]$ElementId,
        [Parameter(Mandatory = $true)]
        [double]$TargetX,
        [Parameter(Mandatory = $true)]
        [double]$TargetY,
        [Parameter(Mandatory = $true)]
        [double]$TargetZ
    )

    $summary = Get-ElementSummary -DocumentKey $DocumentKey -ElementId $ElementId -IncludeParameters
    $locationPoint = Get-ElementLocationPoint -ElementData $summary
    if ($null -eq $locationPoint) {
        return [pscustomobject]@{
            Succeeded = $false
            Message = 'Khong doc duoc LocationPoint cua instance moi.'
            FinalPoint = $null
            DeltaX = 0.0
            DeltaY = 0.0
            DeltaZ = 0.0
        }
    }

    $currentElevationValue = Get-ExplainParameterValue -ExplainResponse $summary -ParameterName 'Elevation from Level'
    $currentElevationFeet = if ([string]::IsNullOrWhiteSpace($currentElevationValue)) { $null } else { Try-ParseImperialLengthStringToFeet -Value $currentElevationValue }
    if ($null -ne $currentElevationFeet) {
        $levelElevation = [double]$locationPoint.Z - [double]$currentElevationFeet
        $desiredElevationFeet = [double]$TargetZ - [double]$levelElevation
        if ([Math]::Abs($desiredElevationFeet - [double]$currentElevationFeet) -gt 1e-6) {
            $desiredElevationValue = Convert-FeetToImperialLengthString -ValueFeet $desiredElevationFeet
            Set-SingleParameterValue -DocumentKey $DocumentKey -ElementId $ElementId -ParameterName 'Elevation from Level' -Value $desiredElevationValue
            $summary = Get-ElementSummary -DocumentKey $DocumentKey -ElementId $ElementId -IncludeParameters
            $locationPoint = Get-ElementLocationPoint -ElementData $summary
            if ($null -eq $locationPoint) {
                return [pscustomobject]@{
                    Succeeded = $false
                    Message = 'Da set Elevation from Level nhung khong doc duoc LocationPoint moi.'
                    FinalPoint = $null
                    DeltaX = 0.0
                    DeltaY = 0.0
                    DeltaZ = 0.0
                }
            }
        }
    }

    $deltaX = [double]$TargetX - [double]$locationPoint.X
    $deltaY = [double]$TargetY - [double]$locationPoint.Y
    $deltaZ = [double]$TargetZ - [double]$locationPoint.Z
    $deltaLength = [Math]::Sqrt(($deltaX * $deltaX) + ($deltaY * $deltaY) + ($deltaZ * $deltaZ))
    if ($deltaLength -le 1e-6) {
        return [pscustomobject]@{
            Succeeded = $true
            Message = 'Instance da dung target point, khong can move.'
            FinalPoint = $locationPoint
            DeltaX = $deltaX
            DeltaY = $deltaY
            DeltaZ = $deltaZ
        }
    }

    $movePayload = @{
        DocumentKey = $DocumentKey
        ElementIds = @([int]$ElementId)
        DeltaX = [double]$deltaX
        DeltaY = [double]$deltaY
        DeltaZ = [double]$deltaZ
    }

    $preview = Invoke-MutationPreview -Tool 'element.move_safe' -Payload $movePayload -TargetDocument $DocumentKey
    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $null = Invoke-MutationExecute -Tool 'element.move_safe' -Payload $movePayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $DocumentKey

    $movedSummary = Get-ElementSummary -DocumentKey $DocumentKey -ElementId $ElementId -IncludeParameters
    $movedPoint = Get-ElementLocationPoint -ElementData $movedSummary
    return [pscustomobject]@{
        Succeeded = ($null -ne $movedPoint)
        Message = 'Da move instance ve target point sau khi place.'
        FinalPoint = $movedPoint
        DeltaX = $deltaX
        DeltaY = $deltaY
        DeltaZ = $deltaZ
    }
}

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

function Resolve-WrapperVariantFamilyName {
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

function Resolve-WrapperVariantTypeName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseTypeName,
        [Parameter(Mandatory = $true)]
        [string]$LengthValue,
        [Parameter(Mandatory = $true)]
        [string]$DiameterValue
    )

    if (-not $UseSizeSpecificVariants.IsPresent) {
        return $BaseTypeName
    }

    $lengthToken = Convert-LengthStringToSizeToken -Value $LengthValue
    $diameterToken = Convert-LengthStringToSizeToken -Value $DiameterValue
    return ('{0}__L{1}__D{2}' -f $BaseTypeName, $lengthToken, $diameterToken)
}

function Add-ParameterChangeIfValue {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Changes,
        [Parameter(Mandatory = $true)]
        [int]$ElementId,
        [Parameter(Mandatory = $true)]
        [string]$ParameterName,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($ParameterName) -or [string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $Changes.Add([pscustomobject]@{
        ElementId = $ElementId
        ParameterName = $ParameterName
        NewValue = $Value
    }) | Out-Null
}

function Get-ProjectedLengthOnAxis {
    param(
        [Parameter(Mandatory = $true)]
        [object]$BoundingBox,
        [Parameter(Mandatory = $true)]
        [object]$Axis
    )

    $dx = [Math]::Abs([double]$BoundingBox.MaxX - [double]$BoundingBox.MinX)
    $dy = [Math]::Abs([double]$BoundingBox.MaxY - [double]$BoundingBox.MinY)
    $dz = [Math]::Abs([double]$BoundingBox.MaxZ - [double]$BoundingBox.MinZ)

    return ([Math]::Abs([double]$Axis.X) * $dx) +
           ([Math]::Abs([double]$Axis.Y) * $dy) +
           ([Math]::Abs([double]$Axis.Z) * $dz)
}

function Get-WrapperSymbolMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [object[]]$PlanItems
    )

    $targets = @()
    $seenKeys = @{}
    foreach ($planItem in $PlanItems) {
        $familyName = Resolve-WrapperVariantFamilyName -BaseName ([string]$planItem.ProposedTargetFamilyName) -LengthValue ([string]$planItem.ParentMiiDimLength) -DiameterValue ([string]$planItem.ParentMiiDiameter)
        $typeName = Resolve-WrapperVariantTypeName -BaseTypeName ([string]$planItem.ProposedTargetTypeName) -LengthValue ([string]$planItem.ParentMiiDimLength) -DiameterValue ([string]$planItem.ParentMiiDiameter)
        if ([string]::IsNullOrWhiteSpace($familyName) -or [string]::IsNullOrWhiteSpace($typeName)) {
            continue
        }

        $key = '{0}|{1}' -f $familyName, $typeName
        if ($seenKeys.ContainsKey($key)) {
            continue
        }
        $seenKeys[$key] = $true

        $targets += [pscustomobject]@{
            FamilyName = $familyName
            TypeName = $typeName
        }
    }

    $map = @{}
    $missing = @()

    foreach ($target in $targets) {
        $payload = @{
            DocumentKey = $DocumentKey
            CategoryNames = @('Generic Models')
            ClassName = ''
            NameContains = $target.TypeName
            IncludeParameters = $false
            OnlyInUse = $false
            MaxResults = 200
        }

        $response = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'type.list_element_types' -Payload $payload -TargetDocument $DocumentKey)
        $match = $response.Items | Where-Object {
            [string]$_.FamilyName -eq $target.FamilyName -and [string]$_.TypeName -eq $target.TypeName
        } | Select-Object -First 1

        $key = '{0}|{1}' -f $target.FamilyName, $target.TypeName
        if ($null -eq $match) {
            $missing += [pscustomobject]@{
                FamilyName = $target.FamilyName
                TypeName = $target.TypeName
            }
            continue
        }

        $map[$key] = [int]$match.TypeId
    }

    return [pscustomobject]@{
        SymbolMap = $map
        MissingTargets = $missing
    }
}

if ([string]::IsNullOrWhiteSpace($PlanArtifactDir)) {
    $PlanArtifactDir = Get-LatestPlanArtifactDir
}

$PlanArtifactDir = (Resolve-Path $PlanArtifactDir).Path
$plan = Get-Content -Path (Join-Path $PlanArtifactDir 'plan.json') -Raw | ConvertFrom-Json
$doc = Get-Content -Path (Join-Path $PlanArtifactDir 'document.json') -Raw | ConvertFrom-Json
$docKey = [string]$plan.DocumentKey

$items = @($plan.Items)
if ($RoundElementIds.Count -gt 0) {
    $idSet = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($id in $RoundElementIds) { [void]$idSet.Add([int]$id) }
    $items = @($items | Where-Object { $idSet.Contains([int]$_.RoundElementId) })
}

if ($MaxItems -gt 0) {
    $items = @($items | Select-Object -First $MaxItems)
}

if ($items.Count -eq 0) {
    throw "Khong co item nao de externalize."
}

$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-externalization-execute\{0}" -f $runId)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$wrapperLookup = Get-WrapperSymbolMap -DocumentKey $docKey -PlanItems $items
if ($wrapperLookup.MissingTargets.Count -gt 0) {
    $missingSummary = @($wrapperLookup.MissingTargets | Sort-Object FamilyName, TypeName)
    Write-JsonFile -Path (Join-Path $artifactDir 'missing-wrapper-types.json') -Data $missingSummary
    throw ("Chua thay family Round moi trong project. Dang thieu: " + (($missingSummary | ForEach-Object { '{0}/{1}' -f $_.FamilyName, $_.TypeName }) -join ', '))
}

$wrapperSymbolMap = $wrapperLookup.SymbolMap

$results = New-Object System.Collections.Generic.List[object]
$createdIds = New-Object System.Collections.Generic.List[int]
$instanceParameterChanges = New-Object System.Collections.Generic.List[object]

foreach ($item in $items) {
    $roundId = [int]$item.RoundElementId
    $parentId = if ($null -ne $item.ParentElementId) { [int]$item.ParentElementId } else { 0 }
    if ($parentId -le 0) {
        $results.Add([pscustomobject]@{
            RoundElementId = $roundId
            ParentElementId = $null
            CreatedElementId = $null
            Succeeded = $false
            Status = 'SKIPPED_NO_PARENT'
            Message = 'Khong co ParentElementId.'
        }) | Out-Null
        continue
    }

    $resolvedTargetFamilyName = Resolve-WrapperVariantFamilyName -BaseName ([string]$item.ProposedTargetFamilyName) -LengthValue ([string]$item.ParentMiiDimLength) -DiameterValue ([string]$item.ParentMiiDiameter)
    $resolvedTargetTypeName = Resolve-WrapperVariantTypeName -BaseTypeName ([string]$item.ProposedTargetTypeName) -LengthValue ([string]$item.ParentMiiDimLength) -DiameterValue ([string]$item.ParentMiiDiameter)
    $targetKey = '{0}|{1}' -f $resolvedTargetFamilyName, $resolvedTargetTypeName
    $familySymbolId = $wrapperSymbolMap[$targetKey]
    if ($null -eq $familySymbolId) {
        $results.Add([pscustomobject]@{
            RoundElementId = $roundId
            ParentElementId = $parentId
            CreatedElementId = $null
            Succeeded = $false
            Status = 'SKIPPED_MISSING_TARGET_SYMBOL'
            Message = "Khong resolve duoc wrapper symbol cho $targetKey."
        }) | Out-Null
        continue
    }

    try {
        $sourceSummary = Get-ElementSummary -DocumentKey $docKey -ElementId $roundId
        $sourcePoint = Get-ElementLocationPoint -ElementData $sourceSummary
        if ($null -eq $sourcePoint) {
            $sourcePoint = [pscustomobject]@{
                X = [double]$item.Origin.X
                Y = [double]$item.Origin.Y
                Z = [double]$item.Origin.Z
            }
        }

        $placementPointX = [double]$sourcePoint.X
        $placementPointY = [double]$sourcePoint.Y
        $placementPointZ = [double]$sourcePoint.Z
        $newId = $null
        $placementStrategyUsed = ''
        $lastError = ''

        $placePayload = @{
            DocumentKey = $docKey
            FamilySymbolId = [int]$familySymbolId
            PlacementMode = 'auto'
            StructuralTypeName = 'NonStructural'
            X = $placementPointX
            Y = $placementPointY
            Z = $placementPointZ
            FaceNormalX = 0.0
            FaceNormalY = 0.0
            FaceNormalZ = 1.0
            ReferenceDirectionX = 1.0
            ReferenceDirectionY = 0.0
            ReferenceDirectionZ = 0.0
            RotateRadians = 0.0
            Notes = "Externalize Round clean project family old=$roundId parent=$parentId target=$resolvedTargetFamilyName/$resolvedTargetTypeName strategy=hostless_project_axes"
        }

        try {
            $preview = Invoke-MutationPreview -Tool 'element.place_family_instance_safe' -Payload $placePayload -TargetDocument $docKey
            $previewPayload = ConvertFrom-PayloadJson -Response $preview
            $executed = Invoke-MutationExecute -Tool 'element.place_family_instance_safe' -Payload $placePayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $docKey
            $execPayload = ConvertFrom-PayloadJson -Response $executed
            $changedIds = @($execPayload.ChangedIds)
            if ($changedIds.Count -gt 0) {
                $newId = [int]$changedIds[0]
            }
            if ($null -ne $newId) {
                $placementStrategyUsed = 'hostless_project_axes'
            }
        }
        catch {
            $lastError = $_.Exception.Message
        }

        if ($null -eq $newId) {
            $container = Resolve-OutermostContainer -DocumentKey $docKey -ElementId $parentId
            $containerId = [int]$container.ElementId

            $hostCandidates = New-Object System.Collections.Generic.List[int]
            if ($containerId -gt 0) { $hostCandidates.Add($containerId) | Out-Null }
            if ($parentId -gt 0 -and $parentId -ne $containerId) { $hostCandidates.Add($parentId) | Out-Null }
            if ($hostCandidates.Count -eq 0 -and $parentId -gt 0) {
                $hostCandidates.Add($parentId) | Out-Null
            }

            $bbox = if ($null -ne $sourceSummary) { $sourceSummary.BoundingBox } else { $null }
            $curveLength = if ($null -ne $bbox) { Get-ProjectedLengthOnAxis -BoundingBox $bbox -Axis $item.BasisX } else { 0.0 }
            if ($curveLength -le 1e-9 -and $null -ne $bbox) {
                $curveLength = Get-BoundingBoxLength -BoundingBox $bbox
            }

            $faceThickness = if ($null -ne $bbox) { Get-ProjectedLengthOnAxis -BoundingBox $bbox -Axis $item.BasisZ } else { 0.0 }
            $halfThickness = $faceThickness / 2.0
            $offsets = @(0.0)
            if ($halfThickness -gt 1e-9) {
                $offsets += @($halfThickness, -$halfThickness)
            }

            $placementCandidates = New-Object System.Collections.Generic.List[object]
            foreach ($offset in $offsets) {
                $candidateX = [double]$sourcePoint.X + ([double]$item.BasisZ.X * $offset)
                $candidateY = [double]$sourcePoint.Y + ([double]$item.BasisZ.Y * $offset)
                $candidateZ = [double]$sourcePoint.Z + ([double]$item.BasisZ.Z * $offset)
                $placementCandidates.Add([pscustomobject]@{
                    Label = "host_face_offset_$offset"
                    X = $candidateX
                    Y = $candidateY
                    Z = $candidateZ
                }) | Out-Null
            }

            $referenceStrategies = New-Object System.Collections.Generic.List[object]
            $referenceStrategies.Add([pscustomobject]@{
                Label = 'basisX_rot_0'
                RotateRadians = 0.0
                X = [double]$item.BasisX.X
                Y = [double]$item.BasisX.Y
                Z = [double]$item.BasisX.Z
            }) | Out-Null
            $referenceStrategies.Add([pscustomobject]@{
                Label = 'basisY_rot_m90'
                RotateRadians = -([Math]::PI / 2.0)
                X = [double]$item.BasisY.X
                Y = [double]$item.BasisY.Y
                Z = [double]$item.BasisY.Z
            }) | Out-Null
            $referenceStrategies.Add([pscustomobject]@{
                Label = 'basisY_rot_p90'
                RotateRadians = ([Math]::PI / 2.0)
                X = [double]$item.BasisY.X
                Y = [double]$item.BasisY.Y
                Z = [double]$item.BasisY.Z
            }) | Out-Null

            foreach ($placementHostId in $hostCandidates) {
                foreach ($candidate in $placementCandidates) {
                    foreach ($refStrategy in $referenceStrategies) {
                        $hostPayload = @{
                            DocumentKey = $docKey
                            FamilySymbolId = [int]$familySymbolId
                            HostElementId = [int]$placementHostId
                            PlacementMode = 'auto'
                            StructuralTypeName = 'NonStructural'
                            X = [double]$candidate.X
                            Y = [double]$candidate.Y
                            Z = [double]$candidate.Z
                            FaceNormalX = [double]$item.BasisZ.X
                            FaceNormalY = [double]$item.BasisZ.Y
                            FaceNormalZ = [double]$item.BasisZ.Z
                            ReferenceDirectionX = [double]$refStrategy.X
                            ReferenceDirectionY = [double]$refStrategy.Y
                            ReferenceDirectionZ = [double]$refStrategy.Z
                            RotateRadians = [double]$refStrategy.RotateRadians
                            Notes = "Externalize Round clean project family old=$roundId parent=$parentId target=$resolvedTargetFamilyName/$resolvedTargetTypeName strategy=host_face host=$placementHostId candidate=$($candidate.Label) ref=$($refStrategy.Label)"
                        }

                        try {
                            $preview = Invoke-MutationPreview -Tool 'element.place_family_instance_safe' -Payload $hostPayload -TargetDocument $docKey
                            $previewPayload = ConvertFrom-PayloadJson -Response $preview
                            $executed = Invoke-MutationExecute -Tool 'element.place_family_instance_safe' -Payload $hostPayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $docKey
                            $execPayload = ConvertFrom-PayloadJson -Response $executed
                            $changedIds = @($execPayload.ChangedIds)
                            if ($changedIds.Count -gt 0) {
                                $newId = [int]$changedIds[0]
                            }
                            if ($null -ne $newId) {
                                $placementPointX = [double]$candidate.X
                                $placementPointY = [double]$candidate.Y
                                $placementPointZ = [double]$candidate.Z
                                $placementStrategyUsed = ('host_face:{0}:{1}:{2}' -f $placementHostId, $candidate.Label, $refStrategy.Label)
                                break
                            }
                            $lastError = "Khong nhan duoc CreatedId tu element.place_family_instance_safe."
                        }
                        catch {
                            $lastError = $_.Exception.Message
                        }
                    }
                    if ($null -ne $newId) { break }
                }
                if ($null -ne $newId) { break }
            }
        }

        if ($null -eq $newId) {
            if ([string]::IsNullOrWhiteSpace($lastError)) {
                $lastError = "Khong nhan duoc CreatedId tu element.place_family_instance_safe."
            }
            throw $lastError
        }

        $moveResult = Move-ElementToTargetPoint -DocumentKey $docKey -ElementId ([int]$newId) -TargetX ([double]$sourcePoint.X) -TargetY ([double]$sourcePoint.Y) -TargetZ ([double]$sourcePoint.Z)
        if (-not $moveResult.Succeeded) {
            throw $moveResult.Message
        }

        if ($null -ne $moveResult.FinalPoint) {
            $placementPointX = [double]$moveResult.FinalPoint.X
            $placementPointY = [double]$moveResult.FinalPoint.Y
            $placementPointZ = [double]$moveResult.FinalPoint.Z
        }

        if (-not [string]::IsNullOrWhiteSpace($placementStrategyUsed)) {
            $placementStrategyUsed = $placementStrategyUsed + '|moved_to_target'
        }
        else {
            $placementStrategyUsed = 'moved_to_target'
        }

        $createdIds.Add([int]$newId) | Out-Null
        Add-ParameterChangeIfValue -Changes $instanceParameterChanges -ElementId ([int]$newId) -ParameterName 'Mii_DimDiameter' -Value ([string]$item.ParentMiiDiameter)
        Add-ParameterChangeIfValue -Changes $instanceParameterChanges -ElementId ([int]$newId) -ParameterName 'Mii_Diameter' -Value ([string]$item.ParentMiiDiameter)
        Add-ParameterChangeIfValue -Changes $instanceParameterChanges -ElementId ([int]$newId) -ParameterName 'Mii_DimLength' -Value ([string]$item.ParentMiiDimLength)
        Add-ParameterChangeIfValue -Changes $instanceParameterChanges -ElementId ([int]$newId) -ParameterName 'Length' -Value ([string]$item.ParentMiiDimLength)
        Add-ParameterChangeIfValue -Changes $instanceParameterChanges -ElementId ([int]$newId) -ParameterName 'Mii_ElementClass' -Value ([string]$item.ParentMiiElementClass)
        Add-ParameterChangeIfValue -Changes $instanceParameterChanges -ElementId ([int]$newId) -ParameterName 'Mii_ElementTier' -Value ([string]$item.ParentMiiElementTier)
        $results.Add([pscustomobject]@{
            RoundElementId = $roundId
            ParentElementId = $parentId
            CreatedElementId = [int]$newId
            Succeeded = $true
            Status = 'CREATED'
            Message = 'Tao clean Round project family thanh cong.'
            TargetFamilyName = $resolvedTargetFamilyName
            TargetTypeName = $resolvedTargetTypeName
            PlacementPointX = $placementPointX
            PlacementPointY = $placementPointY
            PlacementPointZ = $placementPointZ
            PlacementStrategy = $placementStrategyUsed
        }) | Out-Null
    }
    catch {
        $results.Add([pscustomobject]@{
            RoundElementId = $roundId
            ParentElementId = $parentId
            CreatedElementId = $null
            Succeeded = $false
            Status = 'CREATE_FAILED'
            Message = $_.Exception.Message
        }) | Out-Null
    }
}

$commentExecution = $null
if (-not $SkipParameterUpdate.IsPresent -and $createdIds.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($CommentValue)) {
    $commentChanges = @(
        $createdIds | ForEach-Object {
            @{
                ElementId = [int]$_
                ParameterName = 'Comments'
                NewValue = $CommentValue
            }
        }
    )

    foreach ($change in $commentChanges) {
        $instanceParameterChanges.Add($change) | Out-Null
    }
}

$parameterExecution = $null
if (-not $SkipParameterUpdate.IsPresent -and $instanceParameterChanges.Count -gt 0) {
    $setPayload = @{
        DocumentKey = $docKey
        Changes = @($instanceParameterChanges.ToArray())
    }

    $preview = Invoke-MutationPreview -Tool 'parameter.set_safe' -Payload $setPayload -TargetDocument $docKey
    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $parameterExecution = Invoke-MutationExecute -Tool 'parameter.set_safe' -Payload $setPayload -ApprovalToken $preview.ApprovalToken -PreviewRunId $preview.PreviewRunId -ExpectedContext $previewPayload.ResolvedContext -TargetDocument $docKey
    $commentExecution = $parameterExecution
}

$axisAudit = $null
$createdAudit = @()
if (-not $SkipAxisAudit) {
    $axisAuditPayload = @{
        DocumentKey = $docKey
        CategoryNames = @('Generic Models')
        AngleToleranceDegrees = 5.0
        TreatMirroredAsMismatch = $true
        TreatAntiParallelAsMismatch = $false
        HighlightInUi = $false
        IncludeAlignedItems = $true
        MaxElements = 10000
        MaxIssues = 10000
        ZoomToHighlighted = $false
        AnalyzeNestedFamilies = $true
        MaxFamilyDefinitionsToInspect = 200
        MaxNestedInstancesPerFamily = 500
        MaxNestedFindingsPerFamily = 100
        TreatNonSharedNestedAsRisk = $true
        TreatNestedMirroredAsRisk = $true
        TreatNestedRotatedAsRisk = $true
        TreatNestedTiltedAsRisk = $true
        IncludeNestedFindings = $false
        UseActiveViewOnly = $false
    }
    $axisAudit = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'review.family_axis_alignment_global' -Payload $axisAuditPayload -TargetDocument $docKey)
    $createdIdSet = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($id in $createdIds) { [void]$createdIdSet.Add([int]$id) }
    $createdAudit = @($axisAudit.Items | Where-Object { $createdIdSet.Contains([int]$_.ElementId) })
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    SourcePlanArtifactDir = $PlanArtifactDir
    DocumentTitle = [string]$plan.DocumentTitle
    DocumentKey = $docKey
    RequestedCount = $items.Count
    CreatedCount = $createdIds.Count
    FailedCount = @($results | Where-Object { -not $_.Succeeded }).Count
    CommentValue = $CommentValue
    SkipParameterUpdate = [bool]$SkipParameterUpdate
    ParameterUpdateApplied = ($null -ne $parameterExecution)
    ParameterUpdateChangeCount = $instanceParameterChanges.Count
    SkipAxisAudit = [bool]$SkipAxisAudit
    WrapperFamilySuffix = $WrapperFamilySuffix
    UseSizeSpecificVariants = [bool]$UseSizeSpecificVariants
    CreatedIds = @($createdIds)
    CreatedAxisStatusSummary = @($createdAudit | Group-Object Status | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{
            Status = $_.Name
            Count = $_.Count
        }
    })
}

Write-JsonFile -Path (Join-Path $artifactDir 'results.json') -Data $results
if ($null -ne $axisAudit) {
    Write-JsonFile -Path (Join-Path $artifactDir 'axis-audit.json') -Data $axisAudit
}
Write-JsonFile -Path (Join-Path $artifactDir 'created-axis-audit.json') -Data $createdAudit
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
$results | Export-Csv -Path (Join-Path $artifactDir 'results.csv') -NoTypeInformation -Encoding UTF8

$summary | ConvertTo-Json -Depth 50
