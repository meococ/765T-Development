param(
    [switch]$AsJson,
    [string]$RepoRoot = ""
)
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$workspaceRoot = Split-Path -Parent $RepoRoot
$localAssistantConfig = if (-not [string]::IsNullOrWhiteSpace($env:CODEX_CONFIG)) {
    $env:CODEX_CONFIG
}
elseif (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
    Join-Path $env:USERPROFILE '.codex\config.toml'
}
else {
    Join-Path $HOME '.codex\config.toml'
}
$repoBridgeConfig = Join-Path $RepoRoot '.assistant\config\agent-bridge.json'
$delegationCommand = Join-Path $RepoRoot '.assistant\commands\delegate-external-ai.md'
$filesToScan = @(
    (Join-Path $workspaceRoot 'README.md'),
    (Join-Path $workspaceRoot 'ASSISTANT.md'),
    (Join-Path $RepoRoot 'README.md'),
    (Join-Path $RepoRoot 'ASSISTANT.md'),
    (Join-Path $RepoRoot 'AGENTS.md'),
    (Join-Path $RepoRoot 'docs\ARCHITECTURE.md'),
    (Join-Path $RepoRoot 'docs\PATTERNS.md'),
    (Join-Path $RepoRoot 'docs\assistant\BASELINE.md'),
    (Join-Path $RepoRoot 'docs\assistant\CONFIG_MATRIX.md'),
    (Join-Path $RepoRoot 'docs\assistant\SPECIALISTS.md'),
    (Join-Path $RepoRoot 'docs\assistant\USE_CASE_MATRIX.md'),
    $delegationCommand,
    (Join-Path $RepoRoot '.assistant\commands\onboard.md')
)
$requiredTopology = @(
    (Join-Path $RepoRoot 'packs'),
    (Join-Path $RepoRoot 'workspaces\default\workspace.json'),
    (Join-Path $RepoRoot 'catalog'),
    (Join-Path $RepoRoot 'dist'),
    (Join-Path $RepoRoot 'tools\dev'),
    (Join-Path $RepoRoot 'tools\health'),
    (Join-Path $RepoRoot 'tools\workflows')
)
$issues = New-Object System.Collections.Generic.List[object]
function Add-Issue {
    param([string]$Severity, [string]$Code, [string]$Message, [string]$Path = '')
    $issues.Add([pscustomobject]@{ Severity = $Severity; Code = $Code; Message = $Message; Path = $Path }) | Out-Null
}
foreach ($path in $filesToScan) {
    if (-not (Test-Path $path)) {
        Add-Issue 'error' 'MISSING_FILE' 'Missing required canonical file.' $path
        continue
    }
    $text = Get-Content -Path $path -Raw
    if ($text -match '\.assistant/context/_session_state\.json') {
        Add-Issue 'error' 'STALE_CONTEXT_PATH' 'Found stale runtime cache path.' $path
    }
    if ($text -match 'ROUND_TASK_HANDOFF\.md') {
        Add-Issue 'error' 'STALE_HANDOFF_PATH' 'Found stale handoff path.' $path
    }
    if ($text -match '\bCLAUDE\.md\b' -or $text -match '\bCODEX_INSTRUCTIONS\.md\b') {
        Add-Issue 'error' 'LEGACY_CANONICAL_REF' 'Found legacy canonical instruction reference.' $path
    }
    if ($text -match '\.claude[/\\]') {
        Add-Issue 'error' 'LEGACY_ASSISTANT_PATH' 'Found legacy .claude path in canonical file.' $path
    }
}
foreach ($path in $requiredTopology) {
    if (-not (Test-Path $path)) {
        Add-Issue 'error' 'MISSING_TOPOLOGY' 'Required monorepo topology path is missing.' $path
    }
}
$repoAppDataShadow = Join-Path $RepoRoot '%APPDATA%'
if (Test-Path $repoAppDataShadow) {
    Add-Issue 'warning' 'TRACKED_APPDATA_ARTIFACT' 'Found misplaced %APPDATA% shadow deployment inside repo.' $repoAppDataShadow
}
$workspaceStateFiles = @(Get-ChildItem -Path (Join-Path $RepoRoot 'workspaces') -Filter 'project.context.json' -Recurse -File -ErrorAction SilentlyContinue)
foreach ($path in $workspaceStateFiles) {
    Add-Issue 'warning' 'LOCAL_WORKSPACE_STATE' 'Found generated workspace-local project.context.json in repo.' $path.FullName
}
$helperScripts = @(Get-ChildItem -Path (Join-Path $RepoRoot 'tools') -Filter '_*.ps1' -File -ErrorAction SilentlyContinue)
foreach ($file in $helperScripts) {
    $helperText = Get-Content -Path $file.FullName -Raw
    if ($helperText -match 'D:\\Development\\BIM765T-Revit-Agent' -or $helperText -match 'C:\\Users\\ADMIN\\Downloads\\03_BIM_Dynamo') {
        Add-Issue 'warning' 'HELPER_ABSOLUTE_REPO_PATH' 'Helper/debug script still contains a hardcoded absolute repo path.' $file.FullName
    }
}
$toolScratchFiles = @(
    (Join-Path $RepoRoot 'tools\_comments_context.json'),
    (Join-Path $RepoRoot 'tools\_comments_dryrun.json'),
    (Join-Path $RepoRoot 'tools\_comments_payload.json'),
    (Join-Path $RepoRoot 'tools\_schedule_context.json'),
    (Join-Path $RepoRoot 'tools\_schedule_dryrun.json'),
    (Join-Path $RepoRoot 'tools\_explain_tmp.json'),
    (Join-Path $RepoRoot 'tools\axis_audit_report.csv')
)
foreach ($path in $toolScratchFiles) {
    if (Test-Path $path) {
        Add-Issue 'warning' 'TOOL_SCRATCH_OUTPUT' 'Generated helper/debug output is present in tools/ and should remain ignored/untracked.' $path
    }
}
$catalogFiles = @(
    (Join-Path $RepoRoot 'catalog\pack-catalog.json'),
    (Join-Path $RepoRoot 'catalog\workspace-catalog.json'),
    (Join-Path $RepoRoot 'catalog\playbook-catalog.json'),
    (Join-Path $RepoRoot 'catalog\standards-catalog.json')
)
foreach ($path in $catalogFiles) {
    if (-not (Test-Path $path)) {
        continue
    }
    $catalogText = Get-Content -Path $path -Raw
    if ($catalogText -match '03_BIM_Dynamo') {
        Add-Issue 'warning' 'STALE_GENERATED_CATALOG_PATH' 'Catalog contains stale absolute repo path; regenerate export-pack-catalog.' $path
    }
}
$packFiles = @(Get-ChildItem -Path (Join-Path $RepoRoot 'packs') -Filter pack.json -Recurse -File -ErrorAction SilentlyContinue)
if ($packFiles.Count -eq 0) {
    Add-Issue 'error' 'MISSING_PACK_MANIFESTS' 'No pack.json manifests found under packs/.' (Join-Path $RepoRoot 'packs')
}
else {
    foreach ($file in $packFiles) {
        try {
            $manifest = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace([string]$manifest.PackId) -or [string]::IsNullOrWhiteSpace([string]$manifest.PackType)) {
                Add-Issue 'error' 'INVALID_PACK_MANIFEST' 'Pack manifest missing PackId or PackType.' $file.FullName
            }
        }
        catch {
            Add-Issue 'error' 'INVALID_PACK_MANIFEST' 'Pack manifest is not valid JSON.' $file.FullName
        }
    }
}
if (Test-Path $repoBridgeConfig) {
    try {
        $repoConfig = Get-Content -Path $repoBridgeConfig -Raw | ConvertFrom-Json
        $repoModel = [string]$repoConfig.openai.defaultModel
        if ((Test-Path $delegationCommand) -and -not [string]::IsNullOrWhiteSpace($repoModel)) {
            $callText = Get-Content -Path $delegationCommand -Raw
            if ($callText -notmatch [regex]::Escape($repoModel)) {
                Add-Issue 'warning' 'MODEL_BASELINE_DRIFT' "delegate-external-ai.md does not mention repo default model $repoModel." $delegationCommand
            }
        }
    }
    catch {
        Add-Issue 'warning' 'INVALID_REPO_BRIDGE_CONFIG' 'Could not parse .assistant/config/agent-bridge.json.' $repoBridgeConfig
    }
}
if (Test-Path $localAssistantConfig) {
    $configText = Get-Content -Path $localAssistantConfig -Raw
    if ($configText -notmatch 'model\s*=\s*"gpt-5\.4"') {
        Add-Issue 'warning' 'LOCAL_MODEL_BASELINE' 'Local assistant config is not pinned to gpt-5.4.' $localAssistantConfig
    }
}
$result = [pscustomobject]@{
    RepoRoot = $RepoRoot
    CheckedFiles = $filesToScan.Count
    IssueCount = $issues.Count
    HasErrors = @($issues | Where-Object Severity -eq 'error').Count -gt 0
    Issues = $issues
}
if ($AsJson) {
    $result | ConvertTo-Json -Depth 8
}
else {
    $result
}
