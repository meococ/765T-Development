<#
.SYNOPSIS
    Đọc messages trong relay mailbox dành cho agent chỉ định.

.DESCRIPTION
    Scan .assistant/relay/active/ và trả về messages addressed to target agent.
    Mặc định trả tất cả, dùng -Latest để chỉ lấy message mới nhất.

.PARAMETER For
    Agent muốn đọc inbox: claude, codex, hoặc human.

.PARAMETER Latest
    Chỉ trả message mới nhất.

.PARAMETER SessionId
    Filter theo session ID.

.PARAMETER AsMarkdown
    Output markdown thay vì JSON (tiện cho agent đọc trong context).

.EXAMPLE
    .\relay_receive.ps1 -For claude
    .\relay_receive.ps1 -For codex -Latest
    .\relay_receive.ps1 -For claude -SessionId session-20260316 -AsMarkdown
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('claude', 'codex', 'human')]
    [string]$For,

    [switch]$Latest,
    [string]$SessionId = "",
    [switch]$AsMarkdown
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$projectRoot = Resolve-ProjectRoot
$activeDir = Join-Path $projectRoot '.assistant\relay\active'

if (-not (Test-Path $activeDir)) {
    if ($AsMarkdown) {
        Write-Output "# Relay Inbox: $For`n`nKhông có message nào."
    }
    else {
        [pscustomobject]@{ For = $For; Count = 0; Messages = @() } | ConvertTo-Json -Depth 10
    }
    return
}

$allMessages = @()
Get-ChildItem $activeDir -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $msg = Get-Content $_.FullName -Raw | ConvertFrom-Json
        if ($msg.to -eq $For) {
            if (-not [string]::IsNullOrWhiteSpace($SessionId) -and $msg.sessionId -ne $SessionId) {
                return
            }
            $msg | Add-Member -NotePropertyName '_filePath' -NotePropertyValue $_.FullName -Force
            $allMessages += $msg
        }
    }
    catch {
        Write-Warning "Skipping malformed relay message: $($_.Name)"
    }
}

# Sort by timestamp descending (newest first)
$allMessages = @($allMessages | Sort-Object -Property timestamp -Descending)

if ($Latest -and $allMessages.Count -gt 0) {
    $allMessages = @($allMessages[0])
}

if ($AsMarkdown) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Relay Inbox: $For")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("**Pending messages:** $($allMessages.Count)")
    [void]$sb.AppendLine("")

    foreach ($msg in $allMessages) {
        [void]$sb.AppendLine("---")
        [void]$sb.AppendLine("## [$($msg.type)] from $($msg.from) - Role: $($msg.role)")
        [void]$sb.AppendLine("- **MessageId:** ``$($msg.messageId)``")
        [void]$sb.AppendLine("- **Time:** $($msg.timestamp)")
        [void]$sb.AppendLine("- **Session:** $($msg.sessionId)")
        if ($msg.PSObject.Properties['parentMessageId'] -and $msg.parentMessageId) {
            [void]$sb.AppendLine("- **Reply to:** ``$($msg.parentMessageId)``")
        }
        if ($msg.taskContext) {
            [void]$sb.AppendLine("- **Task:** $($msg.taskContext)")
        }
        [void]$sb.AppendLine("")

        if ($msg.content) {
            if ($msg.content.PSObject.Properties['summary'] -and $msg.content.summary) {
                [void]$sb.AppendLine("### Summary")
                [void]$sb.AppendLine($msg.content.summary)
                [void]$sb.AppendLine("")
            }
            if ($msg.content.PSObject.Properties['findings'] -and $msg.content.findings) {
                [void]$sb.AppendLine("### Findings")
                foreach ($f in @($msg.content.findings)) { [void]$sb.AppendLine("- $f") }
                [void]$sb.AppendLine("")
            }
            if ($msg.content.PSObject.Properties['risks'] -and $msg.content.risks) {
                [void]$sb.AppendLine("### Risks")
                foreach ($r in @($msg.content.risks)) { [void]$sb.AppendLine("- $r") }
                [void]$sb.AppendLine("")
            }
            if ($msg.content.PSObject.Properties['questions'] -and $msg.content.questions) {
                [void]$sb.AppendLine("### Questions")
                foreach ($q in @($msg.content.questions)) { [void]$sb.AppendLine("- $q") }
                [void]$sb.AppendLine("")
            }
            if ($msg.content.PSObject.Properties['nextActions'] -and $msg.content.nextActions) {
                [void]$sb.AppendLine("### Next Actions")
                foreach ($a in @($msg.content.nextActions)) { [void]$sb.AppendLine("- $a") }
                [void]$sb.AppendLine("")
            }
            if ($msg.content.PSObject.Properties['toolPlan'] -and $msg.content.toolPlan) {
                [void]$sb.AppendLine("### Tool Plan")
                foreach ($step in @($msg.content.toolPlan)) {
                    [void]$sb.AppendLine("$($step.step). ``$($step.tool)`` - $($step.purpose)")
                }
                [void]$sb.AppendLine("")
            }
        }
    }

    Write-Output $sb.ToString()
}
else {
    # Strip internal _filePath before output
    $cleaned = $allMessages | ForEach-Object {
        $props = $_ | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -ne '_filePath' }
        $obj = [ordered]@{}
        foreach ($p in $props) { $obj[$p.Name] = $_.$($p.Name) }
        [pscustomobject]$obj
    }

    [pscustomobject]@{
        For      = $For
        Count    = $allMessages.Count
        Messages = @($cleaned)
    } | ConvertTo-Json -Depth 10
}
