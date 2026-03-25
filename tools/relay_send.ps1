<#
.SYNOPSIS
    Gui message vao relay mailbox cho agent khac doc.
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('claude', 'codex', 'human')]
    [string]$From,

    [Parameter(Mandatory)]
    [ValidateSet('claude', 'codex', 'human')]
    [string]$To,

    [Parameter(Mandatory)]
    [ValidateSet('reviewer', 'planner', 'debugger', 'executor', 'critic', 'architect')]
    [string]$Role,

    [Parameter(Mandatory)]
    [ValidateSet('task', 'review', 'plan', 'critique', 'decision', 'handoff', 'status')]
    [string]$Type,

    [string]$TaskContext = '',
    [string]$Content = '',
    [string]$SessionId = '',
    [string]$ParentMessageId = '',
    [string]$Attachments = '',
    [string]$Model = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

function ConvertTo-RelayContentObject {
    param(
        [string]$RawContent
    )

    $contentObj = [ordered]@{}
    if ([string]::IsNullOrWhiteSpace($RawContent)) {
        return $contentObj
    }

    if (Test-Path $RawContent) {
        $loaded = Get-Content $RawContent -Raw | ConvertFrom-Json -AsHashtable
        foreach ($entry in $loaded.GetEnumerator()) {
            $contentObj[$entry.Key] = $entry.Value
        }
    }
    else {
        foreach ($pair in ($RawContent -split ';')) {
            $kv = $pair -split '=', 2
            if ($kv.Count -ne 2) {
                continue
            }

            $key = $kv[0].Trim()
            $value = $kv[1].Trim()
            if ([string]::IsNullOrWhiteSpace($key)) {
                continue
            }

            if ($value -match ',') {
                $contentObj[$key] = @($value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            }
            else {
                $contentObj[$key] = $value
            }
        }
    }

    $arrayKeys = @('findings', 'risks', 'questions', 'decisions', 'nextActions')
    foreach ($key in $arrayKeys) {
        if (-not $contentObj.Contains($key) -or $null -eq $contentObj[$key]) {
            continue
        }

        $value = $contentObj[$key]
        if ($value -is [string]) {
            $trimmed = $value.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmed)) {
                $contentObj[$key] = [object[]]@()
            }
            else {
                $contentObj[$key] = [object[]]@($trimmed)
            }
            continue
        }

        $contentObj[$key] = [object[]]@($value)
    }

    if ($contentObj.Contains('summary') -and $contentObj['summary'] -is [System.Collections.IEnumerable] -and $contentObj['summary'] -isnot [string]) {
        $contentObj['summary'] = (@($contentObj['summary']) -join '; ').Trim()
    }

    return $contentObj
}

$projectRoot = Resolve-ProjectRoot
$relayDir = Join-Path $projectRoot '.assistant\relay\active'
New-Item -ItemType Directory -Force -Path $relayDir | Out-Null

$messageId = [Guid]::NewGuid().ToString('N')
$timestamp = [DateTime]::UtcNow.ToString('o')

if ([string]::IsNullOrWhiteSpace($SessionId)) {
    $SessionId = 'session-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
}

$contentObj = ConvertTo-RelayContentObject -RawContent $Content

$attachmentList = @()
if (-not [string]::IsNullOrWhiteSpace($Attachments)) {
    $attachmentList = @($Attachments -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$message = [ordered]@{
    messageId   = $messageId
    timestamp   = $timestamp
    from        = $From
    to          = $To
    role        = $Role
    type        = $Type
    sessionId   = $SessionId
    taskContext = $TaskContext
    content     = $contentObj
    attachments = $attachmentList
}

if (-not [string]::IsNullOrWhiteSpace($ParentMessageId)) {
    $message['parentMessageId'] = $ParentMessageId
}

if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $message['metadata'] = @{ model = $Model }
}

$latestContext = Join-Path $projectRoot '.assistant\context\revit-task-context.latest.json'
if (Test-Path $latestContext) {
    $message['revitContextPath'] = '.assistant/context/revit-task-context.latest.json'
}

$filePath = Join-Path $relayDir "$messageId.json"
Write-JsonFileAtomically -Path $filePath -Data $message

$sessionDir = Join-Path $projectRoot ".assistant\relay\sessions\$SessionId"
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$threadFile = Join-Path $sessionDir 'thread.json'
$thread = @()
if (Test-Path $threadFile) {
    $thread = @(Get-Content $threadFile -Raw | ConvertFrom-Json)
}
$thread += $messageId
$thread = @($thread | Where-Object { $_ } | Select-Object -Unique)
Write-JsonFileAtomically -Path $threadFile -Data $thread

[pscustomobject]@{
    Sent      = $true
    MessageId = $messageId
    From      = $From
    To        = $To
    Role      = $Role
    Type      = $Type
    SessionId = $SessionId
    FilePath  = $filePath
} | ConvertTo-Json -Depth 5
