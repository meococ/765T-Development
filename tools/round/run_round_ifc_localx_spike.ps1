param(
    [string]$BridgeExe = "",
    [string]$ViewName = "3D IFC Penetration Export",
    [string]$OutputRootName = "documents_exports",
    [string]$RelativeOutputPathBase = "round-ifc-localx-spike",
    [string]$FileName = "round-localx-contract-spike.ifc",
    [double]$OffsetX = 20.0,
    [double]$OffsetY = 20.0,
    [double]$OffsetZ = 5.0,
    [double]$SampleSpacing = 8.0,
    [switch]$ExecuteExport,
    [switch]$CleanupAfter = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Join-Path $PSScriptRoot '..')
$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$artifactDir = Join-Path $projectRoot ("artifacts\round-ifc-localx-spike\{0}" -f $runId)
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
        return ($raw | ConvertFrom-Json)
    }
    finally {
        Remove-Item $tmpPayload -Force -ErrorAction SilentlyContinue
        if ($tmpExpectedContext) {
            Remove-Item $tmpExpectedContext -Force -ErrorAction SilentlyContinue
        }
    }
}

function Find-LatestIfcSpikeDir {
    param([string]$ProjectRoot)

    $root = Join-Path $ProjectRoot 'artifacts\round-ifc-mitek-spike'
    if (-not (Test-Path $root)) {
        throw "Khong tim thay artifact root: $root"
    }

    $dir = Get-ChildItem -Path $root -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'spike-samples.json') } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $dir) {
        throw "Khong tim thay round-ifc-mitek-spike artifact co spike-samples.json"
    }

    return $dir.FullName
}

function Load-SeedSample {
    param([string]$IfcSpikeDir)

    $samples = Get-Content -Raw -Path (Join-Path $IfcSpikeDir 'spike-samples.json') | ConvertFrom-Json
    if ($samples.Count -eq 1 -and $samples[0] -is [System.Array]) {
        $samples = @($samples[0])
    }
    else {
        $samples = @($samples)
    }
    $seed = @($samples | Where-Object { [string]$_.ActualGeometryAxis -eq 'AXIS_X' } | Select-Object -First 1)
    if ($seed.Count -eq 0) {
        throw "Khong tim thay seed sample AXIS_X trong artifact: $IfcSpikeDir"
    }

    return $seed[0]
}

function New-CasePoint {
    param(
        [double]$BaseX,
        [double]$BaseY,
        [double]$BaseZ,
        [int]$Index
    )

    return [pscustomobject]@{
        X = $BaseX
        Y = ($BaseY + ($Index * $SampleSpacing))
        Z = $BaseZ
    }
}

function Get-IfcEntityMap {
    param([string]$IfcPath)

    $map = @{}
    foreach ($line in Get-Content -Path $IfcPath) {
        if ($line -match '^(#\d+)=') {
            $map[$matches[1]] = $line.Trim()
        }
    }

    return $map
}

function Split-IfcArgs {
    param([string]$Text)

    $result = New-Object System.Collections.Generic.List[string]
    $buffer = New-Object System.Text.StringBuilder
    $depth = 0
    $quote = $false

    foreach ($char in $Text.ToCharArray()) {
        if ($char -eq "'") {
            $quote = -not $quote
            [void]$buffer.Append($char)
            continue
        }

        if (-not $quote) {
            if ($char -eq '(') {
                $depth++
            }
            elseif ($char -eq ')') {
                $depth--
            }
            elseif ($char -eq ',' -and $depth -eq 0) {
                $result.Add($buffer.ToString().Trim()) | Out-Null
                [void]$buffer.Clear()
                continue
            }
        }

        [void]$buffer.Append($char)
    }

    $remaining = $buffer.ToString().Trim()
    if (-not [string]::IsNullOrWhiteSpace($remaining)) {
        $result.Add($remaining) | Out-Null
    }

    return @($result.ToArray())
}

function Resolve-IfcDirection {
    param(
        [hashtable]$EntityMap,
        [string]$Token,
        [double[]]$Default
    )

    if ([string]::IsNullOrWhiteSpace($Token) -or $Token -eq '$') {
        return $Default
    }

    if (-not $EntityMap.ContainsKey($Token)) {
        return $null
    }

    $line = $EntityMap[$Token]
    if ($line -match 'IFCDIRECTION\(\((?<values>[^\)]+)\)\)') {
        return @($matches.values.Split(',') | ForEach-Object { [double]($_.Trim()) })
    }

    return $null
}

function Resolve-IfcPoint {
    param(
        [hashtable]$EntityMap,
        [string]$Token
    )

    if ([string]::IsNullOrWhiteSpace($Token) -or $Token -eq '$') {
        return $null
    }

    if (-not $EntityMap.ContainsKey($Token)) {
        return $null
    }

    $line = $EntityMap[$Token]
    if ($line -match 'IFCCARTESIANPOINT\(\((?<values>[^\)]+)\)\)') {
        return @($matches.values.Split(',') | ForEach-Object { [double]($_.Trim()) })
    }

    return $null
}

function Resolve-AxisPlacement3D {
    param(
        [hashtable]$EntityMap,
        [string]$Token
    )

    if ([string]::IsNullOrWhiteSpace($Token) -or $Token -eq '$' -or -not $EntityMap.ContainsKey($Token)) {
        return $null
    }

    $line = $EntityMap[$Token]
    if ($line -notmatch 'IFCAXIS2PLACEMENT3D\((?<args>.+)\);$') {
        return $null
    }

    $args = Split-IfcArgs -Text $matches.args
    if ($args.Count -lt 3) {
        return $null
    }

    $origin = Resolve-IfcPoint -EntityMap $EntityMap -Token $args[0]
    $axis = Resolve-IfcDirection -EntityMap $EntityMap -Token $args[1] -Default @(0.0, 0.0, 1.0)
    $refDir = Resolve-IfcDirection -EntityMap $EntityMap -Token $args[2] -Default @(1.0, 0.0, 0.0)

    return [pscustomobject]@{
        AxisPlacementId = $Token
        Origin = $origin
        Axis = $axis
        RefDirection = $refDir
        RawLine = $line
    }
}

function Resolve-LocalPlacement {
    param(
        [hashtable]$EntityMap,
        [string]$Token
    )

    if ([string]::IsNullOrWhiteSpace($Token) -or $Token -eq '$' -or -not $EntityMap.ContainsKey($Token)) {
        return $null
    }

    $line = $EntityMap[$Token]
    if ($line -notmatch 'IFCLOCALPLACEMENT\((?<args>.+)\);$') {
        return $null
    }

    $args = Split-IfcArgs -Text $matches.args
    if ($args.Count -lt 2) {
        return $null
    }

    return [pscustomobject]@{
        LocalPlacementId = $Token
        ParentPlacementId = $args[0]
        RelativePlacement = Resolve-AxisPlacement3D -EntityMap $EntityMap -Token $args[1]
        RawLine = $line
    }
}

function Resolve-MappedRepresentationEvidence {
    param(
        [hashtable]$EntityMap,
        [string]$ProductShapeId
    )

    if ([string]::IsNullOrWhiteSpace($ProductShapeId) -or -not $EntityMap.ContainsKey($ProductShapeId)) {
        return $null
    }

    $shapeLine = $EntityMap[$ProductShapeId]
    if ($shapeLine -notmatch 'IFCPRODUCTDEFINITIONSHAPE\(.+\((?<repRef>#\d+)\)\);$') {
        return [pscustomobject]@{
            ProductShapeId = $ProductShapeId
            ShapeLine = $shapeLine
            RepresentationId = $null
            RepresentationLine = $null
            MappedItemId = $null
            MappedItemLine = $null
            TransformId = $null
            TransformLine = $null
        }
    }

    $repRef = $matches.repRef
    $repLine = if ($EntityMap.ContainsKey($repRef)) { $EntityMap[$repRef] } else { $null }
    $mappedId = $null
    $mappedLine = $null
    $transformId = $null
    $transformLine = $null

    if ($repLine -and $repLine -match 'IFCSHAPEREPRESENTATION\(.+\((?<mappedRef>#\d+)\)\);$') {
        $mappedId = $matches.mappedRef
        if ($EntityMap.ContainsKey($mappedId)) {
            $mappedLine = $EntityMap[$mappedId]
            if ($mappedLine -match 'IFCMAPPEDITEM\((?<mapRef>#\d+),(?<transformRef>#\d+)\);$') {
                $transformId = $matches.transformRef
                if ($EntityMap.ContainsKey($transformId)) {
                    $transformLine = $EntityMap[$transformId]
                }
            }
        }
    }

    return [pscustomobject]@{
        ProductShapeId = $ProductShapeId
        ShapeLine = $shapeLine
        RepresentationId = $repRef
        RepresentationLine = $repLine
        MappedItemId = $mappedId
        MappedItemLine = $mappedLine
        TransformId = $transformId
        TransformLine = $transformLine
    }
}

function Resolve-IfcProductEvidence {
    param(
        [hashtable]$EntityMap,
        [string]$IfcGuid
    )

    $productLine = $null
    foreach ($line in $EntityMap.Values) {
        if ($line -like "*'$IfcGuid'*" -and $line -match '^#\d+=IFC[A-Z0-9_]+\(') {
            $productLine = $line
            break
        }
    }

    if ($null -eq $productLine) {
        return $null
    }

    if ($productLine -notmatch "^(?<productId>#\d+)=IFC[A-Z0-9_]+\('(?<guid>[^']+)',#[^,]+,'(?<name>[^']*)',(?<desc>[^,]*),(?<objType>[^,]*),(?<placement>#\d+),(?<shape>#\d+),'(?<tag>[^']*)'") {
        return [pscustomobject]@{
            IfcGuid = $IfcGuid
            ProductLine = $productLine
            ProductId = $null
            ProductName = $null
            Tag = $null
            LocalPlacement = $null
            Representation = $null
        }
    }

    $placementRef = $matches.placement
    $shapeRef = $matches.shape

    return [pscustomobject]@{
        IfcGuid = $IfcGuid
        ProductId = $matches.productId
        ProductName = $matches.name
        Tag = $matches.tag
        ProductLine = $productLine
        LocalPlacement = Resolve-LocalPlacement -EntityMap $EntityMap -Token $placementRef
        Representation = Resolve-MappedRepresentationEvidence -EntityMap $EntityMap -ProductShapeId $shapeRef
    }
}

$createdIds = New-Object System.Collections.Generic.List[int]

try {
    $seedSpikeDir = Find-LatestIfcSpikeDir -ProjectRoot $projectRoot
    $seedSample = Load-SeedSample -IfcSpikeDir $seedSpikeDir

    $doc = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')

    $seedQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload @{
        DocumentKey = [string]$doc.DocumentKey
        ElementIds = @([int]$seedSample.NewWrapperElementId)
        ViewScopeOnly = $false
        SelectedOnly = $false
        CategoryNames = @('Generic Models')
        ClassName = 'FamilyInstance'
        MaxResults = 5
        IncludeParameters = $true
    } -TargetDocument $doc.DocumentKey)

    if (@($seedQuery.Items).Count -ne 1) {
        throw "Khong query duoc seed wrapper element: $($seedSample.NewWrapperElementId)"
    }

    $seedItem = $seedQuery.Items[0]
    if ([string]$seedItem.FamilyPlacementType -ne 'OneLevelBased') {
        throw "Seed wrapper khong phai OneLevelBased. PlacementType=$($seedItem.FamilyPlacementType)"
    }

    if ($null -eq $seedItem.LocationPoint) {
        throw "Seed wrapper khong co LocationPoint."
    }

    $seedLevelId = [int]$seedItem.LevelId
    $seedTypeId = [int]$seedItem.TypeId
    $basePoint = [pscustomobject]@{
        X = [double]$seedItem.LocationPoint.X + $OffsetX
        Y = [double]$seedItem.LocationPoint.Y + $OffsetY
        Z = [double]$seedItem.LocationPoint.Z + $OffsetZ
    }

    $cases = @(
        [pscustomobject]@{
            CaseKey = 'LOCALX_GLOBALX'
            Description = 'AXIS_X type dat nguyen ban'
            Point = New-CasePoint -BaseX $basePoint.X -BaseY $basePoint.Y -BaseZ $basePoint.Z -Index 0
            Rotate = $null
        }
        [pscustomobject]@{
            CaseKey = 'LOCALX_GLOBALY'
            Description = 'AXIS_X type rotate +90 quanh global Z'
            Point = New-CasePoint -BaseX $basePoint.X -BaseY $basePoint.Y -BaseZ $basePoint.Z -Index 1
            Rotate = [pscustomobject]@{
                AngleDegrees = 90.0
                AxisDirection = @(0.0, 0.0, 1.0)
            }
        }
        [pscustomobject]@{
            CaseKey = 'LOCALX_GLOBALZ'
            Description = 'AXIS_X type rotate -90 quanh global Y'
            Point = New-CasePoint -BaseX $basePoint.X -BaseY $basePoint.Y -BaseZ $basePoint.Z -Index 2
            Rotate = [pscustomobject]@{
                AngleDegrees = -90.0
                AxisDirection = @(0.0, 1.0, 0.0)
            }
        }
    )

    $caseResults = New-Object System.Collections.Generic.List[object]

    foreach ($case in $cases) {
        $placePayload = [ordered]@{
            DocumentKey = [string]$doc.DocumentKey
            FamilySymbolId = $seedTypeId
            LevelId = $seedLevelId
            ViewId = $null
            HostElementId = $null
            PlacementMode = 'auto'
            StructuralTypeName = 'NonStructural'
            X = [double]$case.Point.X
            Y = [double]$case.Point.Y
            Z = [double]$case.Point.Z
            RotateRadians = 0.0
            Notes = "ROUND_LOCALX_SPIKE|$($case.CaseKey)"
        }

        $placePreview = Invoke-MutationPreview -Tool 'element.place_family_instance_safe' -Payload $placePayload -TargetDocument $doc.DocumentKey
        $placePreviewPayload = ConvertFrom-PayloadJson -Response $placePreview
        $placeExecute = Invoke-MutationExecute `
            -Tool 'element.place_family_instance_safe' `
            -Payload $placePayload `
            -ApprovalToken $placePreview.ApprovalToken `
            -PreviewRunId $placePreview.PreviewRunId `
            -ExpectedContext $placePreviewPayload.ResolvedContext `
            -TargetDocument $doc.DocumentKey
        Assert-BridgeSuccess -Response $placeExecute -Tool 'element.place_family_instance_safe'
        $createdId = [int]($placeExecute.PayloadJson | ConvertFrom-Json).ChangedIds[0]
        $createdIds.Add($createdId) | Out-Null

        $caseResult = [ordered]@{
            CaseKey = [string]$case.CaseKey
            Description = [string]$case.Description
            CreatedElementId = $createdId
            PointX = [double]$case.Point.X
            PointY = [double]$case.Point.Y
            PointZ = [double]$case.Point.Z
            RotationApplied = $false
            RotationAngleDegrees = 0.0
            RotationAxisDirection = $null
            IfcGuid = ''
            Comment = ''
            Mark = ''
        }

        if ($null -ne $case.Rotate) {
            $rotatePayload = [ordered]@{
                DocumentKey = [string]$doc.DocumentKey
                ElementIds = @($createdId)
                AngleDegrees = [double]$case.Rotate.AngleDegrees
                AxisMode = 'explicit'
                AxisOriginX = [double]$case.Point.X
                AxisOriginY = [double]$case.Point.Y
                AxisOriginZ = [double]$case.Point.Z
                AxisDirectionX = [double]$case.Rotate.AxisDirection[0]
                AxisDirectionY = [double]$case.Rotate.AxisDirection[1]
                AxisDirectionZ = [double]$case.Rotate.AxisDirection[2]
            }

            $rotatePreview = Invoke-MutationPreview -Tool 'element.rotate_safe' -Payload $rotatePayload -TargetDocument $doc.DocumentKey
            $rotatePreviewPayload = ConvertFrom-PayloadJson -Response $rotatePreview
            $rotateExecute = Invoke-MutationExecute `
                -Tool 'element.rotate_safe' `
                -Payload $rotatePayload `
                -ApprovalToken $rotatePreview.ApprovalToken `
                -PreviewRunId $rotatePreview.PreviewRunId `
                -ExpectedContext $rotatePreviewPayload.ResolvedContext `
                -TargetDocument $doc.DocumentKey
            Assert-BridgeSuccess -Response $rotateExecute -Tool 'element.rotate_safe'

            $caseResult.RotationApplied = $true
            $caseResult.RotationAngleDegrees = [double]$case.Rotate.AngleDegrees
            $caseResult.RotationAxisDirection = @($case.Rotate.AxisDirection)
        }

        $commentValue = "ROUND_LOCALX_SPIKE|CASE=$($case.CaseKey)|TYPE=$($seedItem.TypeName)"
        $parameterPayload = @{
            DocumentKey = [string]$doc.DocumentKey
            Changes = @(
                @{ ElementId = $createdId; ParameterName = 'Comments'; NewValue = $commentValue },
                @{ ElementId = $createdId; ParameterName = 'Mark'; NewValue = $case.CaseKey }
            )
        }

        $parameterPreview = Invoke-MutationPreview -Tool 'parameter.set_safe' -Payload $parameterPayload -TargetDocument $doc.DocumentKey
        $parameterPreviewPayload = ConvertFrom-PayloadJson -Response $parameterPreview
        $parameterExecute = Invoke-MutationExecute `
            -Tool 'parameter.set_safe' `
            -Payload $parameterPayload `
            -ApprovalToken $parameterPreview.ApprovalToken `
            -PreviewRunId $parameterPreview.PreviewRunId `
            -ExpectedContext $parameterPreviewPayload.ResolvedContext `
            -TargetDocument $doc.DocumentKey
        Assert-BridgeSuccess -Response $parameterExecute -Tool 'parameter.set_safe'

        $caseResult.Comment = $commentValue
        $caseResult.Mark = [string]$case.CaseKey
        $caseResults.Add([pscustomobject]$caseResult) | Out-Null
    }

    $createdQuery = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'element.query' -Payload @{
        DocumentKey = [string]$doc.DocumentKey
        ElementIds = [int[]]@($createdIds.ToArray())
        ViewScopeOnly = $false
        SelectedOnly = $false
        CategoryNames = @('Generic Models')
        ClassName = 'FamilyInstance'
        MaxResults = 20
        IncludeParameters = $true
    } -TargetDocument $doc.DocumentKey)

    $createdMap = @{}
    foreach ($item in @($createdQuery.Items)) {
        $paramMap = @{}
        foreach ($param in @($item.Parameters)) {
            $paramMap[[string]$param.Name] = [string]$param.Value
        }

        $createdMap[[int]$item.ElementId] = [pscustomobject]@{
            ElementId = [int]$item.ElementId
            FamilyName = [string]$item.FamilyName
            TypeName = [string]$item.TypeName
            LevelId = [int]$item.LevelId
            LevelName = [string]$item.LevelName
            LocationPoint = $item.LocationPoint
            IfcGuid = if ($paramMap.ContainsKey('IfcGUID')) { [string]$paramMap['IfcGUID'] } else { '' }
            Comments = if ($paramMap.ContainsKey('Comments')) { [string]$paramMap['Comments'] } else { '' }
            Mark = if ($paramMap.ContainsKey('Mark')) { [string]$paramMap['Mark'] } else { '' }
        }
    }

    foreach ($case in $caseResults) {
        if ($createdMap.ContainsKey([int]$case.CreatedElementId)) {
            $live = $createdMap[[int]$case.CreatedElementId]
            $case.IfcGuid = [string]$live.IfcGuid
            $case.Comment = [string]$live.Comments
            $case.Mark = [string]$live.Mark
        }
    }

    Write-JsonFile -Path (Join-Path $artifactDir 'created-elements.live-query.json') -Data $createdQuery
    Write-JsonFile -Path (Join-Path $artifactDir 'placement-cases.json') -Data $caseResults

    $exportPayload = [ordered]@{
        DocumentKey = [string]$doc.DocumentKey
        PresetName = 'coordination_ifc'
        OutputRootName = $OutputRootName
        RelativeOutputPath = ("{0}\{1}" -f $RelativeOutputPathBase, $runId)
        FileName = $FileName
        ViewId = $null
        ViewName = $ViewName
        OverwriteExisting = $true
    }

    $exportPreview = Invoke-MutationPreview -Tool 'export.ifc_safe' -Payload $exportPayload -TargetDocument $doc.DocumentKey
    $exportPreviewPayload = ConvertFrom-PayloadJson -Response $exportPreview
    Write-JsonFile -Path (Join-Path $artifactDir 'ifc-export-preview.response.json') -Data $exportPreview
    Write-JsonFile -Path (Join-Path $artifactDir 'ifc-export-preview.payload.json') -Data $exportPreviewPayload

    $exportExecute = $null
    if ($ExecuteExport.IsPresent) {
        $exportExecute = Invoke-MutationExecute `
            -Tool 'export.ifc_safe' `
            -Payload $exportPayload `
            -ApprovalToken $exportPreview.ApprovalToken `
            -PreviewRunId $exportPreview.PreviewRunId `
            -ExpectedContext $exportPreviewPayload.ResolvedContext `
            -TargetDocument $doc.DocumentKey
        Assert-BridgeSuccess -Response $exportExecute -Tool 'export.ifc_safe'
        Write-JsonFile -Path (Join-Path $artifactDir 'ifc-export-execute.response.json') -Data $exportExecute
    }

    $exportResponse = if ($exportExecute) { $exportExecute } else { $exportPreview }
    $outputPathArtifact = @(
        $exportResponse.Artifacts |
            Where-Object { $_ -like 'OutputPath=*' -or $_ -like 'Output=*' } |
            Select-Object -First 1
    )
    $ifcPath = ''
    if ($outputPathArtifact.Count -gt 0) {
        $ifcPath = ($outputPathArtifact[0] -replace '^OutputPath=', '' -replace '^Output=', '')
    }

    $ifcEvidence = @()
    if (-not [string]::IsNullOrWhiteSpace($ifcPath) -and (Test-Path $ifcPath)) {
        $entityMap = Get-IfcEntityMap -IfcPath $ifcPath
        foreach ($case in $caseResults) {
            $evidence = Resolve-IfcProductEvidence -EntityMap $entityMap -IfcGuid $case.IfcGuid
            if ($null -eq $evidence) {
                $ifcEvidence += [pscustomobject]@{
                    CaseKey = [string]$case.CaseKey
                    CreatedElementId = [int]$case.CreatedElementId
                    IfcGuid = [string]$case.IfcGuid
                    Found = $false
                }
                continue
            }

            $ifcEvidence += [pscustomobject]@{
                CaseKey = [string]$case.CaseKey
                CreatedElementId = [int]$case.CreatedElementId
                IfcGuid = [string]$case.IfcGuid
                Found = $true
                ProductId = [string]$evidence.ProductId
                ProductName = [string]$evidence.ProductName
                ProductTag = [string]$evidence.Tag
                ProductLine = [string]$evidence.ProductLine
                LocalPlacementLine = if ($evidence.LocalPlacement) { [string]$evidence.LocalPlacement.RawLine } else { '' }
                RelativePlacementLine = if ($evidence.LocalPlacement -and $evidence.LocalPlacement.RelativePlacement) { [string]$evidence.LocalPlacement.RelativePlacement.RawLine } else { '' }
                RelativePlacementOrigin = if ($evidence.LocalPlacement -and $evidence.LocalPlacement.RelativePlacement) { @($evidence.LocalPlacement.RelativePlacement.Origin) } else { @() }
                RelativePlacementAxis = if ($evidence.LocalPlacement -and $evidence.LocalPlacement.RelativePlacement) { @($evidence.LocalPlacement.RelativePlacement.Axis) } else { @() }
                RelativePlacementRefDirection = if ($evidence.LocalPlacement -and $evidence.LocalPlacement.RelativePlacement) { @($evidence.LocalPlacement.RelativePlacement.RefDirection) } else { @() }
                RepresentationLine = if ($evidence.Representation) { [string]$evidence.Representation.RepresentationLine } else { '' }
                MappedItemLine = if ($evidence.Representation) { [string]$evidence.Representation.MappedItemLine } else { '' }
                TransformLine = if ($evidence.Representation) { [string]$evidence.Representation.TransformLine } else { '' }
            }
        }
    }

    Write-JsonFile -Path (Join-Path $artifactDir 'ifc-localx-evidence.json') -Data $ifcEvidence

    $createdElementIdArray = [int[]]@($createdIds)
    $caseCount = $caseResults.Count
    $exportStatus = if ($exportExecute) { [string]$exportExecute.StatusCode } else { [string]$exportPreview.StatusCode }
    $exportSucceeded = if ($exportExecute) { [bool]$exportExecute.Succeeded } else { [bool]$exportPreview.Succeeded }

    $summary = @{
        GeneratedAtLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        ArtifactDirectory = $artifactDir
        DocumentTitle = [string]$doc.Title
        DocumentKey = [string]$doc.DocumentKey
        SeedSpikeArtifactDir = $seedSpikeDir
        SeedWrapperElementId = [int]$seedSample.NewWrapperElementId
        SeedTypeId = $seedTypeId
        SeedLevelId = $seedLevelId
        SeedTypeName = [string]$seedItem.TypeName
        CaseCount = $caseCount
        CreatedElementIds = $createdElementIdArray
        ExecuteExport = $ExecuteExport.IsPresent
        ExportStatus = $exportStatus
        ExportSucceeded = $exportSucceeded
        IfcPath = $ifcPath
        CleanupAfter = $CleanupAfter.IsPresent
    }

    Write-JsonFile -Path (Join-Path $artifactDir 'summary.json') -Data $summary
    $summary | ConvertTo-Json -Depth 100
}
finally {
    if ($CleanupAfter.IsPresent -and $createdIds.Count -gt 0) {
        try {
            $docForCleanup = ConvertFrom-PayloadJson -Response (Invoke-ReadTool -Tool 'document.get_active')
            $deletePayload = @{
                DocumentKey = [string]$docForCleanup.DocumentKey
                ElementIds = [int[]]@($createdIds.ToArray())
                AllowDependentDeletes = $false
            }

            $deletePreview = Invoke-MutationPreview -Tool 'element.delete_safe' -Payload $deletePayload -TargetDocument $docForCleanup.DocumentKey
            $deletePreviewPayload = ConvertFrom-PayloadJson -Response $deletePreview
            $deleteExecute = Invoke-MutationExecute `
                -Tool 'element.delete_safe' `
                -Payload $deletePayload `
                -ApprovalToken $deletePreview.ApprovalToken `
                -PreviewRunId $deletePreview.PreviewRunId `
                -ExpectedContext $deletePreviewPayload.ResolvedContext `
                -TargetDocument $docForCleanup.DocumentKey
            Write-JsonFile -Path (Join-Path $artifactDir 'cleanup-delete.response.json') -Data $deleteExecute
        }
        catch {
            $cleanupError = [pscustomobject]@{
                Message = $_.Exception.Message
                ScriptStackTrace = $_.ScriptStackTrace
            }
            Write-JsonFile -Path (Join-Path $artifactDir 'cleanup-delete.error.json') -Data $cleanupError
        }
    }
}
