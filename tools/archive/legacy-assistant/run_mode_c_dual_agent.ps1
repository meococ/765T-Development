param(
    [Parameter(Mandatory = $true)]
    [string]$Task,
    [ValidateSet('reviewer', 'planner', 'debugger', 'bim_coordinator', 'executor')]
    [string]$Role = 'reviewer',
    [ValidateSet('haiku', 'sonnet', 'opus')]
    [string]$Model = 'sonnet',
    [string]$ProjectRoot = "",
    [string]$BridgeExe = "",
    [switch]$RefreshRevitContext = $true,
    [switch]$NoClaude
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Resolve-ProjectRoot
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$ClaudeExe = Resolve-ClaudeExe
$runDir = New-RunDirectory -ProjectRoot $ProjectRoot -TaskSlug "mode-c-$Role"
$schemaPath = Join-Path $ProjectRoot '.assistant\schemas\mode-c-dual-agent-result.schema.json'

$contextPath = Join-Path $ProjectRoot '.assistant\context\revit-task-context.latest.json'
if ($RefreshRevitContext) {
    $contextScript = Join-Path $PSScriptRoot 'get_revit_task_context.ps1'
    & $contextScript -ProjectRoot $ProjectRoot -BridgeExe $BridgeExe -OutputPath $contextPath | Out-Null
}

$contextSummary = "Khong co Revit context."
if (Test-Path $contextPath) {
    $ctx = Get-Content $contextPath -Raw | ConvertFrom-Json
    $docTitle = if ($ctx.ActiveDocument) { $ctx.ActiveDocument.Title } else { '<none>' }
    $viewName = if ($ctx.ActiveView) { $ctx.ActiveView.ViewName } else { '<none>' }
    $levelName = if ($ctx.LevelContext -and $ctx.LevelContext.LevelName) { $ctx.LevelContext.LevelName } else { '<unknown>' }
    $recentOps = 0
    if ($ctx.RecentOperations -and $ctx.RecentOperations.Operations) {
        $recentOps = $ctx.RecentOperations.Operations.Count
    }
    $recentEvents = 0
    if ($ctx.RecentEvents -and $ctx.RecentEvents.Events) {
        $recentEvents = $ctx.RecentEvents.Events.Count
    }

    $contextSummary = @"
RevitOnline: $($ctx.RevitOnline)
Document: $docTitle
View: $viewName
Level: $levelName
RecentOperations: $recentOps
RecentEvents: $recentEvents
ContextFile: $contextPath
"@
}

$roleInstruction = switch ($Role) {
    'planner' {
@"
Vai tro: Revit workflow planner.
- Chia task thanh phases ro rang
- uu tien an toan model, rollback, audit, diff
- chi de xuat tool plan thuc te tren 765T Revit Bridge
"@
    }
    'debugger' {
@"
Vai tro: Revit bridge debugger.
- tim root cause truoc
- khong doan mo ho
- de xuat check logs, context, bridge tools, Revit constraints
- uu tien reproducibility va instrumentation
"@
    }
    'bim_coordinator' {
@"
Vai tro: BIM coordinator.
- review theo view/sheet/workset/link/rules
- uu tien evidence va snapshot
- neu can sua model, chi de xuat write flow co dry-run + approval
"@
    }
    'executor' {
@"
Vai tro: cautious executor.
- dua ra tool plan cu the, tung buoc
- chi de xuat write action khi da co evidence/context phu hop
- uu tien idempotency, validation, post-check
"@
    }
    default {
@"
Vai tro: evidence-first reviewer.
- tong hop nhanh context, risks, va next actions
- neu context thieu, noi ro context thieu gi
- khong bo qua cac rang buoc Revit: transaction, worksharing, active view, scope
"@
    }
}

$projectInstructions = @"
Project: 765T Revit Agent / Revit Bridge

Rules:
- Khong hallucinate tinh trang model
- Neu Revit context thieu, phai noi ro
- Prefer bridge tools va docs trong project over generic guesses
- Su dung output ngan gon, co cau truc, huu dung cho agent orchestration
- Neu co write action, bat buoc phai nhac: dry-run -> approval -> execute -> validate -> diff

Relevant files:
- ASSISTANT.md
- docs/BIM765T.Revit.Agent-Architecture.md
- docs/BIM765T.Revit.McpHost.md
- docs/BIM765T.Revit.ReviewRuleEngine-v1.md
- docs/BIM765T.Revit.Snapshot-Strategy.md
- .assistant/context/revit-task-context.latest.json
"@

$prompt = @"
You are Claude Code acting as a sub-agent in Mode C for 765T Revit Bridge.

$roleInstruction

$projectInstructions

Current Revit Context Summary:
$contextSummary

User Task:
$Task

Return JSON only.
Required top-level keys:
- summary
- contextAssessment
- findings
- toolPlan
- fileTargets
- risks
- nextActions

toolPlan items should include:
- step
- tool
- purpose
- risk

Keep output concise and practical for a Revit/BIM engineering workflow.
"@

$strictSystemPrompt = @"
You are operating as a machine-readable sub-agent.
Return raw JSON only.
Do not include markdown fences.
Do not greet the user.
Do not ask follow-up questions.
Do not add any prose outside the JSON object.
If the context is insufficient, still return a valid JSON object with concise placeholders.
"@

Set-Content -Path (Join-Path $runDir 'prompt.txt') -Value $prompt -Encoding UTF8
if (Test-Path $contextPath) {
    Copy-Item $contextPath (Join-Path $runDir 'revit-task-context.json') -Force
}

if ($NoClaude) {
    [pscustomobject]@{
        Succeeded = $true
        Mode = 'prompt-only'
        RunDirectory = $runDir
        PromptPath = (Join-Path $runDir 'prompt.txt')
        ContextPath = $contextPath
    } | ConvertTo-Json -Depth 20
    exit 0
}

$raw = Invoke-ClaudePrint -ClaudeExe $ClaudeExe -PromptText $prompt -Model $Model -OutputFormat 'json' -JsonSchemaPath $schemaPath -AppendSystemPrompt $strictSystemPrompt -WorkingDirectory $ProjectRoot
Set-Content -Path (Join-Path $runDir 'response.envelope.json') -Value $raw -Encoding UTF8

$converted = ConvertFrom-ClaudeJsonEnvelopeResult -RawJson $raw
if ($converted.ResultText) {
    Set-Content -Path (Join-Path $runDir 'response.result.txt') -Value $converted.ResultText -Encoding UTF8
}
$finalParsed = $converted.Parsed
if (-not $finalParsed -and $converted.ResultText) {
    try {
        $finalParsed = $converted.ResultText | ConvertFrom-Json
    }
    catch {
        $finalParsed = $null
    }
}
if (-not $finalParsed -and $converted.ResultText) {
    $finalParsed = ConvertFrom-ModeCTextFallback -Text $converted.ResultText
}
if ($finalParsed) {
    $finalParsed | ConvertTo-Json -Depth 50 | Set-Content -Path (Join-Path $runDir 'response.parsed.json') -Encoding UTF8
}

[pscustomobject]@{
    Succeeded = $true
    Role = $Role
    Model = $Model
    RunDirectory = $runDir
    SchemaPath = $schemaPath
    Response = $finalParsed
    RawText = $converted.ResultText
} | ConvertTo-Json -Depth 30
