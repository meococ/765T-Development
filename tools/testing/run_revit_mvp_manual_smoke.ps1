param(
    [string]$BridgeExe = "",
    [string]$WorkerHostExe = "",
    [string]$TargetDocument = "",
    [string]$ArtifactDir = "",
    [switch]$SkipWorkerHostHealth,
    [switch]$SkipBridgeHealth,
    [switch]$OpenArtifactDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot -StartPath (Split-Path $PSScriptRoot -Parent)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Join-Path $projectRoot ("artifacts/revit-mvp-manual-smoke/{0}" -f $stamp)
}

New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null

function Try-ResolveBridgeExe {
    param([string]$RequestedPath)

    try {
        return Resolve-BridgeExe -RequestedPath $RequestedPath
    }
    catch {
        return $null
    }
}

function Try-ResolveWorkerHostExe {
    param([string]$RequestedPath)

    try {
        return Resolve-WorkerHostExe -RequestedPath $RequestedPath
    }
    catch {
        return $null
    }
}

function Read-OptionalJsonFile {
    param([string]$Path)

    $result = [ordered]@{
        Path = $Path
        Exists = Test-Path $Path
        Error = ""
        Data = $null
    }

    if (-not $result.Exists) {
        return [pscustomobject]$result
    }

    try {
        $raw = Get-Content -Path $Path -Raw
        $result.Data = if ([string]::IsNullOrWhiteSpace($raw)) { $null } else { $raw | ConvertFrom-Json }
    }
    catch {
        $result.Error = $_.Exception.Message
    }

    return [pscustomobject]$result
}

function Invoke-JsonScript {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [hashtable]$NamedArguments = @{}
    )

    $result = [ordered]@{
        Script = $ScriptPath
        Arguments = @(
            foreach ($key in @($NamedArguments.Keys | Sort-Object)) {
                "{0}={1}" -f $key, $NamedArguments[$key]
            }
        )
        Succeeded = $false
        Error = ""
        RawJson = ""
        Data = $null
    }

    try {
        $rawLines = @(& $ScriptPath @NamedArguments)
        $rawText = ($rawLines | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($rawText)) {
            throw "Script returned empty output."
        }

        $result.RawJson = $rawText
        $result.Data = $rawText | ConvertFrom-Json
        $result.Succeeded = $true
    }
    catch {
        $result.Error = $_.Exception.Message
    }

    return [pscustomobject]$result
}

function Get-OptionalMember {
    param(
        [object]$Object,
        [string]$Name,
        [object]$DefaultValue = $null
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }

    if ($Object.PSObject.Properties[$Name]) {
        return $Object.$Name
    }

    return $DefaultValue
}

function Get-StatusLabel {
    param(
        [bool]$Skipped,
        [bool]$Succeeded,
        [string]$Error
    )

    if ($Skipped) {
        return 'Skipped'
    }

    if ($Succeeded) {
        return 'Ready'
    }

    if ([string]::IsNullOrWhiteSpace($Error)) {
        return 'Unavailable'
    }

    return 'Blocked'
}

$appDataRoot = Join-Path $env:APPDATA 'BIM765T.Revit.Agent'
$paths = [ordered]@{
    ProjectRoot = $projectRoot
    ArtifactDir = $ArtifactDir
    AppDataRoot = $appDataRoot
    WorkspaceRoot = Join-Path $appDataRoot 'workspaces'
    WorkerHostStateRoot = Join-Path $appDataRoot 'workerhost'
    DurableStateRoot = Join-Path $appDataRoot 'state'
    LogsRoot = Join-Path $appDataRoot 'logs'
    SettingsPath = Join-Path $appDataRoot 'settings.json'
    PolicyPath = Join-Path $appDataRoot 'policy.json'
    RepoWorkspaceSeed = Join-Path $projectRoot 'workspaces\default\workspace.json'
    CanonicalDoc = Join-Path $projectRoot 'docs\release\mvp-manual-smoke.md'
}

$resolvedBridgeExe = Try-ResolveBridgeExe -RequestedPath $BridgeExe
$resolvedWorkerHostExe = Try-ResolveWorkerHostExe -RequestedPath $WorkerHostExe
$settingsSnapshot = Read-OptionalJsonFile -Path $paths.SettingsPath
$policySnapshot = Read-OptionalJsonFile -Path $paths.PolicyPath

$workerHealth = if ($SkipWorkerHostHealth) {
    [pscustomobject]@{
        Script = Join-Path $PSScriptRoot 'check_workerhost_health.ps1'
        Arguments = @()
        Succeeded = $false
        Error = ""
        RawJson = ""
        Data = $null
        Skipped = $true
    }
}
else {
    $args = @{ AsJson = $true }
    if (-not [string]::IsNullOrWhiteSpace($resolvedWorkerHostExe)) {
        $args.WorkerHostExe = $resolvedWorkerHostExe
    }

    $result = Invoke-JsonScript -ScriptPath (Join-Path $PSScriptRoot 'check_workerhost_health.ps1') -NamedArguments $args
    Add-Member -InputObject $result -NotePropertyName Skipped -NotePropertyValue $false
    $result
}

$bridgeHealth = if ($SkipBridgeHealth) {
    [pscustomobject]@{
        Script = Join-Path $PSScriptRoot 'check_bridge_health.ps1'
        Arguments = @()
        Succeeded = $false
        Error = ""
        RawJson = ""
        Data = $null
        Skipped = $true
    }
}
else {
    $args = @{
        Profile = 'copilot'
        AsJson = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($resolvedBridgeExe)) {
        $args.BridgeExe = $resolvedBridgeExe
    }

    $result = Invoke-JsonScript -ScriptPath (Join-Path $PSScriptRoot 'check_bridge_health.ps1') -NamedArguments $args
    Add-Member -InputObject $result -NotePropertyName Skipped -NotePropertyValue $false
    $result
}

$summary = [ordered]@{
    GeneratedAtUtc = [DateTime]::UtcNow.ToString('o')
    TargetDocument = $TargetDocument
    Paths = $paths
    ResolvedExecutables = [ordered]@{
        BridgeExe = if ([string]::IsNullOrWhiteSpace($resolvedBridgeExe)) { "" } else { $resolvedBridgeExe }
        WorkerHostExe = if ([string]::IsNullOrWhiteSpace($resolvedWorkerHostExe)) { "" } else { $resolvedWorkerHostExe }
    }
    LocalSettings = [ordered]@{
        SettingsFileExists = $settingsSnapshot.Exists
        SettingsFileError = $settingsSnapshot.Error
        AllowWriteTools = [bool](Get-OptionalMember -Object $settingsSnapshot.Data -Name 'AllowWriteTools' -DefaultValue $false)
        AllowSaveTools = [bool](Get-OptionalMember -Object $settingsSnapshot.Data -Name 'AllowSaveTools' -DefaultValue $false)
        AllowSyncTools = [bool](Get-OptionalMember -Object $settingsSnapshot.Data -Name 'AllowSyncTools' -DefaultValue $false)
        PolicyFileExists = $policySnapshot.Exists
        PolicyFileError = $policySnapshot.Error
    }
    Preflight = [ordered]@{
        WorkerHost = [ordered]@{
            Status = Get-StatusLabel -Skipped ([bool]$workerHealth.Skipped) -Succeeded ([bool]$workerHealth.Succeeded) -Error ([string]$workerHealth.Error)
            Ready = [bool](Get-OptionalMember -Object $workerHealth.Data -Name 'Ready' -DefaultValue $false)
            Degraded = [bool](Get-OptionalMember -Object $workerHealth.Data -Name 'Degraded' -DefaultValue $false)
            QdrantReachable = [bool](Get-OptionalMember -Object $workerHealth.Data -Name 'QdrantReachable' -DefaultValue $false)
            Error = [string]$workerHealth.Error
        }
        Bridge = [ordered]@{
            Status = Get-StatusLabel -Skipped ([bool]$bridgeHealth.Skipped) -Succeeded ([bool]$bridgeHealth.Succeeded) -Error ([string]$bridgeHealth.Error)
            BridgeOnline = [bool](Get-OptionalMember -Object $bridgeHealth.Data -Name 'BridgeOnline' -DefaultValue $false)
            WriteEnabled = [bool](Get-OptionalMember -Object $bridgeHealth.Data -Name 'WriteEnabled' -DefaultValue $false)
            ActiveDocument = [string](Get-OptionalMember -Object $bridgeHealth.Data -Name 'ActiveDocument' -DefaultValue '')
            ActiveView = [string](Get-OptionalMember -Object $bridgeHealth.Data -Name 'ActiveView' -DefaultValue '')
            MissingRequiredTools = @((Get-OptionalMember -Object $bridgeHealth.Data -Name 'MissingRequiredTools' -DefaultValue @()))
            Error = [string]$bridgeHealth.Error
        }
    }
    NextActions = @()
}

$nextActions = New-Object System.Collections.Generic.List[string]
if (-not $settingsSnapshot.Exists) {
    $nextActions.Add("Run install-addin first so %APPDATA%\\BIM765T.Revit.Agent\\settings.json is created.") | Out-Null
}

if ($settingsSnapshot.Exists -and -not [bool](Get-OptionalMember -Object $settingsSnapshot.Data -Name 'AllowWriteTools' -DefaultValue $false)) {
    $nextActions.Add("Flip AllowWriteTools=true in %APPDATA%\\BIM765T.Revit.Agent\\settings.json before mutation smoke.") | Out-Null
}

if (-not [bool]$workerHealth.Succeeded) {
    $nextActions.Add("Start or build WorkerHost, then rerun the helper to refresh preflight.workerhost.json.") | Out-Null
}

if (-not [bool]$bridgeHealth.Succeeded -or -not [bool](Get-OptionalMember -Object $bridgeHealth.Data -Name 'BridgeOnline' -DefaultValue $false)) {
    $nextActions.Add("Open Revit 2024, load the add-in, open a disposable model, then rerun the helper or continue with the manual checklist.") | Out-Null
}

$summary.NextActions = @($nextActions)

$contextPath = Join-Path $ArtifactDir 'context.json'
$summaryPath = Join-Path $ArtifactDir 'summary.json'
$workerHealthPath = Join-Path $ArtifactDir 'preflight.workerhost.json'
$bridgeHealthPath = Join-Path $ArtifactDir 'preflight.bridge.json'
$checklistPath = Join-Path $ArtifactDir 'checklist.md'
$observationsPath = Join-Path $ArtifactDir 'observations.md'

Write-JsonFileAtomically -Path $contextPath -Data ([ordered]@{
    GeneratedAtUtc = $summary.GeneratedAtUtc
    TargetDocument = $TargetDocument
    Paths = $paths
    SettingsSnapshot = $settingsSnapshot
    PolicySnapshot = $policySnapshot
})
Write-JsonFileAtomically -Path $summaryPath -Data $summary
Write-JsonFileAtomically -Path $workerHealthPath -Data $workerHealth
Write-JsonFileAtomically -Path $bridgeHealthPath -Data $bridgeHealth

$workerStatus = [string]$summary.Preflight.WorkerHost.Status
$bridgeStatus = [string]$summary.Preflight.Bridge.Status
$writeEnabled = [string]$summary.Preflight.Bridge.WriteEnabled
$activeDocument = [string]$summary.Preflight.Bridge.ActiveDocument
$activeView = [string]$summary.Preflight.Bridge.ActiveView
$targetDocumentLine = if ([string]::IsNullOrWhiteSpace($TargetDocument)) { "<set when testing>" } else { $TargetDocument }

$checklistContent = @"
# Revit MVP Manual Smoke Bundle

- Generated: $($summary.GeneratedAtUtc)
- Artifact dir: $ArtifactDir
- Canonical doc: $($paths.CanonicalDoc)
- Target document: $targetDocumentLine

## Canonical runtime paths

- AppData root: $($paths.AppDataRoot)
- Project workspace root: $($paths.WorkspaceRoot)
- WorkerHost state root: $($paths.WorkerHostStateRoot)
- Durable state root: $($paths.DurableStateRoot)
- Logs root: $($paths.LogsRoot)
- Repo workspace seed: $($paths.RepoWorkspaceSeed)

## Preflight snapshot

- WorkerHost: $workerStatus
- Bridge: $bridgeStatus
- Write tools enabled in settings/bridge: $writeEnabled
- Active document: $(if ([string]::IsNullOrWhiteSpace($activeDocument)) { '<none>' } else { $activeDocument })
- Active view: $(if ([string]::IsNullOrWhiteSpace($activeView)) { '<none>' } else { $activeView })

Use observations.md trong cung artifact dir de ghi pass/fail, screenshot path, va bug notes.

## Scenario 1 - First-open onboarding

- [ ] Dung model test/disposable.
- [ ] Neu rerun, xoa hoac archive workspace tuong ung duoi $($paths.WorkspaceRoot).
- [ ] Mo Revit 2024 + model test.
- [ ] Mo 765T pane.

Expected:

- onboarding/welcome card hien thay vi chat rong
- workspace badge khong hien default
- deep-scan badge bat dau bang state onboarding that
- neu gui prompt dau tien, stage dau tien thay trong 765T Flow la Thinking

## Scenario 2 - Init + deep scan

- [ ] Chay onboarding init trong UI.
- [ ] Chay deep scan.
- [ ] Xac nhan workspace duoc tao trong machine-local root.

Expected:

- workspace duoc tao duoi $($paths.WorkspaceRoot)
- co workspace.json, project.context.json, reports/project-init.*, reports/project-brain.deep-scan.*, memory/project-brief.md
- deep-scan badge cap nhat theo state that, khong hardcode

## Scenario 3 - Resume session

- [ ] Dong Revit hoan toan sau khi da co workspace + session.
- [ ] Mo lai cung model.
- [ ] Mo lai 765T pane.

Expected:

- UI resume card hien thay vi first-open onboarding
- workspace/session badge map dung workspace vua tao
- pending approval/session context (neu co) duoc restore

## Scenario 4 - Flow streaming

- [ ] Gui mot read-only prompt, vi du review model health.
- [ ] Gui mot mutation prompt an toan, vi du duplicate view hoac create sheet tren model test.

Expected:

- read-only flow bat dau bang Thinking
- mutation flow di qua Preview -> Approval truoc khi Run
- sheet.place_views_safe hoi them context neu input chua du

## Scenario 5 - Mutation safety

- [ ] Verify sheet.create_safe
- [ ] Verify sheet.renumber_safe
- [ ] Verify view.duplicate_safe
- [ ] Verify sheet.place_views_safe

Expected:

- sheet.create_safe, sheet.renumber_safe, view.duplicate_safe co preview/dry-run, khong auto-execute
- reject/cancel khong de lai mutation
- approve moi execute, sau do Verify phan anh ket qua that
- sheet.place_views_safe khong tu chay mu khi thieu context
"@

$observationsContent = @"
# Revit MVP Manual Smoke Observations

- Tester:
- Date:
- Revit version: 2024
- Model:
- Artifact dir: $ArtifactDir

## Scenario results

- [ ] First-open onboarding
- [ ] Init + deep scan
- [ ] Resume session
- [ ] Flow streaming
- [ ] Mutation safety

## Screenshots / artifacts

- onboarding:
- resume:
- flow-thinking:
- preview-approval:

## Notes

- 

## Bugs / follow-up

- 
"@

Write-TextFileAtomically -Path $checklistPath -Content $checklistContent
Write-TextFileAtomically -Path $observationsPath -Content $observationsContent

if ($OpenArtifactDir) {
    Start-Process explorer.exe $ArtifactDir | Out-Null
}

[pscustomobject]$summary | ConvertTo-Json -Depth 20
