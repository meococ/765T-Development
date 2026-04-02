Set-StrictMode -Version Latest

function Resolve-ProjectRoot {
    param(
        [string]$StartPath = ""
    )

    if ([string]::IsNullOrWhiteSpace($StartPath)) {
        $scriptParent = $null
        if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
            $scriptParent = Split-Path $PSScriptRoot -Parent
        }

        if ($scriptParent -and (Test-Path $scriptParent)) {
            $StartPath = $scriptParent
        }
        else {
            $StartPath = (Get-Location).Path
        }
    }

    $currentPath = (Resolve-Path $StartPath).Path
    $fallback = $null
    while ($currentPath) {
        $candidateSln = Join-Path $currentPath 'BIM765T.Revit.Agent.sln'
        $candidateSrc = Join-Path $currentPath 'src\BIM765T.Revit.Agent'
        $candidateTools = Join-Path $currentPath 'tools'
        $candidateReadme = Join-Path $currentPath 'README.md'

        if (Test-Path $candidateSln) {
            return $currentPath
        }
        if ((Test-Path $candidateSrc) -and (Test-Path $candidateTools) -and (Test-Path $candidateReadme)) {
            $fallback = $currentPath
        }

        $parent = Split-Path $currentPath -Parent
        if (-not $parent -or $parent -eq $currentPath) {
            break
        }
        $currentPath = $parent
    }

    if ($fallback) {
        return $fallback
    }

    throw "Khong resolve duoc project root tu: $StartPath"
}

function Test-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Get-PackageBuildParents {
    $parents = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($env:BIM765T_BUILD_PARENT)) {
        $parents.Add($env:BIM765T_BUILD_PARENT) | Out-Null
    }

    if (Test-Path 'D:\') {
        $parents.Add('D:\') | Out-Null
    }

    $localFallback = Join-Path $env:LOCALAPPDATA 'BIM765Tbuilds'
    $parents.Add($localFallback) | Out-Null

    return @($parents | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Get-BuildVersionNumber {
    param(
        [string]$Name
    )

    if ($Name -match '^BIM765Tbuild_v(?<version>\d+)$') {
        return [int]$matches.version
    }

    return -1
}

function Get-PackagedBuildDirectories {
    param(
        [string[]]$Parents = @(Get-PackageBuildParents)
    )

    $results = New-Object System.Collections.Generic.List[object]
    foreach ($parent in $Parents) {
        if (-not (Test-Path $parent)) {
            continue
        }

        Get-ChildItem $parent -Directory -Filter 'BIM765Tbuild_v*' -ErrorAction SilentlyContinue |
            ForEach-Object {
                $version = Get-BuildVersionNumber -Name $_.Name
                if ($version -ge 0) {
                    $results.Add([pscustomobject]@{
                        Directory = $_
                        Version = $version
                        Parent = $parent
                    }) | Out-Null
                }
            }
    }

    return @(
        $results |
            Sort-Object `
                @{ Expression = { $_.Version }; Descending = $true }, `
                @{ Expression = { $_.Directory.LastWriteTimeUtc }; Descending = $true }, `
                @{ Expression = { $_.Directory.FullName }; Descending = $true }
    )
}

function Resolve-LatestPackagedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    foreach ($entry in (Get-PackagedBuildDirectories)) {
        $candidate = Join-Path $entry.Directory.FullName $RelativePath
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $null
}

function Resolve-PackagedBuildRoot {
    param(
        [string]$RequestedPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath) -and (Test-Path $RequestedPath)) {
        return (Resolve-Path $RequestedPath).Path
    }

    $latest = Get-PackagedBuildDirectories | Select-Object -First 1
    if ($latest -and (Test-Path $latest.Directory.FullName)) {
        return (Resolve-Path $latest.Directory.FullName).Path
    }

    throw "Khong resolve duoc packaged build root."
}

function Resolve-DefaultPackageTargetRoot {
    $parents = @(Get-PackageBuildParents)
    $preferredParent = $parents | Where-Object { $_ -eq 'D:\' -and (Test-Path $_) } | Select-Object -First 1
    if (-not $preferredParent) {
        $preferredParent = $parents | Select-Object -Last 1
    }

    if (-not (Test-Path $preferredParent)) {
        New-Item -ItemType Directory -Force -Path $preferredParent | Out-Null
    }

    $existing = @(Get-PackagedBuildDirectories -Parents @($preferredParent))
    $nextVersion = if ($existing.Count -gt 0) {
        (@($existing | ForEach-Object { $_.Version }) | Measure-Object -Maximum).Maximum + 1
    }
    else {
        1
    }

    return (Join-Path $preferredParent ("BIM765Tbuild_v{0}" -f $nextVersion))
}

function Resolve-BridgeExe {
    param(
        [string]$RequestedPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath) -and (Test-Path $RequestedPath)) {
        return (Resolve-Path $RequestedPath).Path
    }

    $projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
    $candidates = @(
        $(Join-Path $projectRoot 'src\BIM765T.Revit.Bridge\bin\Release\net8.0\BIM765T.Revit.Bridge.exe')
        $(Resolve-LatestPackagedPath -RelativePath 'Bridge\BIM765T.Revit.Bridge.exe')
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Khong resolve duoc BIM765T.Revit.Bridge.exe"
}

function Resolve-McpExe {
    param(
        [string]$RequestedPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath) -and (Test-Path $RequestedPath)) {
        return (Resolve-Path $RequestedPath).Path
    }

    $projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
    $candidates = @(
        $(Join-Path $projectRoot 'src\BIM765T.Revit.McpHost\bin\Release\net8.0\BIM765T.Revit.McpHost.exe')
        $(Resolve-LatestPackagedPath -RelativePath 'McpHost\BIM765T.Revit.McpHost.exe')
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Khong resolve duoc BIM765T.Revit.McpHost.exe"
}

function Resolve-WorkerHostExe {
    param(
        [string]$RequestedPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath) -and (Test-Path $RequestedPath)) {
        return (Resolve-Path $RequestedPath).Path
    }

    $projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
    $candidates = @(
        $(Join-Path $projectRoot 'src\BIM765T.Revit.WorkerHost\bin\Release\net8.0\BIM765T.Revit.WorkerHost.exe')
        $(Resolve-LatestPackagedPath -RelativePath 'WorkerHost\BIM765T.Revit.WorkerHost.exe')
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Khong resolve duoc BIM765T.Revit.WorkerHost.exe"
}

function Resolve-ExternalAiCliExe {
    $command = Get-Command claude -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    $candidates = @(
        $env:BIM765T_EXTERNAL_AI_CLI_EXE,
        $env:CLAUDE_CODE_EXE,
        (Join-Path $env:USERPROFILE '.dotnet\tools\claude.cmd'),
        (Join-Path $env:APPDATA 'npm\claude.cmd')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Khong resolve duoc external AI CLI executable."
}

function Resolve-ClaudeExe {
    return Resolve-ExternalAiCliExe
}

function Ensure-ExternalAiCliEnvironment {
    $nodeCandidates = @(
        $env:BIM765T_EXTERNAL_AI_CLI_NODE_PATH,
        $env:CLAUDE_CODE_NODE_PATH,
        'C:\Program Files\nodejs',
        (Join-Path $env:LOCALAPPDATA 'Programs\nodejs')
    )
    $gitCmdCandidates = @(
        $env:BIM765T_EXTERNAL_AI_CLI_GIT_CMD_PATH,
        $env:CLAUDE_CODE_GIT_CMD_PATH,
        (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd'),
        'C:\Program Files\Git\cmd'
    )
    $gitBashCandidates = @(
        $env:BIM765T_EXTERNAL_AI_CLI_GIT_BASH_PATH,
        $env:CLAUDE_CODE_GIT_BASH_PATH,
        (Join-Path $env:LOCALAPPDATA 'Programs\Git\bin\bash.exe'),
        'C:\Program Files\Git\bin\bash.exe'
    )
    $caCandidates = @(
        $env:BIM765T_EXTERNAL_AI_CLI_CA_BUNDLE,
        $env:CLAUDE_CODE_CA_BUNDLE,
        $env:NODE_EXTRA_CA_CERTS,
        (Join-Path $env:USERPROFILE '.assistant\cisco-umbrella-root-ca.pem')
    )

    $nodePath = $nodeCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } | Select-Object -First 1
    $gitCmdPath = $gitCmdCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } | Select-Object -First 1
    $gitBashPath = $gitBashCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } | Select-Object -First 1
    $caPath = $caCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } | Select-Object -First 1

    $prefixes = @()
    if (Test-Path $nodePath) { $prefixes += $nodePath }
    if (Test-Path $gitCmdPath) { $prefixes += $gitCmdPath }
    if ($prefixes.Count -gt 0) {
        $env:Path = ($prefixes -join ';') + ';' + $env:Path
    }

    if (Test-Path $gitBashPath) {
        $env:BIM765T_EXTERNAL_AI_CLI_GIT_BASH_PATH = $gitBashPath
        $env:CLAUDE_CODE_GIT_BASH_PATH = $gitBashPath
    }

    if (Test-Path $caPath) {
        $env:NODE_EXTRA_CA_CERTS = $caPath
    }
}

function Ensure-ClaudeEnvironment {
    Ensure-ExternalAiCliEnvironment
}

function New-RunDirectory {
    param(
        [string]$ProjectRoot,
        [string]$TaskSlug = 'mode-c'
    )

    $safeSlug = ($TaskSlug -replace '[^A-Za-z0-9\-_]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safeSlug)) {
        $safeSlug = 'run'
    }

    $runsRoot = Join-Path $ProjectRoot 'artifacts\tool-runs'
    New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
    $dirName = "{0}_{1}" -f ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')), $safeSlug.ToLowerInvariant()
    $runDir = Join-Path $runsRoot $dirName
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    return $runDir
}

function Get-RunDirectoryTimestamp {
    param(
        [string]$Name
    )

    if ($Name -match '^(?<stamp>\d{8}-\d{6})_') {
        try {
            return [DateTime]::ParseExact(
                $matches.stamp,
                'yyyyMMdd-HHmmss',
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal
            )
        }
        catch {
        }
    }

    return [DateTime]::MinValue
}

function Get-RunDirectories {
    param(
        [string]$ProjectRoot,
        [string]$NamePrefix = '',
        [string[]]$RequiredFiles = @()
    )

    $runsRoot = Join-Path $ProjectRoot 'artifacts\tool-runs'
    if (-not (Test-Path $runsRoot)) {
        return @()
    }

    $prefixLower = if ([string]::IsNullOrWhiteSpace($NamePrefix)) { '' } else { $NamePrefix.ToLowerInvariant() }
    $directories = @(Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue)

    if ($prefixLower) {
        $directories = @(
            $directories | Where-Object {
                $name = $_.Name.ToLowerInvariant()
                $name -like "*_${prefixLower}*" -or $name -eq $prefixLower
            }
        )
    }

    if ($RequiredFiles.Count -gt 0) {
        $directories = @(
            $directories | Where-Object {
                $dir = $_
                foreach ($required in $RequiredFiles) {
                    if (-not (Test-Path (Join-Path $dir.FullName $required))) {
                        return $false
                    }
                }

                return $true
            }
        )
    }

    return @(
        $directories |
            Sort-Object `
                @{ Expression = { Get-RunDirectoryTimestamp -Name $_.Name }; Descending = $true }, `
                @{ Expression = { $_.LastWriteTimeUtc }; Descending = $true }, `
                @{ Expression = { $_.Name }; Descending = $true }
    )
}

function Resolve-RunDirectory {
    param(
        [string]$ProjectRoot,
        [string]$RequestedPath = '',
        [string]$NamePrefix = '',
        [string[]]$RequiredFiles = @()
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path $RequestedPath)) {
            throw "Run directory not found: $RequestedPath"
        }

        $resolved = (Resolve-Path $RequestedPath).Path
        foreach ($required in $RequiredFiles) {
            if (-not (Test-Path (Join-Path $resolved $required))) {
                throw "Run directory thieu file bat buoc '$required': $resolved"
            }
        }

        return $resolved
    }

    $matches = @(Get-RunDirectories -ProjectRoot $ProjectRoot -NamePrefix $NamePrefix -RequiredFiles $RequiredFiles)
    if ($matches.Count -eq 0) {
        $suffix = if ([string]::IsNullOrWhiteSpace($NamePrefix)) { '' } else { " voi prefix '$NamePrefix'" }
        throw "Khong tim thay run nao$suffix trong $(Join-Path $ProjectRoot 'artifacts\tool-runs')"
    }

    return $matches[0].FullName
}

function Write-JsonFile {
    param(
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Data
    )

    $parent = Split-Path $Path -Parent
    if ($parent) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $Data | ConvertTo-Json -Depth 50 | Set-Content -Path $Path -Encoding UTF8
}

function Write-TextFileAtomically {
    param(
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $parent = Split-Path $Path -Parent
    if ($parent) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $tempName = "{0}.{1}.tmp" -f (Split-Path $Path -Leaf), ([Guid]::NewGuid().ToString('N'))
    $tempPath = if ($parent) { Join-Path $parent $tempName } else { $tempName }

    try {
        Set-Content -Path $tempPath -Value $Content -Encoding UTF8
        Move-Item -Path $tempPath -Destination $Path -Force
    }
    finally {
        if (Test-Path $tempPath) {
            Remove-Item -Path $tempPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-JsonFileAtomically {
    param(
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Data
    )

    $json = $Data | ConvertTo-Json -Depth 50
    Write-TextFileAtomically -Path $Path -Content $json
}

function Invoke-BridgeJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BridgeExe,
        [Parameter(Mandatory = $true)]
        [string]$Tool,
        [string]$TargetDocument = "",
        [string]$PayloadJson = "",
        [string]$PayloadFile = "",
        [string]$ExpectedContextFile = "",
        [string]$ScopeFile = "",
        [switch]$DryRun
    )

    $tempPayloadFile = $null
    try {
        $args = @($Tool)

        if ($DryRun) { $args += @('--dry-run', 'true') }
        if ($TargetDocument) { $args += @('--target-document', $TargetDocument) }

        if ($PayloadJson) {
            $tempPayloadFile = Join-Path $env:TEMP ("bim765t_bridge_payload_{0}.json" -f ([Guid]::NewGuid().ToString('N')))
            Set-Content -Path $tempPayloadFile -Value $PayloadJson -Encoding UTF8
            $PayloadFile = $tempPayloadFile
        }

        if ($PayloadFile) { $args += @('--payload', $PayloadFile) }
        if ($ExpectedContextFile) { $args += @('--expected-context', $ExpectedContextFile) }
        if ($ScopeFile) { $args += @('--scope', $ScopeFile) }

        $raw = & $BridgeExe @args
        if (-not $raw) {
            throw "Bridge tra ve rong cho tool $Tool"
        }

        return $raw | ConvertFrom-Json
    }
    finally {
        if ($tempPayloadFile -and (Test-Path $tempPayloadFile)) {
            Remove-Item $tempPayloadFile -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-ExternalAiCliPrint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,
        [Parameter(Mandatory = $true)]
        [string]$PromptText,
        [string]$Model = 'sonnet',
        [string]$OutputFormat = 'text',
        [string]$JsonSchemaPath = '',
        [string]$AppendSystemPrompt = '',
        [switch]$DisableTools,
        [string]$WorkingDirectory = (Get-Location).Path
    )

    Ensure-ExternalAiCliEnvironment

    $modelArg = switch ($Model.ToLowerInvariant()) {
        'haiku' { 'haiku' }
        'opus' { 'opus' }
        default { 'sonnet' }
    }

    $args = @('-p', '--model', $modelArg, '--output-format', $OutputFormat)
    $permissionMode = if (-not [string]::IsNullOrWhiteSpace($env:BIM765T_EXTERNAL_AI_CLI_PERMISSION_MODE)) {
        $env:BIM765T_EXTERNAL_AI_CLI_PERMISSION_MODE
    }
    else {
        $env:CLAUDE_CODE_PERMISSION_MODE
    }

    if (-not [string]::IsNullOrWhiteSpace($permissionMode)) {
        $args += @('--permission-mode', $permissionMode)
    }

    if ($JsonSchemaPath) {
        $schema = Get-Content $JsonSchemaPath -Raw
        $args += @('--json-schema', $schema)
    }

    if ($AppendSystemPrompt) {
        $args += @('--append-system-prompt', $AppendSystemPrompt)
    }

    Push-Location $WorkingDirectory
    try {
        return ($PromptText | & $Executable @args)
    }
    finally {
        Pop-Location
    }
}

function Invoke-ClaudePrint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ClaudeExe,
        [Parameter(Mandatory = $true)]
        [string]$PromptText,
        [string]$Model = 'sonnet',
        [string]$OutputFormat = 'text',
        [string]$JsonSchemaPath = '',
        [string]$AppendSystemPrompt = '',
        [switch]$DisableTools,
        [string]$WorkingDirectory = (Get-Location).Path
    )

    return Invoke-ExternalAiCliPrint `
        -Executable $ClaudeExe `
        -PromptText $PromptText `
        -Model $Model `
        -OutputFormat $OutputFormat `
        -JsonSchemaPath $JsonSchemaPath `
        -AppendSystemPrompt $AppendSystemPrompt `
        -DisableTools:$DisableTools `
        -WorkingDirectory $WorkingDirectory
}

function ConvertFrom-ExternalAiJsonEnvelopeResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RawJson
    )

    $envelope = $RawJson | ConvertFrom-Json
    $resultText = [string]$envelope.result

    if ([string]::IsNullOrWhiteSpace($resultText)) {
        return [pscustomobject]@{
            Envelope = $envelope
            Parsed = $null
            ResultText = $resultText
        }
    }

    $clean = $resultText.Trim()
    if ($clean.StartsWith('```json')) {
        $clean = $clean.Substring(7).Trim()
    }
    if ($clean.StartsWith('```')) {
        $clean = $clean.Substring(3).Trim()
    }
    if ($clean.EndsWith('```')) {
        $clean = $clean.Substring(0, $clean.Length - 3).Trim()
    }

    $parsed = $null
    try {
        $parsed = $clean | ConvertFrom-Json -Depth 50
    }
    catch {
        $parsed = $null
    }

    return [pscustomobject]@{
        Envelope = $envelope
        Parsed = $parsed
        ResultText = $clean
    }
}

function ConvertFrom-ClaudeJsonEnvelopeResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RawJson
    )

    return ConvertFrom-ExternalAiJsonEnvelopeResult -RawJson $RawJson
}

function ConvertFrom-ModeCTextFallback {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $normalized = $Text -replace "`r", ''
    $lines = $normalized -split "`n"

    function Get-Section {
        param(
            [string[]]$AllLines,
            [string]$Heading
        )

        $start = -1
        for ($i = 0; $i -lt $AllLines.Count; $i++) {
            if ($AllLines[$i].Trim() -eq $Heading) {
                $start = $i + 1
                break
            }
        }
        if ($start -lt 0) { return @() }

        $result = New-Object System.Collections.Generic.List[string]
        for ($j = $start; $j -lt $AllLines.Count; $j++) {
            $line = $AllLines[$j]
            if ($line.Trim().StartsWith('## ')) { break }
            $result.Add($line) | Out-Null
        }
        return $result.ToArray()
    }

    $summaryLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line.Trim().StartsWith('## ')) { break }
        if (-not [string]::IsNullOrWhiteSpace($line) -and $line.Trim() -ne '---') {
            $summaryLines.Add($line.Trim()) | Out-Null
        }
    }

    $contextLines = @(Get-Section -AllLines $lines -Heading '## Revit Context')
    $riskLines = @(Get-Section -AllLines $lines -Heading '## Risks')
    $nextLines = @(Get-Section -AllLines $lines -Heading '## Suggested Next Actions')

    $risks = @($riskLines | Where-Object { $_.Trim().StartsWith('- ') -or $_.Trim().StartsWith('⚠️') -or $_.Trim().StartsWith('ℹ️') } | ForEach-Object { $_.Trim(' ','-') })
    $nextActions = @($nextLines | Where-Object { $_.Trim() -match '^\d+\.' } | ForEach-Object { $_.Trim() })

    $toolPlan = @()
    $step = 1
    foreach ($action in $nextActions) {
        $toolName = ''
        if ($action -match '`([^`]+)`') {
            $toolName = $matches[1]
        }
        $toolPlan += [pscustomobject]@{
            step = $step
            tool = if ($toolName) { $toolName } else { 'manual-review' }
            purpose = $action
            risk = 'requires validation against live Revit context'
        }
        $step++
    }

    $findings = @($contextLines | Where-Object { $_.Trim().StartsWith('- **') -or $_.Trim().StartsWith('- ') } | ForEach-Object { $_.Trim(' ','-') })
    if ($findings.Count -eq 0 -and @($contextLines).Count -gt 0) {
        $findings = @(($contextLines -join ' ').Trim())
    }

    return [pscustomobject]@{
        summary = if ($summaryLines.Count -gt 0) { ($summaryLines -join ' ') } else { 'Mode C fallback parsed from external AI text response.' }
        contextAssessment = ($contextLines -join "`n").Trim()
        findings = $findings
        toolPlan = $toolPlan
        fileTargets = @()
        risks = $risks
        nextActions = $nextActions
    }
}
