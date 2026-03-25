<#
.SYNOPSIS
    Hiển thị trạng thái relay mailbox.

.DESCRIPTION
    Đếm messages chờ xử lý per agent, liệt kê sessions đang active,
    và hiển thị tổng quan nhanh.

.EXAMPLE
    .\relay_status.ps1
#>

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot
$activeDir = Join-Path $projectRoot '.assistant\relay\active'
$archiveDir = Join-Path $projectRoot '.assistant\relay\archive'
$sessionsDir = Join-Path $projectRoot '.assistant\relay\sessions'

$activeCount = @{ claude = 0; codex = 0; human = 0; total = 0 }
$latestMessages = @()

if (Test-Path $activeDir) {
    Get-ChildItem $activeDir -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $msg = Get-Content $_.FullName -Raw | ConvertFrom-Json
            if ($activeCount.ContainsKey([string]$msg.to)) {
                $activeCount[$msg.to] += 1
            }
            $activeCount['total'] += 1
            $summaryText = '(no summary)'
            if ($msg.content -and $msg.content.summary) {
                $summaryRaw = [string]$msg.content.summary
                if (-not [string]::IsNullOrWhiteSpace($summaryRaw)) {
                    $summaryText = $summaryRaw.Substring(0, [Math]::Min(60, $summaryRaw.Length))
                }
            }

            $latestMessages += [pscustomobject]@{
                MessageId = if ([string]::IsNullOrWhiteSpace([string]$msg.messageId)) { '(unknown)' } elseif ($msg.messageId.Length -le 8) { [string]$msg.messageId } else { $msg.messageId.Substring(0, 8) + '...' }
                From      = $msg.from
                To        = $msg.to
                Role      = $msg.role
                Type      = $msg.type
                Time      = $msg.timestamp
                Summary   = $summaryText
            }
        }
        catch {
            Write-Warning "Skipping malformed message: $($_.Name)"
        }
    }
}

$archivedCount = 0
if (Test-Path $archiveDir) {
    $archivedCount = @(Get-ChildItem $archiveDir -Filter '*.json' -ErrorAction SilentlyContinue).Count
}

$activeSessions = @()
if (Test-Path $sessionsDir) {
    Get-ChildItem $sessionsDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $threadFile = Join-Path $_.FullName 'thread.json'
        $msgCount = 0
        if (Test-Path $threadFile) {
            $thread = @(Get-Content $threadFile -Raw | ConvertFrom-Json)
            $msgCount = @($thread).Count
        }
        $activeSessions += [pscustomobject]@{
            SessionId    = $_.Name
            MessageCount = $msgCount
        }
    }
}

$status = [ordered]@{
    Timestamp       = [DateTime]::UtcNow.ToString('o')
    ActiveMessages  = [ordered]@{
        Total  = $activeCount['total']
        Claude = $activeCount['claude']
        Codex  = $activeCount['codex']
        Human  = $activeCount['human']
    }
    ArchivedMessages = $archivedCount
    ActiveSessions   = @($activeSessions)
    PendingMessages  = @($latestMessages | Sort-Object Time -Descending)
}

$status | ConvertTo-Json -Depth 10
