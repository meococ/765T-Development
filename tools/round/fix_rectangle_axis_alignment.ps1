param(
    [string]$FamilyName = 'Mii_Pen-Rectangle',
    [string]$CommentPrefix = 'RECT_AXIS_REVIEW',
    [switch]$Execute,
    [switch]$WriteComments,
    [switch]$SaveDocument
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$bridge = Resolve-BridgeExe
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifactDir = Join-Path (Join-Path (Resolve-ProjectRoot) 'artifacts\rectangle-axis-fix') $runId
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

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
        [string]$TargetDocument = ''
    )

    $payloadJson = if ($null -eq $Payload) { '' } else { $Payload | ConvertTo-Json -Depth 50 }
    $response = Invoke-BridgeJson -BridgeExe $bridge -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-GuardedMutation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [object]$Payload,
        [string]$TargetDocument = ''
    )

    $payloadJson = $Payload | ConvertTo-Json -Depth 50
    $dryRun = Invoke-BridgeJson -BridgeExe $bridge -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson -DryRun
    Assert-BridgeSuccess -Response $dryRun -Tool ($Tool + ' dry-run')

    $payloadData = ConvertFrom-PayloadJson -Response $dryRun
    if ($null -eq $payloadData -or $null -eq $payloadData.ResolvedContext) {
        throw "Tool $Tool khong tra ve ResolvedContext o dry-run."
    }

    $payloadFile = Join-Path $env:TEMP ('bim765t_rect_payload_' + [Guid]::NewGuid().ToString('N') + '.json')
    $contextFile = Join-Path $env:TEMP ('bim765t_rect_context_' + [Guid]::NewGuid().ToString('N') + '.json')

    try {
        Set-Content -Path $payloadFile -Value $payloadJson -Encoding UTF8
        Set-Content -Path $contextFile -Value ($payloadData.ResolvedContext | ConvertTo-Json -Depth 10 -Compress) -Encoding UTF8

        $args = @(
            $Tool,
            '--target-document', $TargetDocument,
            '--payload', $payloadFile,
            '--dry-run', 'false',
            '--approval-token', $dryRun.ApprovalToken,
            '--preview-run-id', $dryRun.PreviewRunId,
            '--expected-context', $contextFile
        )

        $raw = & $bridge @args
        if (-not $raw) {
            throw "Bridge tra ve rong khi execute $Tool"
        }

        $result = $raw | ConvertFrom-Json
        Assert-BridgeSuccess -Response $result -Tool ($Tool + ' execute')
        return $result
    }
    finally {
        Remove-Item $payloadFile -Force -ErrorAction SilentlyContinue
        Remove-Item $contextFile -Force -ErrorAction SilentlyContinue
    }
}

function Get-ToolCatalog {
    $response = Invoke-ReadTool -Tool 'session.list_tools'
    return (ConvertFrom-PayloadJson -Response $response).Tools
}

function New-AxisPayload {
    return [ordered]@{
        UseActiveViewOnly = $false
        IncludeAlignedItems = $true
        TreatMirroredAsMismatch = $true
        AngleToleranceDegrees = 1.0
        MaxElements = 10000
        MaxIssues = 1000
        AnalyzeNestedFamilies = $false
        HighlightInUi = $false
    }
}

function Is-NearZero([double]$value, [double]$tol = 0.01) {
    return [Math]::Abs($value) -le $tol
}

function Is-NearOne([double]$value, [double]$tol = 0.01) {
    return [Math]::Abs([Math]::Abs($value) - 1.0) -le $tol
}

function Get-RectangleClassification {
    param([Parameter(Mandatory = $true)][object]$Item)

    $basisXz = [double]$Item.BasisX.Z
    $basisYz = [double]$Item.BasisY.Z
    $basisZz = [double]$Item.BasisZ.Z
    $rotZ = [double]$Item.RotationAroundProjectZDegrees

    $classification = [ordered]@{
        ElementId = [int]$Item.ElementId
        TypeName = [string]$Item.TypeName
        RawStatus = [string]$Item.Status
        ProjectAxisStatus = [string]$Item.ProjectAxisStatus
        RotationAroundProjectZDegrees = $rotZ
        BasisX = $Item.BasisX
        BasisY = $Item.BasisY
        BasisZ = $Item.BasisZ
        EffectiveStatus = 'REVIEW_OTHER'
        NeedsFix = $false
        RecommendedAngleDegrees = $null
        AxisMode = 'element_basis_z'
        ReadyForExport = $false
        Reason = [string]$Item.Reason
    }

    if ([string]$Item.Status -eq 'ALIGNED') {
        $classification.EffectiveStatus = 'PROJECT_OK'
        $classification.ReadyForExport = $true
        return [pscustomobject]$classification
    }

    if ([string]$Item.Status -eq 'ROTATED_IN_VIEW' -and (Is-NearOne $basisZz)) {
        $classification.EffectiveStatus = 'FIX_ROTATE_IN_VIEW'
        $classification.NeedsFix = $true
        $classification.RecommendedAngleDegrees = -1.0 * $rotZ
        return [pscustomobject]$classification
    }

    if ([string]$Item.Status -eq 'TILTED_OUT_OF_PROJECT_Z' -and (Is-NearZero $basisZz)) {
        $basisXVertical = Is-NearOne $basisXz
        $basisYVertical = Is-NearOne $basisYz

        if ($basisXVertical -and (-not $basisYVertical)) {
            $classification.EffectiveStatus = 'VERTICAL_FACE_OK'
            $classification.ReadyForExport = $true
            return [pscustomobject]$classification
        }

        if ($basisYVertical -and (-not $basisXVertical)) {
            $classification.EffectiveStatus = 'FIX_ROTATE_IN_VERTICAL_FACE'
            $classification.NeedsFix = $true
            $classification.RecommendedAngleDegrees = if ($basisYz -gt 0) { -90.0 } else { 90.0 }
            return [pscustomobject]$classification
        }
    }

    return [pscustomobject]$classification
}

function Get-RectangleReview {
    param([Parameter(Mandatory = $true)][string]$DocumentKey)

    $axisResponse = Invoke-ReadTool -Tool 'review.family_axis_alignment' -Payload (New-AxisPayload) -TargetDocument $DocumentKey
    $axisData = ConvertFrom-PayloadJson -Response $axisResponse
    $items = @($axisData.Items | Where-Object { [string]$_.FamilyName -eq $FamilyName })
    $classified = @($items | ForEach-Object { Get-RectangleClassification -Item $_ })

    return [pscustomobject]@{
        AxisData = $axisData
        Items = $classified
        RawItems = $items
    }
}

function Write-RectangleComments {
    param(
        [Parameter(Mandatory = $true)][string]$DocumentKey,
        [Parameter(Mandatory = $true)][object[]]$Items
    )

    $changes = foreach ($item in $Items) {
        $statusText = if ($item.ReadyForExport) { 'OK-Ready to export' } else { 'REVIEW' }
        [ordered]@{
            ElementId = [int]$item.ElementId
            ParameterName = 'Comments'
            NewValue = ('{0}|CLASS={1}|RAW={2}|STATUS={3}' -f $CommentPrefix, $item.EffectiveStatus, $item.RawStatus, $statusText)
        }
    }

    if (@($changes).Count -eq 0) {
        return $null
    }

    return Invoke-GuardedMutation -Tool 'parameter.set_safe' -Payload @{ DocumentKey = $DocumentKey; Changes = @($changes) } -TargetDocument $DocumentKey
}

$doc = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')
$toolCatalog = @(Get-ToolCatalog)
$hasRotateTool = @($toolCatalog | Where-Object { [string]$_.ToolName -eq 'element.rotate_safe' }).Count -gt 0

$preReview = Get-RectangleReview -DocumentKey $doc.DocumentKey
$preSummary = [ordered]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    FamilyName = $FamilyName
    RuntimeHasRotateTool = $hasRotateTool
    TotalItems = @($preReview.Items).Count
    ProjectOkCount = @($preReview.Items | Where-Object { $_.EffectiveStatus -eq 'PROJECT_OK' }).Count
    VerticalFaceOkCount = @($preReview.Items | Where-Object { $_.EffectiveStatus -eq 'VERTICAL_FACE_OK' }).Count
    FixRotateInVerticalFaceCount = @($preReview.Items | Where-Object { $_.EffectiveStatus -eq 'FIX_ROTATE_IN_VERTICAL_FACE' }).Count
    FixRotateInViewCount = @($preReview.Items | Where-Object { $_.EffectiveStatus -eq 'FIX_ROTATE_IN_VIEW' }).Count
    ReviewOtherCount = @($preReview.Items | Where-Object { $_.EffectiveStatus -eq 'REVIEW_OTHER' }).Count
    ReadyForExportCount = @($preReview.Items | Where-Object { $_.ReadyForExport }).Count
}

$preReview | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'pre-review.json') -Encoding UTF8
$preSummary | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $artifactDir 'pre-summary.json') -Encoding UTF8

Write-Host '=== Rectangle axis review ===' -ForegroundColor Cyan
$preSummary.GetEnumerator() | ForEach-Object { Write-Host ('  {0}: {1}' -f $_.Key, $_.Value) }

$actionable = @($preReview.Items | Where-Object { $_.NeedsFix })
$reviewOther = @($preReview.Items | Where-Object { $_.EffectiveStatus -eq 'REVIEW_OTHER' })

if (-not $Execute) {
    if (-not $hasRotateTool) {
        Write-Host ''
        Write-Host 'Runtime chua co tool element.rotate_safe. Hien tai source > runtime; can restart Revit de load Agent moi roi chay lai voi -Execute.' -ForegroundColor Yellow
    }

    if (@($actionable).Count -gt 0) {
        Write-Host ''
        Write-Host 'Actionable items:' -ForegroundColor Yellow
        $actionable | Sort-Object EffectiveStatus, RecommendedAngleDegrees, ElementId | Select-Object ElementId, TypeName, EffectiveStatus, RecommendedAngleDegrees | Format-Table -AutoSize
    }

    if (@($reviewOther).Count -gt 0) {
        Write-Host ''
        Write-Host 'Review-other items:' -ForegroundColor Red
        $reviewOther | Select-Object ElementId, TypeName, RawStatus, Reason | Format-Table -AutoSize
    }

    Write-Host ''
    Write-Host ('Artifact dir: {0}' -f $artifactDir) -ForegroundColor Green
    exit 0
}

if (-not $hasRotateTool) {
    throw 'Runtime chua nap tool element.rotate_safe. Build/deploy da xong tren disk, nhung can restart Revit de runtime tu 109 -> 110 tools roi moi execute live.'
}

if (@($reviewOther).Count -gt 0) {
    throw ('Con {0} item REVIEW_OTHER. Chua execute vi con case mo can xem them.' -f @($reviewOther).Count)
}

$executionLog = @()
$groups = @($actionable | Group-Object RecommendedAngleDegrees | Sort-Object Name)
foreach ($group in $groups) {
    $angle = [double]$group.Name
    $ids = @($group.Group | ForEach-Object { [int]$_.ElementId })
    if ($ids.Count -eq 0) {
        continue
    }

    Write-Host ('Executing rotate_safe for angle {0} on {1} ids...' -f $angle, $ids.Count) -ForegroundColor Yellow
    $payload = [ordered]@{
        DocumentKey = [string]$doc.DocumentKey
        ElementIds = [int[]]$ids
        AngleDegrees = $angle
        AxisMode = 'element_basis_z'
    }

    $result = Invoke-GuardedMutation -Tool 'element.rotate_safe' -Payload $payload -TargetDocument $doc.DocumentKey
    $executionLog += [pscustomobject]@{
        AngleDegrees = $angle
        ElementIds = $ids
        StatusCode = [string]$result.StatusCode
        ChangedIds = @($result.ChangedIds)
        Diagnostics = @($result.Diagnostics)
    }
}

$commentResult = $null
if ($WriteComments) {
    $postCommentReview = Get-RectangleReview -DocumentKey $doc.DocumentKey
    $commentResult = Write-RectangleComments -DocumentKey $doc.DocumentKey -Items @($postCommentReview.Items)
}

$saveResult = $null
if ($SaveDocument) {
    $saveResult = Invoke-GuardedMutation -Tool 'file.save_document' -Payload @{ DocumentKey = [string]$doc.DocumentKey } -TargetDocument $doc.DocumentKey
}

$postReview = Get-RectangleReview -DocumentKey $doc.DocumentKey
$postSummary = [ordered]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    FamilyName = $FamilyName
    RuntimeHasRotateTool = $hasRotateTool
    TotalItems = @($postReview.Items).Count
    ProjectOkCount = @($postReview.Items | Where-Object { $_.EffectiveStatus -eq 'PROJECT_OK' }).Count
    VerticalFaceOkCount = @($postReview.Items | Where-Object { $_.EffectiveStatus -eq 'VERTICAL_FACE_OK' }).Count
    FixRotateInVerticalFaceCount = @($postReview.Items | Where-Object { $_.EffectiveStatus -eq 'FIX_ROTATE_IN_VERTICAL_FACE' }).Count
    FixRotateInViewCount = @($postReview.Items | Where-Object { $_.EffectiveStatus -eq 'FIX_ROTATE_IN_VIEW' }).Count
    ReviewOtherCount = @($postReview.Items | Where-Object { $_.EffectiveStatus -eq 'REVIEW_OTHER' }).Count
    ReadyForExportCount = @($postReview.Items | Where-Object { $_.ReadyForExport }).Count
    CommentWriteApplied = ($null -ne $commentResult)
    SaveDocumentApplied = ($null -ne $saveResult)
}

$postReview | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $artifactDir 'post-review.json') -Encoding UTF8
$postSummary | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $artifactDir 'post-summary.json') -Encoding UTF8
$executionLog | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $artifactDir 'execute-log.json') -Encoding UTF8

Write-Host ''
Write-Host '=== Rectangle axis post-review ===' -ForegroundColor Cyan
$postSummary.GetEnumerator() | ForEach-Object { Write-Host ('  {0}: {1}' -f $_.Key, $_.Value) }
Write-Host ('Artifact dir: {0}' -f $artifactDir) -ForegroundColor Green
