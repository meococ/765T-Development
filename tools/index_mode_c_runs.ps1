param(
    [string]$ProjectRoot = ""
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Resolve-ProjectRoot
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$runsRoot = Join-Path $ProjectRoot '.assistant\runs'
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
$indexPath = Join-Path $runsRoot 'index.json'
$mdPath = Join-Path $runsRoot 'index.md'

$items = New-Object System.Collections.Generic.List[object]
foreach ($run in @(Get-RunDirectories -ProjectRoot $ProjectRoot -NamePrefix 'mode-c-')) {
    $dir = $run.FullName
    $parsedPath = Join-Path $dir 'response.parsed.json'
    $ctxPath = Join-Path $dir 'revit-task-context.json'
    $execPath = Join-Path $dir 'bridge-execution.json'
    $promptPath = Join-Path $dir 'prompt.txt'

    $summary = ''
    $doc = ''
    $view = ''
    $role = ''
    if (Test-Path $parsedPath) {
        try {
            $parsed = Get-Content $parsedPath -Raw | ConvertFrom-Json
            $summary = [string]$parsed.summary
        } catch {}
    }
    if (Test-Path $ctxPath) {
        try {
            $ctx = Get-Content $ctxPath -Raw | ConvertFrom-Json
            if ($ctx.ActiveDocument) { $doc = $ctx.ActiveDocument.Title }
            if ($ctx.ActiveView) { $view = $ctx.ActiveView.ViewName }
        } catch {}
    }
    if ($run.Name -match 'mode-c-([a-z_]+)$') {
        $role = $matches[1]
    }

    $executionStatus = ''
    if (Test-Path $execPath) {
        try {
            $exec = Get-Content $execPath -Raw | ConvertFrom-Json
            $executionStatus = "executed:{0}" -f @($exec.Results).Count
        } catch {}
    }

    $items.Add([pscustomobject]@{
        RunName = $run.Name
        RunDirectory = $dir
        Role = $role
        Document = $doc
        View = $view
        Summary = $summary
        HasExecution = [bool](Test-Path $execPath)
        ExecutionStatus = $executionStatus
        PromptPath = if (Test-Path $promptPath) { $promptPath } else { $null }
        ParsedPath = if (Test-Path $parsedPath) { $parsedPath } else { $null }
        ContextPath = if (Test-Path $ctxPath) { $ctxPath } else { $null }
        LastWriteTimeUtc = $run.LastWriteTimeUtc.ToString('o')
    }) | Out-Null
}

$index = [pscustomobject]@{
    GeneratedAtUtc = [DateTime]::UtcNow.ToString('o')
    RunCount = $items.Count
    Runs = $items
}
Write-JsonFile -Path $indexPath -Data $index

$md = New-Object System.Collections.Generic.List[string]
$md.Add('# Mode C Run Index') | Out-Null
$md.Add('') | Out-Null
$md.Add(("Generated: {0}" -f [DateTime]::UtcNow.ToString('u'))) | Out-Null
$md.Add('') | Out-Null
foreach ($item in $items) {
    $md.Add(("## {0}" -f $item.RunName)) | Out-Null
    $md.Add(("- Role: {0}" -f $item.Role)) | Out-Null
    $md.Add(("- Document: {0}" -f $item.Document)) | Out-Null
    $md.Add(("- View: {0}" -f $item.View)) | Out-Null
    $md.Add(("- HasExecution: {0}" -f $item.HasExecution)) | Out-Null
    if ($item.ExecutionStatus) { $md.Add(("- ExecutionStatus: {0}" -f $item.ExecutionStatus)) | Out-Null }
    if ($item.Summary) { $md.Add(("- Summary: {0}" -f $item.Summary)) | Out-Null }
    $md.Add('') | Out-Null
}
$md -join "`r`n" | Set-Content -Path $mdPath -Encoding UTF8

[pscustomobject]@{
    Succeeded = $true
    RunCount = $items.Count
    IndexPath = $indexPath
    MarkdownPath = $mdPath
} | ConvertTo-Json -Depth 20
