<#
.SYNOPSIS
    Reply mot message trong relay va archive message goc.
#>
param(
    [Parameter(Mandatory)]
    [string]$MessageId,

    [Parameter(Mandatory)]
    [ValidateSet('claude', 'codex', 'human')]
    [string]$From,

    [string]$Content = '',
    [string]$Role = '',
    [string]$Type = '',
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
$activeDir = Join-Path $projectRoot '.assistant\relay\active'
$archiveDir = Join-Path $projectRoot '.assistant\relay\archive'
New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null

$parentFile = Join-Path $activeDir "$MessageId.json"
if (-not (Test-Path $parentFile)) {
    $candidates = @(Get-ChildItem $activeDir -Filter '*.json' | Where-Object {
        $_.BaseName -like "*$MessageId*"
    })
    if ($candidates.Count -eq 1) {
        $parentFile = $candidates[0].FullName
    }
    elseif ($candidates.Count -gt 1) {
        throw "Multiple messages match '$MessageId'. Be more specific."
    }
    else {
        throw "Message '$MessageId' not found in active relay."
    }
}

$parent = Get-Content $parentFile -Raw | ConvertFrom-Json

$replyTypeMap = @{
    'task'     = 'review'
    'review'   = 'decision'
    'plan'     = 'critique'
    'critique' = 'decision'
    'decision' = 'status'
    'handoff'  = 'status'
    'status'   = 'status'
}

$resolvedRole = if (-not [string]::IsNullOrWhiteSpace($Role)) { $Role } else { $parent.role }
$resolvedType = if (-not [string]::IsNullOrWhiteSpace($Type)) { $Type } else { $replyTypeMap[$parent.type] }
if (-not $resolvedType) { $resolvedType = 'status' }

$contentObj = ConvertTo-RelayContentObject -RawContent $Content

$replyId = [Guid]::NewGuid().ToString('N')
$reply = [ordered]@{
    messageId       = $replyId
    timestamp       = [DateTime]::UtcNow.ToString('o')
    from            = $From
    to              = $parent.from
    role            = $resolvedRole
    type            = $resolvedType
    sessionId       = $parent.sessionId
    parentMessageId = $parent.messageId
    taskContext     = $parent.taskContext
    content         = $contentObj
    attachments     = @()
}

if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $reply['metadata'] = @{ model = $Model }
}

$replyPath = Join-Path $activeDir "$replyId.json"
Write-JsonFileAtomically -Path $replyPath -Data $reply

$archiveStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$archiveName = "${archiveStamp}_$($parent.messageId).json"
Move-Item -Path $parentFile -Destination (Join-Path $archiveDir $archiveName) -Force

$sessionDir = Join-Path $projectRoot ".assistant\relay\sessions\$($parent.sessionId)"
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
$threadFile = Join-Path $sessionDir 'thread.json'
$thread = @()
if (Test-Path $threadFile) {
    $thread = @(Get-Content $threadFile -Raw | ConvertFrom-Json)
}
$thread += $replyId
$thread = @($thread | Where-Object { $_ } | Select-Object -Unique)
Write-JsonFileAtomically -Path $threadFile -Data $thread

[pscustomobject]@{
    Sent            = $true
    ReplyMessageId  = $replyId
    ParentMessageId = $parent.messageId
    From            = $From
    To              = $parent.from
    Role            = $resolvedRole
    Type            = $resolvedType
    SessionId       = $parent.sessionId
    ParentArchived  = $true
    ReplyFilePath   = $replyPath
} | ConvertTo-Json -Depth 5
