param(
    [string]$BridgeExe = "",
    [string]$DocumentSelector = "",
    [string]$ContractArtifactDir = "",
    [string]$ViewName = "3D IFC Penetration Export - Round MiTek Canonical",
    [string]$HideReviewedFilterName = "BIM765T_Round_MiTek_HideReviewedRound",
    [string]$ViewTemplateName = "",
    [bool]$UseActive3DOrientationWhenPossible = $true,
    [bool]$CopySectionBoxFromActive3D = $true,
    [string]$OutputRootName = "documents_exports",
    [string]$RelativeOutputPathBase = "round-ifc-mitek-canonical-full",
    [string]$FileName = "round-mitek-canonical-localx-full.ifc",
    [switch]$ExecuteExport,
    [switch]$CleanupAfter = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Join-Path $PSScriptRoot '..')
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-ifc-mitek-canonical-full\{0}" -f $runId)
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

function Invoke-GuardedMutation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [Parameter(Mandatory = $true)]
        [object]$Payload,
        [string]$TargetDocument = ""
    )

    $preview = Invoke-MutationPreview -Tool $Tool -Payload $Payload -TargetDocument $TargetDocument
    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $execute = Invoke-MutationExecute `
        -Tool $Tool `
        -Payload $Payload `
        -ApprovalToken $preview.ApprovalToken `
        -PreviewRunId $preview.PreviewRunId `
        -ExpectedContext $(if ($previewPayload) { $previewPayload.ResolvedContext } else { $null }) `
        -TargetDocument $TargetDocument

    return [pscustomobject]@{
        Preview = $preview
        PreviewPayload = $previewPayload
        Execute = $execute
        ExecutePayload = ConvertFrom-PayloadJson -Response $execute
    }
}

function Find-LatestContractArtifactDir {
    param([string]$ProjectRoot)

    $root = Join-Path $ProjectRoot 'artifacts\round-mitek-export-contract'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    $dir = Get-ChildItem -Path $root -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'export-contract-rows.json') } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $dir) {
        throw "Khong tim thay artifact round-mitek-export-contract co export-contract-rows.json"
    }

    return $dir.FullName
}

function Import-JsonArrayFile {
    param([string]$Path)

    $rows = Get-Content -Raw -Path $Path | ConvertFrom-Json
    if ($rows.Count -eq 1 -and $rows[0] -is [System.Array]) {
        return @($rows[0])
    }

    return @($rows)
}

function Split-IntoChunks {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Items,
        [int]$ChunkSize = 20
    )

    if ($ChunkSize -le 0) {
        $ChunkSize = 20
    }

    $chunks = New-Object System.Collections.Generic.List[object[]]
    for ($index = 0; $index -lt $Items.Count; $index += $ChunkSize) {
        $last = [Math]::Min($index + $ChunkSize - 1, $Items.Count - 1)
        $chunks.Add(@($Items[$index..$last])) | Out-Null
    }

    return @($chunks)
}

function Get-ElementLocationPoint {
    param([Parameter(Mandatory = $true)][object]$ElementSummary)

    if ($null -ne $ElementSummary.LocationCurveStart -and $null -ne $ElementSummary.LocationCurveEnd) {
        return [pscustomobject]@{
            X = ([double]$ElementSummary.LocationCurveStart.X + [double]$ElementSummary.LocationCurveEnd.X) / 2.0
            Y = ([double]$ElementSummary.LocationCurveStart.Y + [double]$ElementSummary.LocationCurveEnd.Y) / 2.0
            Z = ([double]$ElementSummary.LocationCurveStart.Z + [double]$ElementSummary.LocationCurveEnd.Z) / 2.0
        }
    }

    if ($null -ne $ElementSummary.LocationPoint) {
        return [pscustomobject]@{
            X = [double]$ElementSummary.LocationPoint.X
            Y = [double]$ElementSummary.LocationPoint.Y
            Z = [double]$ElementSummary.LocationPoint.Z
        }
    }

    if ($null -ne $ElementSummary.BoundingBox) {
        return [pscustomobject]@{
            X = ([double]$ElementSummary.BoundingBox.MinX + [double]$ElementSummary.BoundingBox.MaxX) / 2.0
            Y = ([double]$ElementSummary.BoundingBox.MinY + [double]$ElementSummary.BoundingBox.MaxY) / 2.0
            Z = ([double]$ElementSummary.BoundingBox.MinZ + [double]$ElementSummary.BoundingBox.MaxZ) / 2.0
        }
    }

    return $null
}

function Get-ParameterValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$ElementSummary,
        [Parameter(Mandatory = $true)]
        [string]$ParameterName
    )

    foreach ($parameter in @($ElementSummary.Parameters)) {
        if ([string]$parameter.Name -eq $ParameterName) {
            return [string]$parameter.Value
        }
    }

    return ''
}

if ([string]::IsNullOrWhiteSpace($ContractArtifactDir)) {
    $ContractArtifactDir = Find-LatestContractArtifactDir -ProjectRoot $projectRoot
}

$contractSummaryPath = Join-Path $ContractArtifactDir 'summary.json'
$contractRowsPath = Join-Path $ContractArtifactDir 'export-contract-rows.json'
if (-not (Test-Path $contractRowsPath)) {
    throw "Khong tim thay export-contract-rows.json tai: $contractRowsPath"
}

$contractSummary = if (Test-Path $contractSummaryPath) { Get-Content -Raw -Path $contractSummaryPath | ConvertFrom-Json } else { $null }
$contractRows = @(
    Import-JsonArrayFile -Path $contractRowsPath |
        Where-Object { [string]$_.CanonicalExportTypeName -ne '' }
)

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
if ($null -eq $doc) {
    throw 'Khong doc duoc active document.'
}
$targetDocument = if (-not [string]::IsNullOrWhiteSpace($DocumentSelector)) { $DocumentSelector } elseif (-not [string]::IsNullOrWhiteSpace([string]$doc.Title)) { [string]$doc.Title } else { '' }
$payloadDocumentKey = if (-not [string]::IsNullOrWhiteSpace([string]$doc.Title)) { [string]$doc.Title } else { [string]$doc.DocumentKey }

Write-JsonFile -Path (Join-Path $artifactDir 'active-document.json') -Data $doc

$wrapperIds = [int[]]@($contractRows | ForEach-Object { [int]$_.NewWrapperElementId })
$wrapperQueryPayload = @{
    DocumentKey = $payloadDocumentKey
    ElementIds = $wrapperIds
    ViewScopeOnly = $false
    SelectedOnly = $false
    CategoryNames = @('Generic Models')
    ClassName = 'FamilyInstance'
    MaxResults = 500
    IncludeParameters = $true
}
$wrapperQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload $wrapperQueryPayload -TargetDocument $targetDocument)
Write-JsonFile -Path (Join-Path $artifactDir 'wrapper-live-query.json') -Data $wrapperQuery

$wrapperMap = @{}
foreach ($item in @($wrapperQuery.Items)) {
    $wrapperMap[[int]$item.ElementId] = $item
}

$missingWrapperIds = @(
    $wrapperIds |
        Where-Object { -not $wrapperMap.ContainsKey([int]$_) } |
        Select-Object -Unique
)
if ($missingWrapperIds.Count -gt 0) {
    throw "Khong query du live wrapper cho ids: $($missingWrapperIds -join ', ')"
}

$reviewCommentCount = @(
    $wrapperMap.Values |
        Where-Object { (Get-ParameterValue -ElementSummary $_ -ParameterName 'Comments') -like 'ROUND_EXPORT_REVIEW|PAIR#*' }
).Count

$typeCatalogPayload = @{
    DocumentKey = $payloadDocumentKey
    CategoryNames = @('Generic Models')
    ClassName = ''
    NameContains = 'AXIS_'
    IncludeParameters = $false
    OnlyInUse = $false
    MaxResults = 500
}
$typeCatalog = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'type.list_element_types' -Payload $typeCatalogPayload -TargetDocument $targetDocument)
Write-JsonFile -Path (Join-Path $artifactDir 'type-catalog.json') -Data $typeCatalog

$canonicalTypeMap = @{}
foreach ($item in @($typeCatalog.Items)) {
    if ([string]$item.FamilyName -ne 'Round_Project') {
        continue
    }

    $typeName = [string]$item.TypeName
    if (-not $canonicalTypeMap.ContainsKey($typeName)) {
        $canonicalTypeMap[$typeName] = [int]$item.TypeId
    }
}

$missingTypeNames = @(
    $contractRows |
        ForEach-Object { [string]$_.CanonicalExportTypeName } |
        Where-Object { -not $canonicalTypeMap.ContainsKey($_) } |
        Select-Object -Unique
)
if ($missingTypeNames.Count -gt 0) {
    throw "Khong tim thay canonical AXIS_X types trong project: $($missingTypeNames -join ', ')"
}

$viewMutation = Invoke-GuardedMutation -Tool 'view.create_3d_safe' -Payload @{
    DocumentKey = $payloadDocumentKey
    ViewName = $ViewName
    UseActive3DOrientationWhenPossible = $UseActive3DOrientationWhenPossible
    CopySectionBoxFromActive3D = $CopySectionBoxFromActive3D
    ViewTemplateName = $ViewTemplateName
    FailIfExists = $false
    DuplicateIfExists = $false
    ActivateViewAfterCreate = $false
} -TargetDocument $targetDocument

$filterMutation = Invoke-GuardedMutation -Tool 'view.create_or_update_filter_safe' -Payload @{
    DocumentKey = $payloadDocumentKey
    FilterName = $HideReviewedFilterName
    CategoryNames = @('Generic Models')
    Rules = @(
        @{
            ParameterName = 'Comments'
            Operator = 'contains'
            Value = 'ROUND_EXPORT_REVIEW|PAIR#'
            CaseSensitive = $false
        }
    )
    OverwriteIfExists = $true
    InferCategoriesFromSelectionWhenEmpty = $false
} -TargetDocument $targetDocument

$applyFilterMutation = Invoke-GuardedMutation -Tool 'view.apply_filter_safe' -Payload @{
    DocumentKey = $payloadDocumentKey
    ViewName = $ViewName
    FilterName = $HideReviewedFilterName
    Visible = $false
} -TargetDocument $targetDocument

Write-JsonFile -Path (Join-Path $artifactDir 'view-create.result.json') -Data $viewMutation
Write-JsonFile -Path (Join-Path $artifactDir 'filter-create.result.json') -Data $filterMutation
Write-JsonFile -Path (Join-Path $artifactDir 'filter-apply.result.json') -Data $applyFilterMutation

$createdRows = New-Object System.Collections.Generic.List[object]
$createdIds = New-Object System.Collections.Generic.List[int]
$cleanupLog = New-Object System.Collections.Generic.List[object]
$executionError = $null
$exportMutation = $null

try {
    $total = $contractRows.Count
    $index = 0
    foreach ($row in $contractRows) {
        $index++
        $sourceId = [int]$row.NewWrapperElementId
        $source = $wrapperMap[$sourceId]
        $point = Get-ElementLocationPoint -ElementSummary $source
        if ($null -eq $point) {
            throw "Khong resolve duoc location cho wrapper $sourceId"
        }

        $canonicalTypeName = [string]$row.CanonicalExportTypeName
        $symbolId = [int]$canonicalTypeMap[$canonicalTypeName]
        $pairTag = [string]$row.PairTag
        $levelIdValue = if ($null -ne $source.LevelId) { [int]$source.LevelId } else { $null }

        Write-Host ("[{0}/{1}] Place surrogate {2} -> {3}" -f $index, $total, $pairTag, $canonicalTypeName) -ForegroundColor Cyan

        $placePayload = [ordered]@{
            DocumentKey = $payloadDocumentKey
            FamilySymbolId = $symbolId
            LevelId = $levelIdValue
            ViewId = $null
            HostElementId = $null
            PlacementMode = 'auto'
            StructuralTypeName = 'NonStructural'
            X = [double]$point.X
            Y = [double]$point.Y
            Z = [double]$point.Z
            RotateRadians = 0.0
            Notes = "ROUND_MITEK_CANONICAL|$pairTag|SRC=$sourceId"
        }

        $placeMutation = Invoke-GuardedMutation -Tool 'element.place_family_instance_safe' -Payload $placePayload -TargetDocument $targetDocument
        $placePayloadResponse = $placeMutation.ExecutePayload
        $createdId = [int]@($placePayloadResponse.ChangedIds)[0]
        $createdIds.Add($createdId) | Out-Null

        $rotationApplied = $false
        $rotationAngleDegrees = 0.0
        $rotationAxisDirection = @()

        switch ([string]$row.ExportStrategy) {
            'CANONICALIZE_TO_AXIS_X_IN_PLANE' {
                $rotatePayload = [ordered]@{
                    DocumentKey = $payloadDocumentKey
                    ElementIds = [int[]]@($createdId)
                    AngleDegrees = 90.0
                    AxisMode = 'explicit'
                    AxisOriginX = [double]$point.X
                    AxisOriginY = [double]$point.Y
                    AxisOriginZ = [double]$point.Z
                    AxisDirectionX = 0.0
                    AxisDirectionY = 0.0
                    AxisDirectionZ = 1.0
                }

                Invoke-GuardedMutation -Tool 'element.rotate_safe' -Payload $rotatePayload -TargetDocument $targetDocument | Out-Null
                $rotationApplied = $true
                $rotationAngleDegrees = 90.0
                $rotationAxisDirection = @(0.0, 0.0, 1.0)
            }
            'CANONICALIZE_TO_AXIS_X_3D' {
                $rotatePayload = [ordered]@{
                    DocumentKey = $payloadDocumentKey
                    ElementIds = [int[]]@($createdId)
                    AngleDegrees = -90.0
                    AxisMode = 'explicit'
                    AxisOriginX = [double]$point.X
                    AxisOriginY = [double]$point.Y
                    AxisOriginZ = [double]$point.Z
                    AxisDirectionX = 0.0
                    AxisDirectionY = 1.0
                    AxisDirectionZ = 0.0
                }

                Invoke-GuardedMutation -Tool 'element.rotate_safe' -Payload $rotatePayload -TargetDocument $targetDocument | Out-Null
                $rotationApplied = $true
                $rotationAngleDegrees = -90.0
                $rotationAxisDirection = @(0.0, 1.0, 0.0)
            }
        }

        $createdRows.Add([pscustomobject]@{
            PairNumber = [int]$row.PairNumber
            PairTag = $pairTag
            SourceWrapperElementId = $sourceId
            CanonicalExportTypeName = $canonicalTypeName
            OriginalActualTypeName = [string]$row.ActualTypeName
            ExportStrategy = [string]$row.ExportStrategy
            CreatedElementId = $createdId
            LevelId = $levelIdValue
            LevelName = [string]$source.LevelName
            PointX = [double]$point.X
            PointY = [double]$point.Y
            PointZ = [double]$point.Z
            RotationApplied = $rotationApplied
            RotationAngleDegrees = $rotationAngleDegrees
            RotationAxisDirection = $rotationAxisDirection
            Comment = "ROUND_MITEK_CANONICAL|$pairTag|SRC=$sourceId|TYPE=$canonicalTypeName"
            Mark = $pairTag
        }) | Out-Null
    }

    foreach ($chunk in (Split-IntoChunks -Items @($createdRows.ToArray()) -ChunkSize 20)) {
        $changes = New-Object System.Collections.Generic.List[object]
        foreach ($item in $chunk) {
            $changes.Add(@{ ElementId = [int]$item.CreatedElementId; ParameterName = 'Comments'; NewValue = [string]$item.Comment }) | Out-Null
            $changes.Add(@{ ElementId = [int]$item.CreatedElementId; ParameterName = 'Mark'; NewValue = [string]$item.Mark }) | Out-Null
        }

        Invoke-GuardedMutation -Tool 'parameter.set_safe' -Payload @{
            DocumentKey = $payloadDocumentKey
            Changes = @($changes.ToArray())
        } -TargetDocument $targetDocument | Out-Null
    }

    $exportPayload = [ordered]@{
        DocumentKey = $payloadDocumentKey
        PresetName = 'coordination_ifc'
        OutputRootName = $OutputRootName
        RelativeOutputPath = ("{0}\{1}" -f $RelativeOutputPathBase, $runId)
        FileName = $FileName
        ViewId = $null
        ViewName = $ViewName
        OverwriteExisting = $true
    }

    if ($ExecuteExport.IsPresent) {
        $exportMutation = Invoke-GuardedMutation -Tool 'export.ifc_safe' -Payload $exportPayload -TargetDocument $targetDocument
    }
    else {
        $previewOnly = Invoke-MutationPreview -Tool 'export.ifc_safe' -Payload $exportPayload -TargetDocument $targetDocument
        $exportMutation = [pscustomobject]@{
            Preview = $previewOnly
            PreviewPayload = ConvertFrom-PayloadJson -Response $previewOnly
            Execute = $null
            ExecutePayload = $null
        }
    }

    Write-JsonFile -Path (Join-Path $artifactDir 'ifc-export.result.json') -Data $exportMutation

    $createdQueryPayload = @{
        DocumentKey = $payloadDocumentKey
        ElementIds = [int[]]@($createdIds.ToArray())
        ViewScopeOnly = $false
        SelectedOnly = $false
        CategoryNames = @('Generic Models')
        ClassName = 'FamilyInstance'
        MaxResults = 500
        IncludeParameters = $true
    }
    $createdQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload $createdQueryPayload -TargetDocument $targetDocument)
    Write-JsonFile -Path (Join-Path $artifactDir 'created-surrogates.live-query.json') -Data $createdQuery

    $createdQueryMap = @{}
    foreach ($item in @($createdQuery.Items)) {
        $createdQueryMap[[int]$item.ElementId] = $item
    }

    foreach ($item in @($createdRows.ToArray())) {
        if ($createdQueryMap.ContainsKey([int]$item.CreatedElementId)) {
            $live = $createdQueryMap[[int]$item.CreatedElementId]
            Add-Member -InputObject $item -NotePropertyName IfcGuid -NotePropertyValue (Get-ParameterValue -ElementSummary $live -ParameterName 'IfcGUID') -Force
            Add-Member -InputObject $item -NotePropertyName LiveComments -NotePropertyValue (Get-ParameterValue -ElementSummary $live -ParameterName 'Comments') -Force
            Add-Member -InputObject $item -NotePropertyName LiveMark -NotePropertyValue (Get-ParameterValue -ElementSummary $live -ParameterName 'Mark') -Force
        }
    }
}
catch {
    $executionError = $_
}
finally {
    if ($CleanupAfter.IsPresent -and $createdIds.Count -gt 0) {
        foreach ($chunk in (Split-IntoChunks -Items @($createdIds.ToArray()) -ChunkSize 20)) {
            try {
                $deleteMutation = Invoke-GuardedMutation -Tool 'element.delete_safe' -Payload @{
                    DocumentKey = $payloadDocumentKey
                    ElementIds = [int[]]@($chunk)
                    AllowDependentDeletes = $false
                } -TargetDocument $targetDocument

                $cleanupLog.Add([pscustomobject]@{
                    ElementIds = [int[]]@($chunk)
                    StatusCode = if ($deleteMutation.Execute) { [string]$deleteMutation.Execute.StatusCode } else { '' }
                    ChangedIds = if ($deleteMutation.ExecutePayload) { @($deleteMutation.ExecutePayload.ChangedIds) } else { @() }
                }) | Out-Null
            }
            catch {
                $cleanupLog.Add([pscustomobject]@{
                    ElementIds = [int[]]@($chunk)
                    StatusCode = 'DELETE_FAILED'
                    Error = $_.Exception.Message
                }) | Out-Null
            }
        }
    }
}

Write-JsonFile -Path (Join-Path $artifactDir 'created-surrogates.json') -Data @($createdRows.ToArray())
Write-JsonFile -Path (Join-Path $artifactDir 'cleanup-log.json') -Data @($cleanupLog.ToArray())

$docAfter = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')
$exportArtifacts = @()
$exportStatus = 'NOT_RUN'
$exportSucceeded = $false
if ($null -ne $exportMutation) {
    if ($null -ne $exportMutation.Execute) {
        $exportArtifacts = @($exportMutation.Execute.Artifacts)
        $exportStatus = [string]$exportMutation.Execute.StatusCode
        $exportSucceeded = [bool]$exportMutation.Execute.Succeeded
    }
    elseif ($null -ne $exportMutation.Preview) {
        $exportArtifacts = @($exportMutation.Preview.Artifacts)
        $exportStatus = [string]$exportMutation.Preview.StatusCode
        $exportSucceeded = [bool]$exportMutation.Preview.Succeeded
    }
}

$ifcPath = ''
$outputArtifact = @(
    $exportArtifacts |
        Where-Object { $_ -like 'OutputPath=*' -or $_ -like 'Output=*' } |
        Select-Object -First 1
)
if ($outputArtifact.Count -gt 0) {
    $ifcPath = ($outputArtifact[0] -replace '^OutputPath=', '' -replace '^Output=', '')
}

$summary = [pscustomobject]@{
    GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    ArtifactDirectory = $artifactDir
    ContractArtifactDir = $ContractArtifactDir
    DocumentTitle = [string]$doc.Title
    DocumentKey = [string]$doc.DocumentKey
    PairCount = [int]$contractRows.Count
    LiveWrapperCount = [int]@($wrapperMap.Keys).Count
    ReviewCommentTaggedWrapperCount = [int]$reviewCommentCount
    ViewName = $ViewName
    HideReviewedFilterName = $HideReviewedFilterName
    ViewTemplateName = $ViewTemplateName
    UseActive3DOrientationWhenPossible = $UseActive3DOrientationWhenPossible
    CopySectionBoxFromActive3D = $CopySectionBoxFromActive3D
    CreatedSurrogateCount = [int]$createdRows.Count
    CleanupRequested = [bool]$CleanupAfter.IsPresent
    CleanupChunkCount = [int]$cleanupLog.Count
    ExportStatus = $exportStatus
    ExportSucceeded = $exportSucceeded
    ExportArtifacts = $exportArtifacts
    ExportIfcPath = $ifcPath
    ExecuteExport = [bool]$ExecuteExport.IsPresent
    DocumentIsModifiedAfterRun = if ($null -ne $docAfter) { [bool]$docAfter.IsModified } else { $null }
    ExecutionError = if ($executionError) { [string]$executionError.Exception.Message } else { '' }
}

Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
$summary | ConvertTo-Json -Depth 100

if ($executionError) {
    throw $executionError
}
