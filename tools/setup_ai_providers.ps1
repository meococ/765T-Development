<#
.SYNOPSIS
    Setup AI provider environment variables for BIM765T Revit Agent.
.DESCRIPTION
    Configures API keys for OpenRouter, MiniMax, OpenAI, and/or Anthropic providers.
    Keys are stored as User-level environment variables (persistent across sessions).
.PARAMETER Provider
    Which provider to configure: 'openrouter', 'minimax', 'openai', 'anthropic', or 'all'.
    Default: 'all' (prompts for each provider).
.PARAMETER MiniMaxKey
    MiniMax API key. If not provided, prompts interactively.
.PARAMETER OpenRouterKey
    OpenRouter API key. If not provided, prompts interactively.
.PARAMETER OpenAiKey
    OpenAI API key. If not provided, prompts interactively.
.PARAMETER AnthropicKey
    Anthropic API key. If not provided, prompts interactively.
.PARAMETER ListOnly
    Show current provider configuration without making changes.
.PARAMETER RemoveAll
    Remove all AI provider environment variables.
.EXAMPLE
    .\setup_ai_providers.ps1
    .\setup_ai_providers.ps1 -Provider minimax -MiniMaxKey "sk-..."
    .\setup_ai_providers.ps1 -Provider openrouter -OpenRouterKey "sk-or-..."
    .\setup_ai_providers.ps1 -ListOnly
#>
param(
    [ValidateSet('openrouter', 'minimax', 'openai', 'anthropic', 'all')]
    [string]$Provider = 'all',
    [string]$MiniMaxKey = '',
    [string]$OpenRouterKey = '',
    [string]$OpenAiKey = '',
    [string]$AnthropicKey = '',
    [switch]$ListOnly,
    [switch]$RemoveAll
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')
# ── Provider definitions ──────────────────────────────────────────────
$providers = @{
    openrouter = @{
        DisplayName = 'OpenRouter'
        KeyVar      = 'OPENROUTER_API_KEY'
        KeyPrefix   = 'sk-or-'
        ExtraVars   = @(
            @{ Name = 'OPENROUTER_PRIMARY_MODEL';  Default = 'openai/gpt-5.4' }
            @{ Name = 'OPENROUTER_FALLBACK_MODEL';  Default = 'openai/gpt-5.4' }
            @{ Name = 'OPENROUTER_RESPONSE_MODEL';  Default = 'openai/gpt-5.4' }
            @{ Name = 'OPENROUTER_HTTP_REFERER';    Default = 'https://bim765t.dev' }
            @{ Name = 'OPENROUTER_X_TITLE';         Default = 'BIM765T Revit Worker' }
        )
        Priority    = 1
        Notes       = 'Supports 300+ models (GPT, Claude, Gemini, etc.) via single key'
        GetKeyUrl   = 'https://openrouter.ai/keys'
    }
    minimax = @{
        DisplayName = 'MiniMax'
        KeyVar      = 'MINIMAX_API_KEY'
        KeyPrefix   = 'sk-'
        ExtraVars   = @(
            @{ Name = 'MINIMAX_BASE_URL';       Default = 'https://api.minimax.io/v1' }
            @{ Name = 'MINIMAX_MODEL';          Default = 'MiniMax-M2.7-highspeed' }
            @{ Name = 'MINIMAX_FALLBACK_MODEL'; Default = 'MiniMax-M2.7' }
            @{ Name = 'MINIMAX_RESPONSE_MODEL'; Default = 'MiniMax-M2.7-highspeed' }
            @{ Name = 'MINIMAX_X_TITLE';        Default = 'BIM765T Revit Worker' }
        )
        Priority    = 2
        Notes       = 'OpenAI-compatible MiniMax lane for low-cost, fast reasoning'
        GetKeyUrl   = 'https://platform.minimax.io/'
    }
    openai = @{
        DisplayName = 'OpenAI'
        KeyVar      = 'OPENAI_API_KEY'
        KeyPrefix   = 'sk-'
        ExtraVars   = @(
            @{ Name = 'OPENAI_MODEL';          Default = 'gpt-5.4' }
            @{ Name = 'OPENAI_FALLBACK_MODEL'; Default = 'gpt-5.4' }
            @{ Name = 'OPENAI_RESPONSE_MODEL'; Default = 'gpt-5.4' }
        )
        Priority    = 3
        Notes       = 'Direct OpenAI API access'
        GetKeyUrl   = 'https://platform.openai.com/api-keys'
    }
    anthropic = @{
        DisplayName = 'Anthropic (Claude)'
        KeyVar      = 'ANTHROPIC_API_KEY'
        KeyPrefix   = 'sk-ant-'
        ExtraVars   = @(
            @{ Name = 'ANTHROPIC_MODEL'; Default = 'claude-sonnet-4-20250514' }
        )
        Priority    = 4
        Notes       = 'Claude models (Sonnet, Opus, Haiku)'
        GetKeyUrl   = 'https://console.anthropic.com/settings/keys'
    }
}
# ── Helper functions ──────────────────────────────────────────────────
function Get-ProviderStatus {
    param([string]$Name)
    $def = $providers[$Name]
    $currentKey = [Environment]::GetEnvironmentVariable($def.KeyVar, 'User')
    $processKey = [Environment]::GetEnvironmentVariable($def.KeyVar, 'Process')
    $hasKey = -not [string]::IsNullOrWhiteSpace($currentKey)
    $hasProcessKey = -not [string]::IsNullOrWhiteSpace($processKey)
    $maskedKey = if ($hasKey) {
        $k = $currentKey
        if ($k.Length -gt 12) { $k.Substring(0, 8) + '...' + $k.Substring($k.Length - 4) }
        else { '***' }
    } else { '' }
    $extras = @{}
    foreach ($ev in $def.ExtraVars) {
        $val = [Environment]::GetEnvironmentVariable($ev.Name, 'User')
        if ([string]::IsNullOrWhiteSpace($val)) { $val = $ev.Default }
        $extras[$ev.Name] = $val
    }
    return [pscustomobject]@{
        Provider    = $def.DisplayName
        Priority    = $def.Priority
        KeyVar      = $def.KeyVar
        Configured  = $hasKey -or $hasProcessKey
        MaskedKey   = $maskedKey
        Source      = if ($hasKey) { 'User env' } elseif ($hasProcessKey) { 'Process env' } else { 'Not set' }
        ExtraVars   = $extras
        Notes       = $def.Notes
        GetKeyUrl   = $def.GetKeyUrl
    }
}
function Show-AllStatus {
    Write-Host ""
    Write-Host "=== BIM765T AI Provider Status ===" -ForegroundColor Cyan
    Write-Host ""
    $activeProvider = 'NONE (rule-first mode)'
    $providerOverride = [Environment]::GetEnvironmentVariable('BIM765T_LLM_PROVIDER', 'User')
    if ([string]::IsNullOrWhiteSpace($providerOverride)) {
        $providerOverride = [Environment]::GetEnvironmentVariable('BIM765T_LLM_PROVIDER', 'Process')
    }
    foreach ($name in @('openrouter', 'minimax', 'openai', 'anthropic')) {
        $status = Get-ProviderStatus -Name $name
        $icon = if ($status.Configured) { '[OK]' } else { '[--]' }
        $color = if ($status.Configured) { 'Green' } else { 'DarkGray' }
        Write-Host "  $icon " -ForegroundColor $color -NoNewline
        Write-Host "$($status.Provider) (Priority $($status.Priority))" -ForegroundColor White
        Write-Host "      Key: $($status.KeyVar) = $($status.Source)" -ForegroundColor Gray
        if ($status.Configured -and $activeProvider -eq 'NONE (rule-first mode)') {
            $activeProvider = "$($status.Provider) (Priority $($status.Priority))"
        }
        if ($status.Configured -and $status.MaskedKey) {
            Write-Host "      Value: $($status.MaskedKey)" -ForegroundColor DarkYellow
        }
        foreach ($key in $status.ExtraVars.Keys) {
            $val = $status.ExtraVars[$key]
            Write-Host "      $key = $val" -ForegroundColor DarkGray
        }
        Write-Host ""
    }

    if (-not [string]::IsNullOrWhiteSpace($providerOverride)) {
        $activeProvider = if ($providerOverride -eq 'RULE_FIRST') {
            'RULE FIRST (Pinned)'
        }
        else {
            "$providerOverride (Pinned)"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($providerOverride)) {
        Write-Host "  Provider override: $providerOverride" -ForegroundColor Yellow
    }
    Write-Host "  Active provider: $activeProvider" -ForegroundColor Yellow
    Write-Host "  (First configured provider wins by priority)" -ForegroundColor DarkGray
    Write-Host ""
}

function Set-ProviderOverride {
    param([string]$ProviderName)

    $normalized = switch ($ProviderName.ToLowerInvariant()) {
        'openrouter' { 'OPENROUTER' }
        'minimax' { 'MINIMAX' }
        'openai' { 'OPENAI' }
        'anthropic' { 'ANTHROPIC' }
        default { '' }
    }

    if (-not [string]::IsNullOrWhiteSpace($normalized)) {
        [Environment]::SetEnvironmentVariable('BIM765T_LLM_PROVIDER', $normalized, 'User')
        Write-Host "  [OK] Set BIM765T_LLM_PROVIDER = $normalized" -ForegroundColor DarkGreen
    }
}

function Set-ProviderKey {
    param(
        [string]$Name,
        [string]$Key
    )
    $def = $providers[$Name]
    if ([string]::IsNullOrWhiteSpace($Key)) {
        Write-Host ""
        Write-Host "  $($def.DisplayName) - Get key at: $($def.GetKeyUrl)" -ForegroundColor Cyan
        Write-Host "  $($def.Notes)" -ForegroundColor DarkGray
        $Key = Read-Host "  Enter $($def.KeyVar) (or press Enter to skip)"
    }
    if ([string]::IsNullOrWhiteSpace($Key)) {
        Write-Host "  Skipped $($def.DisplayName)" -ForegroundColor DarkGray
        return $false
    }
    # Basic validation
    if ($def.KeyPrefix -and -not $Key.StartsWith($def.KeyPrefix)) {
        Write-Host "  WARNING: Key does not start with '$($def.KeyPrefix)'. Proceeding anyway." -ForegroundColor Yellow
    }
    # Set main key
    [Environment]::SetEnvironmentVariable($def.KeyVar, $Key, 'User')
    Set-Item -Path ("Env:{0}" -f $def.KeyVar) -Value $Key
    Write-Host "  [OK] Set $($def.KeyVar)" -ForegroundColor Green
    # Set extra vars with defaults
    foreach ($ev in $def.ExtraVars) {
        $existing = [Environment]::GetEnvironmentVariable($ev.Name, 'User')
        if ([string]::IsNullOrWhiteSpace($existing)) {
            [Environment]::SetEnvironmentVariable($ev.Name, $ev.Default, 'User')
            Write-Host "  [OK] Set $($ev.Name) = $($ev.Default)" -ForegroundColor DarkGreen
        }
        elseif ($Name -eq 'minimax' -and ($ev.Name -eq 'MINIMAX_MODEL' -or $ev.Name -eq 'MINIMAX_RESPONSE_MODEL') -and (($existing -eq 'MiniMax-M2.5') -or ($existing -eq 'MiniMax-M2.5-highspeed')) -and $ev.Default -ne $existing) {
            [Environment]::SetEnvironmentVariable($ev.Name, $ev.Default, 'User')
            Write-Host "  [OK] Upgraded $($ev.Name) = $($ev.Default)" -ForegroundColor DarkGreen
        }
        elseif ($Name -eq 'minimax' -and $ev.Name -eq 'MINIMAX_FALLBACK_MODEL' -and $existing -eq 'MiniMax-M2.5' -and $ev.Default -ne $existing) {
            [Environment]::SetEnvironmentVariable($ev.Name, $ev.Default, 'User')
            Write-Host "  [OK] Upgraded $($ev.Name) = $($ev.Default)" -ForegroundColor DarkGreen
        }
        else {
            Write-Host "  [--] $($ev.Name) already set, keeping: $existing" -ForegroundColor DarkGray
        }
    }
    return $true
}
function Remove-ProviderKeys {
    param([string]$Name)
    $def = $providers[$Name]
    [Environment]::SetEnvironmentVariable($def.KeyVar, $null, 'User')
    Write-Host "  Removed $($def.KeyVar)" -ForegroundColor Yellow
    foreach ($ev in $def.ExtraVars) {
        [Environment]::SetEnvironmentVariable($ev.Name, $null, 'User')
        Write-Host "  Removed $($ev.Name)" -ForegroundColor DarkYellow
    }
}
# ── Main logic ────────────────────────────────────────────────────────
if ($ListOnly) {
    Show-AllStatus
    exit 0
}
if ($RemoveAll) {
    Write-Host ""
    Write-Host "Removing all AI provider environment variables..." -ForegroundColor Yellow
    foreach ($name in @('openrouter', 'minimax', 'openai', 'anthropic')) {
        Remove-ProviderKeys -Name $name
    }
    [Environment]::SetEnvironmentVariable('BIM765T_LLM_PROVIDER', $null, 'User')
    Write-Host "  Removed BIM765T_LLM_PROVIDER" -ForegroundColor DarkYellow
    Write-Host ""
    Write-Host "Done. All AI provider keys removed." -ForegroundColor Green
    exit 0
}
Write-Host ""
Write-Host "=== BIM765T AI Provider Setup ===" -ForegroundColor Cyan
Write-Host "Provider priority: OpenRouter > MiniMax > OpenAI > Anthropic > Rule-first" -ForegroundColor DarkGray
Write-Host ""
$configured = 0
if ($Provider -eq 'all' -or $Provider -eq 'openrouter') {
    if (Set-ProviderKey -Name 'openrouter' -Key $OpenRouterKey) {
        $configured++
        if ($Provider -ne 'all') { Set-ProviderOverride -ProviderName 'openrouter' }
    }
}
if ($Provider -eq 'all' -or $Provider -eq 'minimax') {
    if (Set-ProviderKey -Name 'minimax' -Key $MiniMaxKey) {
        $configured++
        if ($Provider -ne 'all') { Set-ProviderOverride -ProviderName 'minimax' }
    }
}
if ($Provider -eq 'all' -or $Provider -eq 'openai') {
    if (Set-ProviderKey -Name 'openai' -Key $OpenAiKey) {
        $configured++
        if ($Provider -ne 'all') { Set-ProviderOverride -ProviderName 'openai' }
    }
}
if ($Provider -eq 'all' -or $Provider -eq 'anthropic') {
    if (Set-ProviderKey -Name 'anthropic' -Key $AnthropicKey) {
        $configured++
        if ($Provider -ne 'all') { Set-ProviderOverride -ProviderName 'anthropic' }
    }
}
Write-Host ""
if ($configured -gt 0) {
    Write-Host "Setup complete! $configured provider(s) configured." -ForegroundColor Green
    Write-Host ""
    Write-Host "IMPORTANT: Restart WorkerHost and Revit to pick up new keys." -ForegroundColor Yellow
    Write-Host "  .\tools\start_workerhost.ps1" -ForegroundColor DarkGray
}
else {
    Write-Host "No providers configured. System will use rule-first mode." -ForegroundColor Yellow
}
Write-Host ""
Show-AllStatus
