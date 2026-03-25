param(
    [string]$ProjectRoot = "",
    [string]$Prompt = "",
    [string]$PromptFile = "",
    [ValidateSet('text', 'json')]
    [string]$OutputFormat = 'text',
    [ValidateSet('haiku', 'sonnet', 'opus')]
    [string]$Model = 'sonnet',
    [string]$SchemaPath = "",
    [string]$AppendSystemPrompt = "",
    [switch]$SaveRun
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

if ([string]::IsNullOrWhiteSpace($Prompt)) {
    if ([string]::IsNullOrWhiteSpace($PromptFile) -or -not (Test-Path $PromptFile)) {
        throw "Can prompt hoac PromptFile hop le."
    }
    $Prompt = Get-Content $PromptFile -Raw
}

$runDir = $null
if ($SaveRun) {
    $runDir = New-RunDirectory -ProjectRoot $ProjectRoot -TaskSlug 'ask-claude'
    Set-Content -Path (Join-Path $runDir 'prompt.txt') -Value $Prompt -Encoding UTF8
}

$raw = Invoke-ClaudePrint -ClaudeExe $ClaudeExe -PromptText $Prompt -Model $Model -OutputFormat $OutputFormat -JsonSchemaPath $SchemaPath -AppendSystemPrompt $AppendSystemPrompt -WorkingDirectory $ProjectRoot

if ($SaveRun -and $runDir) {
    $responsePath = if ($OutputFormat -eq 'json') { 'response.json' } else { 'response.txt' }
    Set-Content -Path (Join-Path $runDir $responsePath) -Value $raw -Encoding UTF8
}

$raw
