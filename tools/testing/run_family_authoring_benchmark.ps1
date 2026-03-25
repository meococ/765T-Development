param(
    [string]$BridgeExe = "",
    [string]$PresetPath = "",
    [string]$PlaybookPath = "",
    [string]$FamilyName = "",
    [string]$OutputRoot = "",
    [string]$ArtifactRoot = "",
    [switch]$Execute,
    [switch]$NoActivateInUi,
    [switch]$CompactSave
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Data,
        [int]$Depth = 100
    )

    $dir = Split-Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $Data | ConvertTo-Json -Depth $Depth | Set-Content -Path $Path -Encoding UTF8
}

function Assert-BridgeSuccess {
    param(
        [Parameter(Mandatory = $true)][object]$Response,
        [Parameter(Mandatory = $true)][string]$Tool
    )

    if ($null -eq $Response) {
        throw "Tool $Tool tr? v? null."
    }

    $diag = @($Response.Diagnostics | ForEach-Object { $_.Code + ':' + $_.Message }) -join ' | '
    if ([string]::IsNullOrWhiteSpace($diag)) {
        $diag = '<kh?ng c? diagnostics>'
    }

    if (-not $Response.Succeeded) {
        throw "Tool $Tool th?t b?i. Status=$($Response.StatusCode). Diag=$diag"
    }

    $hasErrorDiagnostic = @($Response.Diagnostics | Where-Object { [int]$_.Severity -ge 2 }).Count -gt 0
    if ($hasErrorDiagnostic) {
        throw "Tool $Tool c? error diagnostics d? response success. Status=$($Response.StatusCode). Diag=$diag"
    }
}

function ConvertFrom-PayloadJson {
    param([Parameter(Mandatory = $true)][object]$Response)
    if ([string]::IsNullOrWhiteSpace([string]$Response.PayloadJson)) {
        return $null
    }

    return ($Response.PayloadJson | ConvertFrom-Json)
}

function Resolve-MacroPath {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $resolved = $Value.Replace('{repoRoot}', $RepoRoot)
    $resolved = [Environment]::ExpandEnvironmentVariables($resolved)

    if ([System.IO.Path]::IsPathRooted($resolved)) {
        return [System.IO.Path]::GetFullPath($resolved)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $resolved))
}

function New-RunFolder {
    param(
        [Parameter(Mandatory = $true)][string]$BaseRoot,
        [Parameter(Mandatory = $true)][string]$FamilyName
    )

    if (-not (Test-Path $BaseRoot)) {
        New-Item -ItemType Directory -Path $BaseRoot -Force | Out-Null
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $safeFamilyName = ($FamilyName -replace '[^A-Za-z0-9._-]', '_')
    $path = Join-Path $BaseRoot ("{0}_{1}" -f $stamp, $safeFamilyName)
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    return $path
}

function Invoke-ReadTool {
    param(
        [Parameter(Mandatory = $true)][string]$BridgeExe,
        [Parameter(Mandatory = $true)][string]$Tool,
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
        [Parameter(Mandatory = $true)][string]$BridgeExe,
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$Payload,
        [string]$TargetDocument = ""
    )

    $payloadJson = $Payload | ConvertTo-Json -Depth 100
    $response = Invoke-BridgeJson -BridgeExe $BridgeExe -Tool $Tool -TargetDocument $TargetDocument -PayloadJson $payloadJson -DryRun
    Assert-BridgeSuccess -Response $response -Tool $Tool
    return $response
}

function Invoke-MutationExecute {
    param(
        [Parameter(Mandatory = $true)][string]$BridgeExe,
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$Payload,
        [Parameter(Mandatory = $true)][string]$ApprovalToken,
        [Parameter(Mandatory = $true)][string]$PreviewRunId,
        [object]$ExpectedContext = $null,
        [string]$TargetDocument = ""
    )

    $tmpPayload = Join-Path $env:TEMP ("bim765t_family_benchmark_payload_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
    $tmpExpectedContext = $null
    try {
        $Payload | ConvertTo-Json -Depth 100 | Set-Content -Path $tmpPayload -Encoding UTF8
        $args = @($Tool, '--dry-run', 'false')
        if (-not [string]::IsNullOrWhiteSpace($TargetDocument)) {
            $args += @('--target-document', $TargetDocument)
        }
        if ($null -ne $ExpectedContext) {
            $tmpExpectedContext = Join-Path $env:TEMP ("bim765t_family_benchmark_context_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
            $ExpectedContext | ConvertTo-Json -Depth 100 | Set-Content -Path $tmpExpectedContext -Encoding UTF8
            $args += @('--expected-context', $tmpExpectedContext)
        }

        $args += @('--payload', $tmpPayload, '--approval-token', $ApprovalToken, '--preview-run-id', $PreviewRunId)
        $raw = & $BridgeExe @args
        if (-not $raw) {
            throw "Bridge trả về rỗng cho tool $Tool khi execute."
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

function Invoke-BenchmarkMutationStep {
    param(
        [Parameter(Mandatory = $true)][string]$BridgeExe,
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$Payload,
        [Parameter(Mandatory = $true)][string]$StepName,
        [Parameter(Mandatory = $true)][string]$ArtifactDir,
        [string]$TargetDocument = "",
        [switch]$ExecuteMutation
    )

    $safeStepName = ($StepName -replace '[^A-Za-z0-9._-]', '_')
    $preview = Invoke-MutationPreview -BridgeExe $BridgeExe -Tool $Tool -Payload $Payload -TargetDocument $TargetDocument
    Write-JsonFile -Path (Join-Path $ArtifactDir ("{0}.preview.json" -f $safeStepName)) -Data $preview

    $previewPayload = ConvertFrom-PayloadJson -Response $preview
    $result = [ordered]@{
        StepName = $StepName
        Tool = $Tool
        PreviewStatus = [string]$preview.StatusCode
        ExecuteStatus = ''
        ApprovalToken = [string]$preview.ApprovalToken
        PreviewRunId = [string]$preview.PreviewRunId
        ChangedIds = @()
    }

    if ($ExecuteMutation) {
        $expectedContext = $null
        if ($null -ne $previewPayload -and $null -ne $previewPayload.PSObject.Properties['ResolvedContext']) {
            $expectedContext = $previewPayload.ResolvedContext
            if ($null -ne $expectedContext -and $null -ne $expectedContext.PSObject.Properties['ActiveDocEpoch']) {
                $expectedContext.ActiveDocEpoch = 0
            }
        }

        $execute = Invoke-MutationExecute `
            -BridgeExe $BridgeExe `
            -Tool $Tool `
            -Payload $Payload `
            -ApprovalToken ([string]$preview.ApprovalToken) `
            -PreviewRunId ([string]$preview.PreviewRunId) `
            -ExpectedContext $expectedContext `
            -TargetDocument $TargetDocument

        Write-JsonFile -Path (Join-Path $ArtifactDir ("{0}.execute.json" -f $safeStepName)) -Data $execute
        $result.ExecuteStatus = [string]$execute.StatusCode
        $result.ChangedIds = @($execute.ChangedIds)
    }

    return [pscustomobject]$result
}

function Add-RunStep {
    param(
        [System.Collections.Generic.List[object]]$Steps,
        [Parameter(Mandatory = $true)][object]$Step
    )

    $Steps.Add($Step) | Out-Null
}

function Resolve-FamilyDocumentKey {
    param(
        [Parameter(Mandatory = $true)][string]$BridgeExe,
        [Parameter(Mandatory = $true)][string]$ExpectedFamilyName
    )

    $active = Invoke-ReadTool -BridgeExe $BridgeExe -Tool 'document.get_active'
    $doc = ConvertFrom-PayloadJson -Response $active
    if ($null -eq $doc) {
        throw 'document.get_active không trả payload.'
    }

    if (-not $doc.IsFamilyDocument) {
        throw "Active document hiện tại không phải family document sau khi tạo benchmark. Title=$($doc.Title)"
    }

    $title = [string]$doc.Title
    $pathName = [string]$doc.PathName
    if ($title -notlike "*$ExpectedFamilyName*" -and $pathName -notlike "*$ExpectedFamilyName*") {
        Write-Warning "Active family document không match hoàn toàn benchmark name. Title=$title Path=$pathName"
    }

    return [string]$doc.DocumentKey
}

function Test-BenchmarkVerification {
    param(
        [Parameter(Mandatory = $true)][object]$Xray,
        [Parameter(Mandatory = $true)][object]$Geometry,
        [Parameter(Mandatory = $true)][object]$Preset
    )

    $verify = $Preset.Verify
    $results = New-Object System.Collections.Generic.List[object]

    $results.Add([pscustomobject]@{ Check = 'forms_min'; Passed = (@($Geometry.Forms).Count -ge [int]$verify.MinimumForms); Actual = @($Geometry.Forms).Count; Expected = [int]$verify.MinimumForms }) | Out-Null
    $results.Add([pscustomobject]@{ Check = 'reference_planes_min'; Passed = (@($Geometry.ReferencePlaneNames).Count -ge [int]$verify.MinimumReferencePlanes); Actual = @($Geometry.ReferencePlaneNames).Count; Expected = [int]$verify.MinimumReferencePlanes }) | Out-Null
    $results.Add([pscustomobject]@{ Check = 'formula_count_min'; Passed = (@($Xray.FormulaParameters).Count -ge [int]$verify.MinimumFormulaCount); Actual = @($Xray.FormulaParameters).Count; Expected = [int]$verify.MinimumFormulaCount }) | Out-Null
    $results.Add([pscustomobject]@{ Check = 'types_min'; Passed = [int]$Xray.TypesCount -ge [int]$verify.MinimumTypeCount; Actual = [int]$Xray.TypesCount; Expected = [int]$verify.MinimumTypeCount }) | Out-Null

    foreach ($name in @($verify.RequiredReferencePlanes)) {
        $results.Add([pscustomobject]@{ Check = "refplane:$name"; Passed = @($Geometry.ReferencePlaneNames) -contains $name; Actual = [bool](@($Geometry.ReferencePlaneNames) -contains $name); Expected = $true }) | Out-Null
    }

    foreach ($typeName in @($verify.RequiredTypeNames)) {
        $results.Add([pscustomobject]@{ Check = "type:$typeName"; Passed = @($Xray.TypeNames) -contains $typeName; Actual = [bool](@($Xray.TypeNames) -contains $typeName); Expected = $true }) | Out-Null
    }

    return @($results)
}

$projectRoot = Resolve-ProjectRoot
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

if ([string]::IsNullOrWhiteSpace($PresetPath)) {
    $PresetPath = Join-Path $projectRoot 'docs\agent\presets\family_benchmark_servicebox.v1.json'
}
if ([string]::IsNullOrWhiteSpace($PlaybookPath)) {
    $PlaybookPath = Join-Path $projectRoot 'docs\agent\playbooks\family_benchmark_servicebox.v1.json'
}

$preset = Get-Content -Path $PresetPath -Raw | ConvertFrom-Json
$playbook = Get-Content -Path $PlaybookPath -Raw | ConvertFrom-Json

$resolvedFamilyName = if ([string]::IsNullOrWhiteSpace($FamilyName)) { [string]$preset.FamilyName } else { $FamilyName }
$resolvedOutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) { Resolve-MacroPath -Value ([string]$preset.OutputRoot) -RepoRoot $projectRoot } else { Resolve-MacroPath -Value $OutputRoot -RepoRoot $projectRoot }
$resolvedArtifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { Resolve-MacroPath -Value ([string]$preset.ArtifactRoot) -RepoRoot $projectRoot } else { Resolve-MacroPath -Value $ArtifactRoot -RepoRoot $projectRoot }

$runArtifactDir = New-RunFolder -BaseRoot $resolvedArtifactRoot -FamilyName $resolvedFamilyName
$familyOutputDir = New-RunFolder -BaseRoot $resolvedOutputRoot -FamilyName $resolvedFamilyName
$familySavePath = Join-Path $familyOutputDir ($resolvedFamilyName + '.rfa')
$activation = -not $NoActivateInUi
$runMode = if ($Execute) { 'execute' } else { 'plan' }

$plan = [ordered]@{
    BenchmarkId = [string]$playbook.PlaybookId
    Version = [string]$playbook.Version
    FamilyName = $resolvedFamilyName
    RunMode = $runMode
    BridgeExe = $BridgeExe
    PresetPath = $PresetPath
    PlaybookPath = $PlaybookPath
    FamilySavePath = $familySavePath
    ArtifactDir = $runArtifactDir
    ActivateInUi = $activation
    CompactSave = [bool]$CompactSave
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    PlannedSteps = @('create_document', 'add_parameters', 'add_reference_planes', 'create_subcategories', 'create_geometry', 'assign_subcategories', 'bind_materials', 'bind_visibility', 'add_dimensions', 'set_formulas', 'set_type_catalog', 'save', 'verify_xray', 'verify_geometry')
}
Write-JsonFile -Path (Join-Path $runArtifactDir 'plan.json') -Data $plan

if (-not $Execute) {
    Write-Host 'Da tao plan benchmark family authoring.' -ForegroundColor Cyan
    Write-Host ("Family se duoc luu tai: {0}" -f $familySavePath) -ForegroundColor Yellow
    Write-Host ("Artifacts: {0}" -f $runArtifactDir) -ForegroundColor Yellow
    Write-Host 'Dung -Execute de chay authoring workflow live qua bridge.' -ForegroundColor Green
    return
}

$runSteps = New-Object 'System.Collections.Generic.List[object]'
$geometryMap = @{}

try {
    $createDocumentPayload = [ordered]@{
        TemplateCategory = [string]$preset.TemplateCategory
        SaveAsPath = $familySavePath
        ActivateInUI = $activation
        UnitSystem = [string]$preset.UnitSystem
    }

    $createStep = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.create_document_safe' -Payload $createDocumentPayload -StepName '01_create_document' -ArtifactDir $runArtifactDir -ExecuteMutation
    Add-RunStep -Steps $runSteps -Step $createStep

    $familyDocumentKey = Resolve-FamilyDocumentKey -BridgeExe $BridgeExe -ExpectedFamilyName $resolvedFamilyName

    $parameterIndex = 0
    foreach ($parameter in @($preset.Parameters)) {
        $parameterIndex++
        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.add_parameter_safe' -Payload $parameter -StepName ("02_parameter_{0:D2}_{1}" -f $parameterIndex, [string]$parameter.ParameterName) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $planeIndex = 0
    foreach ($plane in @($preset.ReferencePlanes)) {
        $planeIndex++
        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.add_reference_plane_safe' -Payload $plane -StepName ("03_refplane_{0:D2}_{1}" -f $planeIndex, [string]$plane.Name) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $subcatIndex = 0
    foreach ($subcat in @($preset.Subcategories)) {
        $subcatIndex++
        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.create_subcategory_safe' -Payload $subcat -StepName ("04_subcategory_{0:D2}_{1}" -f $subcatIndex, [string]$subcat.SubcategoryName) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $geometryIndex = 0
    foreach ($geometry in @($preset.Geometry)) {
        $geometryIndex++
        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool ([string]$geometry.Tool) -Payload $geometry.Payload -StepName ("05_geometry_{0:D2}_{1}" -f $geometryIndex, [string]$geometry.Key) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
        $changedIds = @($step.ChangedIds)
        if ($changedIds.Count -gt 0) {
            $geometryMap[[string]$geometry.Key] = [int]$changedIds[0]
        }
    }

    $assignIndex = 0
    foreach ($assignment in @($preset.SubcategoryAssignments)) {
        $assignIndex++
        $geometryId = $geometryMap[[string]$assignment.GeometryKey]
        if (-not $geometryId) {
            throw "Khong tim thay geometry id cho key '$($assignment.GeometryKey)' de gan subcategory."
        }

        $payload = [ordered]@{
            FormElementId = $geometryId
            SubcategoryName = [string]$assignment.SubcategoryName
        }

        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.set_subcategory_safe' -Payload $payload -StepName ("06_assign_subcategory_{0:D2}_{1}" -f $assignIndex, [string]$assignment.GeometryKey) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $materialIndex = 0
    foreach ($binding in @($preset.MaterialBindings)) {
        $materialIndex++
        $geometryId = $geometryMap[[string]$binding.GeometryKey]
        if (-not $geometryId) {
            throw "Khong tim thay geometry id cho key '$($binding.GeometryKey)' de bind material."
        }

        $payload = [ordered]@{
            FormElementId = $geometryId
            MaterialParameterName = [string]$binding.MaterialParameterName
            DefaultMaterialName = [string]$binding.DefaultMaterialName
        }

        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.bind_material_safe' -Payload $payload -StepName ("07_bind_material_{0:D2}_{1}" -f $materialIndex, [string]$binding.GeometryKey) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $visibilityIndex = 0
    foreach ($visibility in @($preset.VisibilityBindings)) {
        $visibilityIndex++
        $geometryId = $geometryMap[[string]$visibility.GeometryKey]
        if (-not $geometryId) {
            throw "Khong tim thay geometry id cho key '$($visibility.GeometryKey)' de gan visibility."
        }

        $payload = [ordered]@{
            FormElementId = $geometryId
            VisibilityParameterName = [string]$visibility.VisibilityParameterName
            DefaultVisible = [bool]$visibility.DefaultVisible
        }

        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.set_parameter_visibility_safe' -Payload $payload -StepName ("08_bind_visibility_{0:D2}_{1}" -f $visibilityIndex, [string]$visibility.GeometryKey) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $dimensionIndex = 0
    foreach ($dimension in @($preset.Dimensions)) {
        $dimensionIndex++
        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.add_dimension_safe' -Payload $dimension -StepName ("09_dimension_{0:D2}_{1}" -f $dimensionIndex, [string]$dimension.LabelParameterName) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $formulaIndex = 0
    foreach ($formula in @($preset.Formulas)) {
        $formulaIndex++
        $step = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.set_parameter_formula_safe' -Payload $formula -StepName ("10_formula_{0:D2}_{1}" -f $formulaIndex, [string]$formula.ParameterName) -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
        Add-RunStep -Steps $runSteps -Step $step
    }

    $typeCatalogStep = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.set_type_catalog_safe' -Payload $preset.TypeCatalog -StepName '11_type_catalog' -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
    Add-RunStep -Steps $runSteps -Step $typeCatalogStep

    $savePayload = [ordered]@{
        SaveAsPath = $familySavePath
        OverwriteExisting = $true
        CompactFile = [bool]$CompactSave
    }

    $saveStep = Invoke-BenchmarkMutationStep -BridgeExe $BridgeExe -Tool 'family.save_safe' -Payload $savePayload -StepName '12_save_family' -ArtifactDir $runArtifactDir -TargetDocument $familyDocumentKey -ExecuteMutation
    Add-RunStep -Steps $runSteps -Step $saveStep

    $xrayResponse = Invoke-ReadTool -BridgeExe $BridgeExe -Tool 'family.xray' -TargetDocument $familyDocumentKey
    $xray = ConvertFrom-PayloadJson -Response $xrayResponse
    Write-JsonFile -Path (Join-Path $runArtifactDir 'family-xray.json') -Data $xray

    $geometryResponse = Invoke-ReadTool -BridgeExe $BridgeExe -Tool 'family.list_geometry' -TargetDocument $familyDocumentKey
    $geometry = ConvertFrom-PayloadJson -Response $geometryResponse
    Write-JsonFile -Path (Join-Path $runArtifactDir 'family-geometry.json') -Data $geometry

    $verification = Test-BenchmarkVerification -Xray $xray -Geometry $geometry -Preset $preset
    Write-JsonFile -Path (Join-Path $runArtifactDir 'verification.json') -Data $verification

    $runReport = [ordered]@{
        BenchmarkId = [string]$playbook.PlaybookId
        FamilyName = $resolvedFamilyName
        FamilyDocumentKey = $familyDocumentKey
        FamilySavePath = $familySavePath
        ArtifactDir = $runArtifactDir
        ExecuteMode = $true
        StartedUtc = $plan.CreatedUtc
        FinishedUtc = [DateTime]::UtcNow.ToString('o')
        StepCount = $runSteps.Count
        Steps = @($runSteps)
        GeometryMap = $geometryMap
        XraySummary = [ordered]@{
            FamilyName = [string]$xray.FamilyName
            CategoryName = [string]$xray.CategoryName
            TypesCount = [int]$xray.TypesCount
            FormulaCount = @($xray.FormulaParameters).Count
            ReferencePlaneCount = @($xray.ReferencePlanes).Count
            Issues = @($xray.Issues)
        }
        GeometrySummary = [ordered]@{
            FormsCount = @($geometry.Forms).Count
            ReferencePlanesCount = @($geometry.ReferencePlaneNames).Count
            ParameterCount = [int]$geometry.ParameterCount
            TypeCount = [int]$geometry.TypeCount
        }
        Verification = $verification
    }

    Write-JsonFile -Path (Join-Path $runArtifactDir 'run-report.json') -Data $runReport

    Write-Host 'Benchmark family authoring da chay xong.' -ForegroundColor Green
    Write-Host ("Family: {0}" -f $familySavePath) -ForegroundColor Cyan
    Write-Host ("Artifacts: {0}" -f $runArtifactDir) -ForegroundColor Cyan
}
catch {
    $stepSnapshot = @()
    if ($null -ne $runSteps) {
        $stepSnapshot = @($runSteps | ForEach-Object { $_ })
    }

    $errorReport = [pscustomobject]@{
        BenchmarkId = [string]$playbook.PlaybookId
        FamilyName = $resolvedFamilyName
        ArtifactDir = $runArtifactDir
        ExecuteMode = [bool]$Execute
        Error = $_.Exception.Message
        Steps = $stepSnapshot
        FailedUtc = [DateTime]::UtcNow.ToString('o')
    }

    Write-JsonFile -Path (Join-Path $runArtifactDir 'run-error.json') -Data $errorReport
    throw
}
