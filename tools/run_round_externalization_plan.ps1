param(
    [string]$BridgeExe = "",
    [string]$ParentFamilyName = "Penetration Alpha",
    [string]$RoundFamilyName = "Round",
    [string]$WrapperFamilyName = "Round_Project"
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

$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-externalization-plan-run\{0}" -f $runId)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

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

$docResponse = Invoke-ReadTool -Tool 'document.get_active'
$doc = ConvertFrom-PayloadJson -Response $docResponse

$payload = @{
    DocumentKey = $doc.DocumentKey
    ParentFamilyName = $ParentFamilyName
    RoundFamilyName = $RoundFamilyName
    MaxResults = 10000
    AngleToleranceDegrees = 5.0
    RequireParentFamilyMatch = $true
    TraceCommentPrefix = 'BIM765T_EXTERNAL_ROUND'
    PlanWrapperFamilyName = $WrapperFamilyName
    PlanWrapperTypeName = 'AXIS_X'
    ElevXWrapperFamilyName = $WrapperFamilyName
    ElevXWrapperTypeName = 'AXIS_Z'
    ElevYWrapperFamilyName = $WrapperFamilyName
    ElevYWrapperTypeName = 'AXIS_Y'
}

$planResponse = Invoke-ReadTool -Tool 'report.round_externalization_plan' -Payload $payload -TargetDocument $doc.DocumentKey
$plan = ConvertFrom-PayloadJson -Response $planResponse

$items = @($plan.Items)
$csvRows = @(
    $items | ForEach-Object {
        [pscustomobject]@{
            RoundElementId = [int]$_.RoundElementId
            RoundFamilyName = [string]$_.RoundFamilyName
            RoundTypeName = [string]$_.RoundTypeName
            RoundStatus = [string]$_.RoundStatus
            ProposedPlacementMode = [string]$_.ProposedPlacementMode
            PlacementNote = [string]$_.PlacementNote
            ProposedTargetFamilyName = [string]$_.ProposedTargetFamilyName
            ProposedTargetTypeName = [string]$_.ProposedTargetTypeName
            ParentElementId = if ($null -ne $_.ParentElementId) { [int]$_.ParentElementId } else { $null }
            ParentFamilyName = [string]$_.ParentFamilyName
            ParentTypeName = [string]$_.ParentTypeName
            ParentCategoryName = [string]$_.ParentCategoryName
            ParentMiiDiameter = [string]$_.ParentMiiDiameter
            ParentMiiDimLength = [string]$_.ParentMiiDimLength
            ParentMiiElementClass = [string]$_.ParentMiiElementClass
            ParentMiiElementTier = [string]$_.ParentMiiElementTier
            ParentMark = [string]$_.ParentMark
            OriginX = [double]$_.Origin.X
            OriginY = [double]$_.Origin.Y
            OriginZ = [double]$_.Origin.Z
            BasisXX = [double]$_.BasisX.X
            BasisXY = [double]$_.BasisX.Y
            BasisXZ = [double]$_.BasisX.Z
            BasisYX = [double]$_.BasisY.X
            BasisYY = [double]$_.BasisY.Y
            BasisYZ = [double]$_.BasisY.Z
            BasisZX = [double]$_.BasisZ.X
            BasisZY = [double]$_.BasisZ.Y
            BasisZZ = [double]$_.BasisZ.Z
            RotationAroundProjectZDegrees = [double]$_.RotationAroundProjectZDegrees
            AngleXDegrees = [double]$_.AngleXDegrees
            AngleYDegrees = [double]$_.AngleYDegrees
            AngleZDegrees = [double]$_.AngleZDegrees
            CanExternalize = [bool]$_.CanExternalize
            Notes = [string]$_.Notes
            SuggestedTraceComment = [string]$_.SuggestedTraceComment
        }
    }
)

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    DocumentTitle = [string]$plan.DocumentTitle
    DocumentKey = [string]$plan.DocumentKey
    ParentFamilyName = [string]$plan.ParentFamilyName
    RoundFamilyName = [string]$plan.RoundFamilyName
    TotalRoundInstances = [int]$plan.TotalRoundInstances
    Count = [int]$plan.Count
    EligibleCount = [int]$plan.EligibleCount
    MissingParentCount = [int]$plan.MissingParentCount
    UnexpectedParentCount = [int]$plan.UnexpectedParentCount
    MissingTransformCount = [int]$plan.MissingTransformCount
    UniqueParentInstanceCount = [int]$plan.UniqueParentInstanceCount
    Truncated = [bool]$plan.Truncated
    ModeSummary = @($plan.ModeSummary)
    TypeSummary = @($plan.TypeSummary)
}

$csvPath = Join-Path $artifactDir 'round-externalization-plan.csv'
Write-JsonFile -Path (Join-Path $artifactDir 'document.json') -Data $doc
Write-JsonFile -Path (Join-Path $artifactDir 'plan.json') -Data $plan
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
$csvRows | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8

$summary | ConvertTo-Json -Depth 50
