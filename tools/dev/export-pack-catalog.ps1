param(
    [string]$RepoRoot = ""
)

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

$catalogDir = Join-Path $RepoRoot 'catalog'
New-Item -ItemType Directory -Force -Path $catalogDir | Out-Null

function Get-RepoRelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    $normalizedRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/')
    $normalizedPath = [System.IO.Path]::GetFullPath($Path)

    if ($normalizedPath.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $normalizedPath.Substring($normalizedRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    }

    return $normalizedPath -replace '\\', '/'
}

$packFiles = Get-ChildItem -Path (Join-Path $RepoRoot 'packs') -Filter pack.json -Recurse -File -ErrorAction SilentlyContinue
$packs = @($packFiles | ForEach-Object {
    $manifest = Get-Content $_.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{
        PackId = $manifest.PackId
        PackType = $manifest.PackType
        Version = $manifest.Version
        DisplayName = $manifest.DisplayName
        RootPath = Get-RepoRelativePath $_.Directory.FullName
        SourcePath = Get-RepoRelativePath $_.FullName
        EnabledByDefault = [bool]$manifest.EnabledByDefault
        Exports = @($manifest.Exports)
    }
})

$workspaceFiles = Get-ChildItem -Path (Join-Path $RepoRoot 'workspaces') -Filter workspace.json -Recurse -File -ErrorAction SilentlyContinue
$workspaces = @($workspaceFiles | ForEach-Object {
    $manifest = Get-Content $_.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{
        WorkspaceId = $manifest.WorkspaceId
        DisplayName = $manifest.DisplayName
        RootPath = Get-RepoRelativePath $_.Directory.FullName
        EnabledPacks = @($manifest.EnabledPacks)
        PreferredStandardsPacks = @($manifest.PreferredStandardsPacks)
        PreferredPlaybookPacks = @($manifest.PreferredPlaybookPacks)
        AllowedAgents = @($manifest.AllowedAgents)
        AllowedSpecialists = @($manifest.AllowedSpecialists)
    }
})

$playbookFiles = Get-ChildItem -Path (Join-Path $RepoRoot 'packs\playbooks') -Filter *.json -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'pack.json' }
$playbooks = @($playbookFiles | ForEach-Object {
    $playbook = Get-Content $_.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{
        PlaybookId = $playbook.PlaybookId
        PackId = $playbook.PackId
        Description = $playbook.Description
        TriggerPhrases = @($playbook.TriggerPhrases)
        RequiredInputs = @($playbook.RequiredInputs)
        StandardsRefs = @($playbook.StandardsRefs)
        SourcePath = Get-RepoRelativePath $_.FullName
    }
})

$standardFiles = Get-ChildItem -Path (Join-Path $RepoRoot 'packs\standards') -Filter *.json -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'pack.json' }
$standards = @($standardFiles | ForEach-Object {
    [pscustomobject]@{
        FileName = $_.Name
        RelativePath = $_.FullName.Substring($RepoRoot.Length + 1)
        SourcePath = Get-RepoRelativePath $_.FullName
    }
})

@{ GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); PathMode = 'repo-relative'; Packs = $packs } | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $catalogDir 'pack-catalog.json') -Encoding UTF8
@{ GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); PathMode = 'repo-relative'; Workspaces = $workspaces } | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $catalogDir 'workspace-catalog.json') -Encoding UTF8
@{ GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); PathMode = 'repo-relative'; Playbooks = $playbooks } | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $catalogDir 'playbook-catalog.json') -Encoding UTF8
@{ GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); PathMode = 'repo-relative'; Standards = $standards } | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $catalogDir 'standards-catalog.json') -Encoding UTF8
@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Source = 'live_registry_required'
    GeneratorScript = 'tools/generate_tool_catalog.ps1'
    Status = 'run bridge-backed tool catalog export when Revit/bridge is available'
} | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $catalogDir 'tool-catalog.json') -Encoding UTF8

[pscustomobject]@{
    PackCount = $packs.Count
    WorkspaceCount = $workspaces.Count
    PlaybookCount = $playbooks.Count
    StandardsFileCount = $standards.Count
    ToolCatalogStub = (Join-Path $catalogDir 'tool-catalog.json')
    CatalogDir = $catalogDir
} | ConvertTo-Json -Compress
