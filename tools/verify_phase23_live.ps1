param(
    [string]$BridgeExe = "",
    [string]$TargetDocumentKey = "",
    [string]$PreferDocumentTitleContains = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Join-Path $PSScriptRoot '..')
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\phase23-live-verify\{0}" -f $runId)
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

function Save-ToolResponse {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [object]$Response
    )

    Write-JsonFile -Path (Join-Path $artifactDir ("{0}.response.json" -f $Name)) -Data $Response
    $payload = ConvertFrom-PayloadJson -Response $Response
    if ($null -ne $payload) {
        Write-JsonFile -Path (Join-Path $artifactDir ("{0}.payload.json" -f $Name)) -Data $payload
    }

    return $payload
}

function New-StepSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [object]$Response = $null,
        [object]$Payload = $null,
        [string]$ErrorMessage = ""
    )

    if ($null -eq $Response) {
        return [ordered]@{
            Tool = $Tool
            StatusCode = 'FAILED'
            ConfirmationRequired = $false
            PreviewRunId = ''
            ApprovalTokenIssued = $false
            DiagnosticCodes = @()
            ChangedIdCount = 0
            ArtifactCount = 0
            Error = $ErrorMessage
            PayloadSummary = $Payload
        }
    }

    return [ordered]@{
        Tool = $Tool
        StatusCode = [string]$Response.StatusCode
        ConfirmationRequired = [bool]$Response.ConfirmationRequired
        PreviewRunId = [string]$Response.PreviewRunId
        ApprovalTokenIssued = (-not [string]::IsNullOrWhiteSpace([string]$Response.ApprovalToken))
        DiagnosticCodes = @($Response.Diagnostics | ForEach-Object { [string]$_.Code })
        ChangedIdCount = @($Response.ChangedIds).Count
        ArtifactCount = @($Response.Artifacts).Count
        Error = $ErrorMessage
        PayloadSummary = $Payload
    }
}

function Resolve-TargetDocumentInfo {
    param(
        [string]$RequestedDocumentKey,
        [string]$PreferredTitleContains
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedDocumentKey)) {
        $requested = Invoke-ReadTool -Tool 'document.get_metadata' -Payload @{
            DocumentKey = $RequestedDocumentKey
        } -TargetDocument $RequestedDocumentKey
        return (ConvertFrom-PayloadJson -Response $requested)
    }

    $openDocsResponse = Invoke-ReadTool -Tool 'session.list_open_documents'
    $openDocs = ConvertFrom-PayloadJson -Response $openDocsResponse
    Write-JsonFile -Path (Join-Path $artifactDir 'open-documents.json') -Data $openDocs

    if (-not [string]::IsNullOrWhiteSpace($PreferredTitleContains)) {
        $match = @($openDocs.Documents | Where-Object { $_.Title -like ("*" + $PreferredTitleContains + "*") }) | Select-Object -First 1
        if ($match) {
            return $match
        }
    }

    $activeResponse = Invoke-ReadTool -Tool 'document.get_active'
    return (ConvertFrom-PayloadJson -Response $activeResponse)
}

$healthScript = Join-Path $PSScriptRoot 'check_bridge_health.ps1'
$healthJson = & powershell -NoProfile -ExecutionPolicy Bypass -File $healthScript -BridgeExe $BridgeExe -Profile phase23 -AsJson
$health = $healthJson | ConvertFrom-Json
Write-JsonFile -Path (Join-Path $artifactDir 'bridge-health.phase23.json') -Data $health

$targetDoc = Resolve-TargetDocumentInfo -RequestedDocumentKey $TargetDocumentKey -PreferredTitleContains $PreferDocumentTitleContains
if ($null -eq $targetDoc -or [string]::IsNullOrWhiteSpace([string]$targetDoc.DocumentKey)) {
    throw "Khong resolve duoc target document cho phase23 live verification."
}

$targetDocKey = [string]$targetDoc.DocumentKey
Write-JsonFile -Path (Join-Path $artifactDir 'target-document.json') -Data $targetDoc

$sheetListResponse = Invoke-ReadTool -Tool 'sheet.list_all' -Payload @{
    DocumentKey = $targetDocKey
    SheetNumberContains = ""
    SheetNameContains = ""
    IncludeViewports = $false
    MaxResults = 20
} -TargetDocument $targetDocKey
$sheetList = Save-ToolResponse -Name 'sheet-list-all' -Response $sheetListResponse
$firstSheet = @($sheetList.Sheets) | Select-Object -First 1

$familyRootsResponse = Invoke-ReadTool -Tool 'family.list_library_roots' -TargetDocument $targetDocKey
$familyRoots = Save-ToolResponse -Name 'family-list-library-roots' -Response $familyRootsResponse
$firstFamilyRoot = @($familyRoots.Roots) | Select-Object -First 1

$presetCatalogResponse = Invoke-ReadTool -Tool 'export.list_presets' -TargetDocument $targetDocKey
$presetCatalog = Save-ToolResponse -Name 'export-list-presets' -Response $presetCatalogResponse
$firstOutputRoot = @($presetCatalog.OutputRoots) | Select-Object -First 1
$ifcPreset = @($presetCatalog.Presets | Where-Object { $_.Kind -eq 'ifc' }) | Select-Object -First 1
$dwgPreset = @($presetCatalog.Presets | Where-Object { $_.Kind -eq 'dwg' }) | Select-Object -First 1
$pdfPreset = @($presetCatalog.Presets | Where-Object { $_.Kind -eq 'pdf' }) | Select-Object -First 1

$outputValidationResponse = Invoke-ReadTool -Tool 'storage.validate_output_target' -Payload @{
    DocumentKey = $targetDocKey
    OperationKind = 'export'
    OutputRootName = if ($firstOutputRoot) { [string]$firstOutputRoot.Name } else { 'documents_exports' }
    RelativePath = ("phase23-smoke\{0}" -f $runId)
} -TargetDocument $targetDocKey
$outputValidation = Save-ToolResponse -Name 'storage-validate-output-target' -Response $outputValidationResponse

$fixPayload = @{
    DocumentKey = $targetDocKey
    ScenarioName = 'safe_cleanup'
    PlaybookName = 'default.fix_loop_v1'
    ElementIds = @()
    CategoryNames = @()
    RequiredParameterNames = @()
    UseCurrentSelectionWhenEmpty = $false
    ViewId = $null
    SheetId = $null
    MaxIssues = 10
    MaxActions = 5
    ImportFilePath = ''
    MatchParameterName = ''
}

$fixReviewResponse = Invoke-ReadTool -Tool 'review.fix_candidates' -Payload $fixPayload -TargetDocument $targetDocKey
$fixReview = Save-ToolResponse -Name 'review-fix-candidates' -Response $fixReviewResponse

$fixPlanResponse = Invoke-ReadTool -Tool 'workflow.fix_loop_plan' -Payload $fixPayload -TargetDocument $targetDocKey
$fixPlan = Save-ToolResponse -Name 'workflow-fix-loop-plan' -Response $fixPlanResponse

$fixApplyPreviewResponse = Invoke-MutationPreview -Tool 'workflow.fix_loop_apply' -Payload @{
    RunId = [string]$fixPlan.RunId
    ActionIds = @()
    AllowMutations = $true
} -TargetDocument $targetDocKey
$fixApplyPreview = Save-ToolResponse -Name 'workflow-fix-loop-apply-preview' -Response $fixApplyPreviewResponse

$fixVerifyResponse = Invoke-ReadTool -Tool 'workflow.fix_loop_verify' -Payload @{
    RunId = [string]$fixPlan.RunId
    MaxResidualIssues = 20
} -TargetDocument $targetDocKey
$fixVerify = Save-ToolResponse -Name 'workflow-fix-loop-verify' -Response $fixVerifyResponse

$schedulePayload = @{
    DocumentKey = $targetDocKey
    ScheduleName = ("ZZ_PHASE23_SMOKE_{0}" -f $runId)
    CategoryName = 'Generic Models'
    Fields = @(
        @{
            ParameterName = 'Family and Type'
            ColumnHeading = 'Family and Type'
            Hidden = $false
        }
    )
    Filters = @()
    Sorts = @()
    IsItemized = $true
    IncludeLinkedFiles = $false
    ShowGrandTotal = $false
    MaxFieldCount = 10
}

$schedulePreviewResponse = Invoke-ReadTool -Tool 'schedule.preview_create' -Payload $schedulePayload -TargetDocument $targetDocKey
$schedulePreview = Save-ToolResponse -Name 'schedule-preview-create' -Response $schedulePreviewResponse

$scheduleCreatePreviewResponse = Invoke-MutationPreview -Tool 'schedule.create_safe' -Payload $schedulePayload -TargetDocument $targetDocKey
$scheduleCreatePreview = Save-ToolResponse -Name 'schedule-create-preview' -Response $scheduleCreatePreviewResponse

$familyLoadPreviewResponse = Invoke-MutationPreview -Tool 'family.load_safe' -Payload @{
    DocumentKey = $targetDocKey
    LibraryRootName = if ($firstFamilyRoot) { [string]$firstFamilyRoot.Name } else { 'documents_library' }
    RelativeFamilyPath = 'smoke\missing_family_for_preview.rfa'
    TypeNames = @()
    LoadAllSymbols = $true
    OverwriteExisting = $false
} -TargetDocument $targetDocKey
$familyLoadPreview = Save-ToolResponse -Name 'family-load-preview' -Response $familyLoadPreviewResponse

$relativeOutputPath = ("phase23-smoke\{0}" -f $runId)
$outputRootName = if ($firstOutputRoot) { [string]$firstOutputRoot.Name } else { 'documents_exports' }

$ifcPreviewResponse = Invoke-MutationPreview -Tool 'export.ifc_safe' -Payload @{
    DocumentKey = $targetDocKey
    PresetName = if ($ifcPreset) { [string]$ifcPreset.PresetName } else { 'coordination_ifc' }
    OutputRootName = $outputRootName
    RelativeOutputPath = $relativeOutputPath
    FileName = ("phase23-smoke-{0}.ifc" -f $runId)
    ViewId = $null
    ViewName = ''
    OverwriteExisting = $false
} -TargetDocument $targetDocKey
$ifcPreview = Save-ToolResponse -Name 'export-ifc-preview' -Response $ifcPreviewResponse

$dwgSheetIds = [int[]]$(if ($firstSheet) { @([int]$firstSheet.Id) } else { @() })
$pdfSheetIds = [int[]]$(if ($firstSheet) { @([int]$firstSheet.Id) } else { @() })
$pdfSheetNumbers = [string[]]$(if ($firstSheet -and -not [string]::IsNullOrWhiteSpace([string]$firstSheet.SheetNumber)) { @([string]$firstSheet.SheetNumber) } else { @() })

$dwgPreviewResponse = $null
$dwgPreview = $null
$dwgPreviewError = ''
try {
    $dwgPreviewResponse = Invoke-MutationPreview -Tool 'export.dwg_safe' -Payload @{
        DocumentKey = $targetDocKey
        PresetName = if ($dwgPreset) { [string]$dwgPreset.PresetName } else { 'default_dwg' }
        OutputRootName = $outputRootName
        RelativeOutputPath = $relativeOutputPath
        FileName = ("phase23-smoke-{0}.dwg" -f $runId)
        ViewIds = @()
        SheetIds = $dwgSheetIds
        UseActiveViewWhenEmpty = $false
        OverwriteExisting = $false
    } -TargetDocument $targetDocKey
    $dwgPreview = Save-ToolResponse -Name 'export-dwg-preview' -Response $dwgPreviewResponse
}
catch {
    $dwgPreviewError = $_.Exception.Message
    Set-Content -Path (Join-Path $artifactDir 'export-dwg-preview.error.txt') -Value $dwgPreviewError -Encoding UTF8
}

$pdfPreviewResponse = $null
$pdfPreview = $null
$pdfPreviewError = ''
try {
    $pdfPreviewResponse = Invoke-MutationPreview -Tool 'sheet.print_pdf_safe' -Payload @{
        DocumentKey = $targetDocKey
        PresetName = if ($pdfPreset) { [string]$pdfPreset.PresetName } else { 'sheet_issue_pdf' }
        OutputRootName = $outputRootName
        RelativeOutputPath = $relativeOutputPath
        FileName = ("phase23-smoke-{0}.pdf" -f $runId)
        SheetIds = $pdfSheetIds
        SheetNumbers = $pdfSheetNumbers
        Combine = $true
        OverwriteExisting = $false
    } -TargetDocument $targetDocKey
    $pdfPreview = Save-ToolResponse -Name 'sheet-print-pdf-preview' -Response $pdfPreviewResponse
}
catch {
    $pdfPreviewError = $_.Exception.Message
    Set-Content -Path (Join-Path $artifactDir 'sheet-print-pdf-preview.error.txt') -Value $pdfPreviewError -Encoding UTF8
}

$summary = [ordered]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    TargetDocument = [ordered]@{
        DocumentKey = $targetDocKey
        Title = [string]$targetDoc.Title
        PathName = [string]$targetDoc.PathName
        WasActiveAtSelectionTime = [bool]$targetDoc.IsActive
    }
    BridgeHealth = $health
    FixLoop = [ordered]@{
        Review = New-StepSummary -Tool 'review.fix_candidates' -Response $fixReviewResponse -Payload ([ordered]@{
            IssueCount = @($fixReview.Issues).Count
            CandidateActionCount = @($fixReview.CandidateActions).Count
            DiagnosticCount = @($fixReview.Diagnostics).Count
        })
        Plan = New-StepSummary -Tool 'workflow.fix_loop_plan' -Response $fixPlanResponse -Payload ([ordered]@{
            RunId = [string]$fixPlan.RunId
            Status = [string]$fixPlan.Status
            IssueCount = @($fixPlan.Issues).Count
            CandidateActionCount = @($fixPlan.CandidateActions).Count
            ExecutableActionCount = @($fixPlan.CandidateActions | Where-Object { $_.IsExecutable }).Count
        })
        ApplyPreview = New-StepSummary -Tool 'workflow.fix_loop_apply' -Response $fixApplyPreviewResponse -Payload ([ordered]@{
            RunId = [string]$fixPlan.RunId
            ChangedIdCount = @($fixApplyPreviewResponse.ChangedIds).Count
            DiagnosticCount = @($fixApplyPreviewResponse.Diagnostics).Count
        })
        Verify = New-StepSummary -Tool 'workflow.fix_loop_verify' -Response $fixVerifyResponse -Payload ([ordered]@{
            Status = if ($fixVerify.Verification) { [string]$fixVerify.Verification.Status } else { '' }
            ExpectedIssueCount = if ($fixVerify.Verification) { $fixVerify.Verification.ExpectedIssueCount } else { 0 }
            ActualIssueCount = if ($fixVerify.Verification) { $fixVerify.Verification.ActualIssueCount } else { 0 }
            ActualIssueDelta = if ($fixVerify.Verification) { $fixVerify.Verification.ActualIssueDelta } else { 0 }
            ResidualIssueCount = if ($fixVerify.Verification) { @($fixVerify.Verification.ResidualIssues).Count } else { 0 }
        })
    }
    DeliveryOps = [ordered]@{
        FamilyLibraryRootCount = @($familyRoots.Roots).Count
        OutputRootCount = @($presetCatalog.OutputRoots).Count
        PresetCount = @($presetCatalog.Presets).Count
        FirstSheet = if ($firstSheet) {
            [ordered]@{
                SheetId = [int]$firstSheet.Id
                SheetNumber = [string]$firstSheet.SheetNumber
                SheetName = [string]$firstSheet.SheetName
            }
        } else { $null }
        OutputValidation = [ordered]@{
            Allowed = [bool]$outputValidation.Allowed
            OutputRootName = [string]$outputValidation.OutputRootName
            ResolvedPath = [string]$outputValidation.ResolvedPath
            Reason = [string]$outputValidation.Reason
        }
        SchedulePreview = New-StepSummary -Tool 'schedule.preview_create' -Response $schedulePreviewResponse -Payload ([ordered]@{
            ResolvedCategoryId = $schedulePreview.ResolvedCategoryId
            ExistingScheduleId = $schedulePreview.ExistingScheduleId
            FieldNames = @($schedulePreview.FieldNames)
            WarningCount = @($schedulePreview.Warnings).Count
        })
        ScheduleCreatePreview = New-StepSummary -Tool 'schedule.create_safe' -Response $scheduleCreatePreviewResponse -Payload ([ordered]@{
            DiagnosticCount = @($scheduleCreatePreviewResponse.Diagnostics).Count
            PreviewRunId = [string]$scheduleCreatePreviewResponse.PreviewRunId
        })
        FamilyLoadPreview = New-StepSummary -Tool 'family.load_safe' -Response $familyLoadPreviewResponse -Payload ([ordered]@{
            DiagnosticCount = @($familyLoadPreviewResponse.Diagnostics).Count
            RequestedRoot = if ($firstFamilyRoot) { [string]$firstFamilyRoot.Name } else { 'documents_library' }
        })
        IfcPreview = New-StepSummary -Tool 'export.ifc_safe' -Response $ifcPreviewResponse -Payload ([ordered]@{
            DiagnosticCount = @($ifcPreviewResponse.Diagnostics).Count
            PreviewRunId = [string]$ifcPreviewResponse.PreviewRunId
        })
        DwgPreview = New-StepSummary -Tool 'export.dwg_safe' -Response $dwgPreviewResponse -ErrorMessage $dwgPreviewError -Payload ([ordered]@{
            DiagnosticCount = if ($dwgPreviewResponse) { @($dwgPreviewResponse.Diagnostics).Count } else { 0 }
            PreviewRunId = if ($dwgPreviewResponse) { [string]$dwgPreviewResponse.PreviewRunId } else { '' }
        })
        PdfPreview = New-StepSummary -Tool 'sheet.print_pdf_safe' -Response $pdfPreviewResponse -ErrorMessage $pdfPreviewError -Payload ([ordered]@{
            DiagnosticCount = if ($pdfPreviewResponse) { @($pdfPreviewResponse.Diagnostics).Count } else { 0 }
            PreviewRunId = if ($pdfPreviewResponse) { [string]$pdfPreviewResponse.PreviewRunId } else { '' }
        })
    }
}

Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
$summary | ConvertTo-Json -Depth 50
