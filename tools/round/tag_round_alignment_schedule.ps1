param(
    [string]$BridgeExe = "",
    [string]$FamilyName = "Round",
    [string]$ScheduleName = "BIM765T_Round_Axis_Audit",
    [string]$AlignedComment = "Old+Align",
    [string]$NotAlignedComment = "Old+Not Align",
    [switch]$SkipTagging,
    [switch]$SkipSchedule
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
$artifactDir = Join-Path $projectRoot ("artifacts\round-axis-audit\{0}" -f $runId)
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
    param(
        [Parameter(Mandatory = $true)]
        [object]$Response
    )

    if ([string]::IsNullOrWhiteSpace([string]$Response.PayloadJson)) {
        return $null
    }

    return ($Response.PayloadJson | ConvertFrom-Json)
}

function Invoke-BridgeReadPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [object]$Payload,
        [string]$TargetDocument = ""
    )

    $payloadJson = $Payload | ConvertTo-Json -Depth 100
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-BridgeMutation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [string]$TargetDocument,
        [Parameter(Mandatory = $true)]
        [object]$Payload
    )

    $fingerprint = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool 'document.get_context_fingerprint' -TargetDocument $TargetDocument
    Assert-BridgeSuccess -Response $fingerprint -Tool 'document.get_context_fingerprint'

    $payloadFile = Join-Path $env:TEMP ("round_axis_payload_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
    $contextFile = Join-Path $env:TEMP ("round_axis_context_{0}.json" -f ([Guid]::NewGuid().ToString('N')))

    try {
        $Payload | ConvertTo-Json -Depth 100 | Set-Content -Path $payloadFile -Encoding UTF8
        $fingerprint.PayloadJson | Set-Content -Path $contextFile -Encoding UTF8

        $preview = (& $BridgeExe $Tool '--target-document' $TargetDocument '--dry-run' 'true' '--payload' $payloadFile '--expected-context' $contextFile) | ConvertFrom-Json
        Assert-BridgeSuccess -Response $preview -Tool ($Tool + ' [preview]')

        if (-not $preview.ConfirmationRequired -or [string]::IsNullOrWhiteSpace([string]$preview.ApprovalToken)) {
            throw "Tool $Tool preview khong tra approval token hop le."
        }

        $executeArgs = @(
            $Tool,
            '--target-document', $TargetDocument,
            '--dry-run', 'false',
            '--payload', $payloadFile,
            '--expected-context', $contextFile,
            '--approval-token', $preview.ApprovalToken
        )

        if (-not [string]::IsNullOrWhiteSpace([string]$preview.PreviewRunId)) {
            $executeArgs += @('--preview-run-id', [string]$preview.PreviewRunId)
        }

        $execute = (& $BridgeExe @executeArgs) | ConvertFrom-Json
        Assert-BridgeSuccess -Response $execute -Tool ($Tool + ' [execute]')

        return [pscustomobject]@{
            Preview = $preview
            Execute = $execute
        }
    }
    finally {
        Remove-Item $payloadFile, $contextFile -Force -ErrorAction SilentlyContinue
    }
}

function Get-ElementQuery {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [int[]]$ElementIds
    )

    $payload = @{
        DocumentKey = $DocumentKey
        ViewScopeOnly = $false
        SelectedOnly = $false
        ElementIds = $ElementIds
        IncludeParameters = $true
        MaxResults = [Math]::Max(200, $ElementIds.Count + 20)
    }

    $response = Invoke-BridgeReadPayload -Tool 'element.query' -Payload $payload -TargetDocument $DocumentKey
    return (ConvertFrom-PayloadJson -Response $response)
}

function Get-CommentsMap {
    param(
        [Parameter(Mandatory = $true)]
        [object]$QueryData
    )

    $map = @{}
    foreach ($item in @($QueryData.Items)) {
        $comment = @($item.Parameters | Where-Object { $_.Name -eq 'Comments' } | Select-Object -First 1).Value
        if ($null -eq $comment) { $comment = '' }
        $map[[int]$item.ElementId] = [string]$comment
    }
    return $map
}

function New-StatusCounts {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Items
    )

    return @($Items |
        Group-Object -Property Status |
        Sort-Object -Property Name |
        ForEach-Object {
            [pscustomobject]@{
                Status = $_.Name
                Count = $_.Count
            }
        })
}

function New-CommentCounts {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$CommentMap
    )

    return @($CommentMap.GetEnumerator() |
        Group-Object -Property Value |
        Sort-Object -Property Name |
        ForEach-Object {
            [pscustomobject]@{
                Comment = [string]$_.Name
                Count = $_.Count
            }
        })
}

function Get-View3DItems {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey
    )

    $payload = @{
        DocumentKey = $DocumentKey
        ViewScopeOnly = $false
        SelectedOnly = $false
        ClassName = 'View3D'
        MaxResults = 5000
        IncludeParameters = $false
    }

    $response = Invoke-BridgeReadPayload -Tool 'element.query' -Payload $payload -TargetDocument $DocumentKey
    return @( (ConvertFrom-PayloadJson -Response $response).Items )
}

function New-AuditPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [int]$ViewId = 0,
        [string]$ViewName = ''
    )

    $payload = @{
        DocumentKey = $DocumentKey
        CategoryNames = @('Generic Models')
        AngleToleranceDegrees = 5.0
        TreatMirroredAsMismatch = $true
        TreatAntiParallelAsMismatch = $false
        HighlightInUi = $false
        IncludeAlignedItems = $true
        MaxElements = 2500
        MaxIssues = 2500
        ZoomToHighlighted = $false
        AnalyzeNestedFamilies = $true
        MaxFamilyDefinitionsToInspect = 80
        MaxNestedInstancesPerFamily = 500
        MaxNestedFindingsPerFamily = 100
        TreatNonSharedNestedAsRisk = $true
        TreatNestedMirroredAsRisk = $true
        TreatNestedRotatedAsRisk = $true
        TreatNestedTiltedAsRisk = $true
        IncludeNestedFindings = $true
    }

    if ($ViewId -gt 0) {
        $payload.ViewId = $ViewId
    }
    if (-not [string]::IsNullOrWhiteSpace($ViewName)) {
        $payload.ViewName = $ViewName
    }

    return $payload
}

function Get-RoundAuditForView {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DocumentKey,
        [Parameter(Mandatory = $true)]
        [string]$FamilyName,
        [int]$ViewId = 0,
        [string]$ViewName = ''
    )

    $response = Invoke-BridgeReadPayload -Tool 'review.family_axis_alignment' -Payload (New-AuditPayload -DocumentKey $DocumentKey -ViewId $ViewId -ViewName $ViewName) -TargetDocument $DocumentKey
    $data = ConvertFrom-PayloadJson -Response $response
    $roundItems = @($data.Items | Where-Object { $_.FamilyName -eq $FamilyName })
    return [pscustomobject]@{
        Response = $response
        Data = $data
        RoundItems = $roundItems
        ViewId = $ViewId
        ViewName = if ([string]::IsNullOrWhiteSpace($ViewName)) { $data.ViewName } else { $ViewName }
    }
}

$activeResponse = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool 'document.get_active'
Assert-BridgeSuccess -Response $activeResponse -Tool 'document.get_active'
$activeDoc = ConvertFrom-PayloadJson -Response $activeResponse

$viewResponse = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool 'view.get_active_context'
Assert-BridgeSuccess -Response $viewResponse -Tool 'view.get_active_context'
$activeView = ConvertFrom-PayloadJson -Response $viewResponse

$initialViewName = ''
if ($activeView -and $activeView.PSObject.Properties.Name -contains 'ViewName' -and $activeView.ViewName) {
    $initialViewName = [string]$activeView.ViewName
}

$auditAttempt = Get-RoundAuditForView -DocumentKey $activeDoc.DocumentKey -FamilyName $FamilyName -ViewName $initialViewName
$auditData = $auditAttempt.Data
$roundItems = @($auditAttempt.RoundItems)
$resolvedViewName = $auditAttempt.ViewName
$resolvedViewId = if ($auditData.ViewId) { [int]$auditData.ViewId } else { $null }

if ($roundItems.Count -eq 0) {
    $all3dViews = Get-View3DItems -DocumentKey $activeDoc.DocumentKey
    $userToken = ''
    if ($activeDoc.Title -match '_([^_]+)$') {
        $userToken = $Matches[1]
    }

    $candidateViews = @($all3dViews |
        Sort-Object `
            @{ Expression = { if ($userToken -and $_.Name -like ('*' + $userToken + '*')) { 0 } else { 1 } } }, `
            @{ Expression = { if ($_.Name -match 'Pen') { 0 } else { 1 } } }, `
            @{ Expression = { if ($_.Name -match 'Coordination') { 0 } else { 1 } } }, `
            @{ Expression = { if ($_.Name -match '3D') { 0 } else { 1 } } }, `
            @{ Expression = { $_.Name } })

    $bestAttempt = $null
    $maxChecks = [Math]::Min(15, $candidateViews.Count)
    for ($i = 0; $i -lt $maxChecks; $i++) {
        $candidate = $candidateViews[$i]
        $candidateAttempt = Get-RoundAuditForView -DocumentKey $activeDoc.DocumentKey -FamilyName $FamilyName -ViewId ([int]$candidate.ElementId) -ViewName ([string]$candidate.Name)
        if ($null -eq $bestAttempt -or $candidateAttempt.RoundItems.Count -gt $bestAttempt.RoundItems.Count) {
            $bestAttempt = $candidateAttempt
        }

        if ($candidateAttempt.RoundItems.Count -gt 0 -and $userToken -and $candidate.Name -like ('*' + $userToken + '*')) {
            $bestAttempt = $candidateAttempt
            break
        }
    }

    if ($bestAttempt -and $bestAttempt.RoundItems.Count -gt 0) {
        $auditAttempt = $bestAttempt
        $auditData = $bestAttempt.Data
        $roundItems = @($bestAttempt.RoundItems)
        $resolvedViewName = $bestAttempt.ViewName
        $resolvedViewId = if ($bestAttempt.ViewId -gt 0) { [int]$bestAttempt.ViewId } else { $null }
    }
}

if ($roundItems.Count -eq 0) {
    throw "Khong tim thay instance nao cua family '$FamilyName' trong pham vi audit hien tai."
}

$alignedItems = @($roundItems | Where-Object { $_.Status -eq 'ALIGNED' })
$notAlignedItems = @($roundItems | Where-Object { $_.Status -ne 'ALIGNED' })
$allIds = @($roundItems | ForEach-Object { [int]$_.ElementId })

$beforeQuery = Get-ElementQuery -DocumentKey $activeDoc.DocumentKey -ElementIds $allIds
$beforeComments = Get-CommentsMap -QueryData $beforeQuery
$firstRound = @($beforeQuery.Items | Select-Object -First 1)[0]
$noteLikeParams = @($firstRound.Parameters | Where-Object { $_.Name -match 'Note' })

$tagResults = @()
if (-not $SkipTagging) {
    if ($alignedItems.Count -gt 0) {
        $tagResults += Invoke-BridgeMutation -Tool 'parameter.batch_fill_safe' -TargetDocument $activeDoc.DocumentKey -Payload @{
            DocumentKey = $activeDoc.DocumentKey
            ParameterName = 'Comments'
            FillValue = $AlignedComment
            FillMode = 'OverwriteAll'
            ElementIds = @($alignedItems | ForEach-Object { [int]$_.ElementId })
            CategoryNames = @()
        }
    }

    if ($notAlignedItems.Count -gt 0) {
        $tagResults += Invoke-BridgeMutation -Tool 'parameter.batch_fill_safe' -TargetDocument $activeDoc.DocumentKey -Payload @{
            DocumentKey = $activeDoc.DocumentKey
            ParameterName = 'Comments'
            FillValue = $NotAlignedComment
            FillMode = 'OverwriteAll'
            ElementIds = @($notAlignedItems | ForEach-Object { [int]$_.ElementId })
            CategoryNames = @()
        }
    }
}

$scheduleMutation = $null
if (-not $SkipSchedule) {
    $scheduleMutation = Invoke-BridgeMutation -Tool 'schedule.create_penetration_alpha_inventory_safe' -TargetDocument $activeDoc.DocumentKey -Payload @{
        DocumentKey = $activeDoc.DocumentKey
        FamilyName = $FamilyName
        ScheduleName = $ScheduleName
        OverwriteIfExists = $true
        Itemized = $true
    }
}

$afterQuery = Get-ElementQuery -DocumentKey $activeDoc.DocumentKey -ElementIds $allIds
$afterComments = Get-CommentsMap -QueryData $afterQuery

$scheduleExport = $null
if (-not $SkipSchedule) {
    $scheduleExportResponse = Invoke-BridgeReadPayload -Tool 'data.export_schedule' -TargetDocument $activeDoc.DocumentKey -Payload @{
        DocumentKey = $activeDoc.DocumentKey
        ScheduleName = $ScheduleName
        Format = 'json'
    }
    $scheduleExport = ConvertFrom-PayloadJson -Response $scheduleExportResponse
}

$summary = [pscustomobject]@{
    RunId = $runId
    DocumentTitle = $activeDoc.Title
    DocumentKey = $activeDoc.DocumentKey
    ActiveViewName = $activeView.ViewName
    AuditViewName = $resolvedViewName
    AuditViewId = $resolvedViewId
    FamilyName = $FamilyName
    TotalRoundCount = $roundItems.Count
    AlignedCount = $alignedItems.Count
    NotAlignedCount = $notAlignedItems.Count
    StatusCounts = New-StatusCounts -Items $roundItems
    CommentsParameterAvailable = ($firstRound.Parameters | Where-Object { $_.Name -eq 'Comments' } | Measure-Object).Count -gt 0
    NotesParameterAvailable = $noteLikeParams.Count -gt 0
    TaggingApplied = (-not $SkipTagging)
    ScheduleCreatedOrUpdated = (-not $SkipSchedule)
    ScheduleName = if ($SkipSchedule) { '' } else { $ScheduleName }
    ScheduleId = if ($scheduleMutation) { @($scheduleMutation.Execute.ChangedIds | Select-Object -First 1)[0] } else { $null }
    ScheduleRowCount = if ($scheduleExport) { $scheduleExport.RowCount } else { $null }
    ScheduleNonEmptyRowCount = if ($scheduleExport) { @($scheduleExport.Rows | Where-Object { (@($_ | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) }).Count } else { $null }
    BeforeCommentCounts = @(New-CommentCounts -CommentMap $beforeComments)
    AfterCommentCounts = @(New-CommentCounts -CommentMap $afterComments)
    AlignedElementIds = @($alignedItems | ForEach-Object { [int]$_.ElementId })
    NotAlignedElementIds = @($notAlignedItems | ForEach-Object { [int]$_.ElementId })
    ArtifactDirectory = $artifactDir
}

Write-JsonFile -Path (Join-Path $artifactDir 'axis-audit.raw.json') -Data $auditData
Write-JsonFile -Path (Join-Path $artifactDir 'round-items.json') -Data $roundItems
Write-JsonFile -Path (Join-Path $artifactDir 'before-query.json') -Data $beforeQuery
Write-JsonFile -Path (Join-Path $artifactDir 'after-query.json') -Data $afterQuery
if ($scheduleExport) {
    Write-JsonFile -Path (Join-Path $artifactDir 'schedule-export.json') -Data $scheduleExport
}
Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary

$summary | ConvertTo-Json -Depth 20
