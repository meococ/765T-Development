param(
    [Parameter(Mandatory=$true)]
    [string]$ApiKey,
    [switch]$InstallKit
)

$ErrorActionPreference = 'Stop'

$claudeDir = Join-Path $env:USERPROFILE '.claude'
New-Item -ItemType Directory -Force -Path $claudeDir | Out-Null
$settingsPath = Join-Path $claudeDir 'settings.json'

$settings = @{}
if (Test-Path $settingsPath) {
    try {
        $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    } catch {
        throw "settings.json hiện tại không parse được JSON: $settingsPath"
    }
}
if (-not $settings) { $settings = @{} }
if (-not $settings.ContainsKey('env') -or -not $settings.env) { $settings.env = @{} }

$settings.env.ANTHROPIC_BASE_URL = 'https://claudible.io'
$settings.env.ANTHROPIC_AUTH_TOKEN = $ApiKey
$settings.env.ANTHROPIC_DEFAULT_OPUS_MODEL = 'claude-opus-4.6'
$settings.env.ANTHROPIC_DEFAULT_SONNET_MODEL = 'claude-sonnet-4.6'
$settings.env.ANTHROPIC_DEFAULT_HAIKU_MODEL = 'claude-haiku-4.5'
$settings.disableLoginPrompt = $true

$settings | ConvertTo-Json -Depth 20 | Set-Content -Path $settingsPath -Encoding UTF8

[Environment]::SetEnvironmentVariable('ANTHROPIC_BASE_URL', 'https://claudible.io', 'User')
[Environment]::SetEnvironmentVariable('ANTHROPIC_AUTH_TOKEN', $ApiKey, 'User')
$gitBashCandidates = @(
    $env:CLAUDE_CODE_GIT_BASH_PATH,
    (Join-Path $env:LOCALAPPDATA 'Programs\Git\bin\bash.exe'),
    'C:\Program Files\Git\bin\bash.exe'
)
$gitBashPath = $gitBashCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } | Select-Object -First 1
if ($gitBashPath) {
    [Environment]::SetEnvironmentVariable('CLAUDE_CODE_GIT_BASH_PATH', $gitBashPath, 'User')
}

Write-Host "Configured Claude Code for Claudible."
Write-Host "settings.json: $settingsPath"

if ($InstallKit) {
    Write-Host 'Installing Claudible Kit V2 into current project...'
    irm 'https://claudible.io/kit-install.ps1' | iex
}
