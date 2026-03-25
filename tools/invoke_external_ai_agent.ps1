<##
.SYNOPSIS
    G?i WorkerHost external AI broker theo c?ch neutral, kh?ng ph? thu?c vendor-specific CLI.

.DESCRIPTION
    Script n?y g?i request t?i WorkerHost `/api/external-ai/chat`.
    N? gi? ??ng operating model external-AI-broker-first c?a repo.
##>
param(
    [Parameter(Mandatory)]
    [string]$Task,

    [string]$MissionId = '',
    [string]$SessionId = 'external-ai-cli',
    [string]$PersonaId = 'revit_worker',
    [string]$ActorId = 'external-ai-cli',
    [string]$DocumentKey = '',
    [string]$TargetDocument = '',
    [string]$TargetView = '',
    [string]$WorkerHostUrl = '',
    [switch]$ContinueMission,
    [switch]$SaveRun
)

$ErrorActionPreference = 'Stop'

function Resolve-RepoRoot {
    $current = $PSScriptRoot
    while ($current) {
        if (Test-Path (Join-Path $current 'BIM765T.Revit.Agent.sln')) {
            return $current
        }

        $parent = Split-Path $current -Parent
        if (-not $parent -or $parent -eq $current) {
            break
        }

        $current = $parent
    }

    throw 'Khong resolve duoc repo root.'
}

$repoRoot = Resolve-RepoRoot
if ([string]::IsNullOrWhiteSpace($WorkerHostUrl)) {
    $WorkerHostUrl = if (-not [string]::IsNullOrWhiteSpace($env:BIM765T_WORKERHOST_URL)) {
        $env:BIM765T_WORKERHOST_URL
    }
    else {
        'http://127.0.0.1:50765'
    }
}
$WorkerHostUrl = $WorkerHostUrl.TrimEnd('/')

$status = Invoke-RestMethod -Uri "$WorkerHostUrl/api/external-ai/status" -Method Get
if (-not $status.Ready) {
    throw "WorkerHost chua san sang. Url=$WorkerHostUrl"
}

$body = [ordered]@{
    missionId      = $MissionId
    sessionId      = $SessionId
    message        = $Task
    personaId      = $PersonaId
    continueMission = [bool]$ContinueMission
    actorId        = $ActorId
    documentKey    = $DocumentKey
    targetDocument = $TargetDocument
    targetView     = $TargetView
    timeoutMs      = 120000
}

$response = Invoke-RestMethod -Uri "$WorkerHostUrl/api/external-ai/chat" -Method Post -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 8)

if ($SaveRun) {
    $runRoot = Join-Path $repoRoot '.assistant\runs\external-ai'
    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $runDir = Join-Path $runRoot $timestamp
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null

    $Task | Set-Content (Join-Path $runDir 'task.txt') -Encoding UTF8
    ($body | ConvertTo-Json -Depth 8) | Set-Content (Join-Path $runDir 'request.json') -Encoding UTF8
    ($response | ConvertTo-Json -Depth 12) | Set-Content (Join-Path $runDir 'response.json') -Encoding UTF8
}

$response | ConvertTo-Json -Depth 12
