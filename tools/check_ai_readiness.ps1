<#

.SYNOPSIS

    Check full AI stack readiness for BIM765T Revit Agent.

.DESCRIPTION

    Validates all components needed for end-to-end AI chat in Revit:

    environment variables, WorkerHost, Revit, named pipes, HTTP endpoints.

.PARAMETER AsJson

    Output results as JSON instead of formatted console.

.PARAMETER WorkerHostUrl

    Base URL of WorkerHost HTTP API. Default: http://localhost:50765

.EXAMPLE

    .\check_ai_readiness.ps1

    .\check_ai_readiness.ps1 -AsJson

#>

param(

    [switch]$AsJson,

    [string]$WorkerHostUrl = 'http://127.0.0.1:50765'

)



$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')



$checks = @{}

$overallReady = $true



function Add-Check {

    param(

        [string]$Name,

        [bool]$Passed,

        [string]$Detail = '',

        [string]$Fix = ''

    )



    $script:checks[$Name] = [pscustomobject]@{

        Name   = $Name

        Passed = $Passed

        Detail = $Detail

        Fix    = $Fix

    }



    if (-not $Passed) {

        $script:overallReady = $false

    }

}



# 1. AI Provider Environment Variables

$providerVars = @{

    'OPENROUTER_API_KEY'    = 'OpenRouter'

    'MINIMAX_API_KEY'       = 'MiniMax'

    'MINIMAX_AUTH_TOKEN'    = 'MiniMax'

    'OPENAI_API_KEY'        = 'OpenAI'

    'OPENAI_AUTH_TOKEN'     = 'OpenAI'

    'ANTHROPIC_API_KEY'     = 'Anthropic'

    'ANTHROPIC_AUTH_TOKEN'  = 'Anthropic'

}



$configuredProviders = foreach ($entry in $providerVars.GetEnumerator()) {

    $secret = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')

    if ([string]::IsNullOrWhiteSpace($secret)) {

        $secret = [Environment]::GetEnvironmentVariable($entry.Key, 'User')

    }

    if ([string]::IsNullOrWhiteSpace($secret)) {

        $secret = [Environment]::GetEnvironmentVariable($entry.Key, 'Machine')

    }

    if (-not [string]::IsNullOrWhiteSpace($secret)) {

        $entry.Value

    }

}

$configuredProviders = @($configuredProviders | Sort-Object -Unique)

$providerOverride = [Environment]::GetEnvironmentVariable('BIM765T_LLM_PROVIDER', 'User')

if ([string]::IsNullOrWhiteSpace($providerOverride)) {

    $providerOverride = [Environment]::GetEnvironmentVariable('BIM765T_LLM_PROVIDER', 'Process')

}

if ([string]::IsNullOrWhiteSpace($providerOverride)) {

    $providerOverride = ''

}

else {

    $providerOverride = $providerOverride.Trim().ToUpperInvariant()

}



$hasAnyProvider = $configuredProviders.Count -gt 0

Add-Check -Name 'AI Provider Keys' `
    -Passed $hasAnyProvider `
    -Detail $(if ($hasAnyProvider) { "Configured: $($configuredProviders -join ', ')" } else { 'No AI provider keys found' }) `
    -Fix 'Run: .\tools\setup_ai_providers.ps1'



$overrideLabel = switch ($providerOverride) {

    'OPENROUTER' { 'OpenRouter' }

    'MINIMAX' { 'MiniMax' }

    'OPENAI' { 'OpenAI' }

    'ANTHROPIC' { 'Anthropic' }

    'RULE_FIRST' { 'Rule-first' }

    default { '' }

}



if (-not [string]::IsNullOrWhiteSpace($providerOverride)) {

    $overridePassed = $providerOverride -eq 'RULE_FIRST' -or ($overrideLabel -and $configuredProviders -contains $overrideLabel)

    Add-Check -Name 'Provider Override' `
    -Passed $overridePassed `
    -Detail $(if ($overridePassed) { "Pinned to: $providerOverride" } else { "Pinned to $providerOverride but matching provider key not found" }) `
    -Fix 'Run: .\tools\setup_ai_providers.ps1 -Provider minimax'

}

elseif ($configuredProviders.Count -gt 1) {

    Add-Check -Name 'Provider Override' `
    -Passed $false `
    -Detail "Multiple provider keys detected without BIM765T_LLM_PROVIDER: $($configuredProviders -join ', ')" `
    -Fix 'Set BIM765T_LLM_PROVIDER to the intended provider (for MiniMax: MINIMAX)'

}



# 2. WorkerHost Process

$workerHostProcess = Get-Process -Name 'BIM765T.Revit.WorkerHost' -ErrorAction SilentlyContinue

$workerHostRunning = $null -ne $workerHostProcess

Add-Check -Name 'WorkerHost Process' `
    -Passed $workerHostRunning `
    -Detail $(if ($workerHostRunning) { "PID: $($workerHostProcess.Id)" } else { 'Not running' }) `
    -Fix 'Run: .\tools\start_workerhost.ps1'



# 3. Revit Process

$revitProcess = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue

$revitRunning = $null -ne $revitProcess

Add-Check -Name 'Revit Process' `
    -Passed $revitRunning `
    -Detail $(if ($revitRunning) { "PID: $($revitProcess.Id)" } else { 'Not running' }) `
    -Fix 'Start Autodesk Revit 2024'



# 4. WorkerHost HTTP Health

$httpHealthOk = $false

$httpDetail = 'Not reachable'

$statusResponse = $null



if ($workerHostRunning) {

    try {

        $statusUrl = "$WorkerHostUrl/api/external-ai/status"

        $response = Invoke-RestMethod -Uri $statusUrl -Method Get -TimeoutSec 15 -ErrorAction Stop

        $statusResponse = $response

        $httpHealthOk = [bool]$response.Health.Ready -or [bool]$response.Health.StandaloneChatReady

        $modeLabel = if ([bool]$response.Health.LiveRevitReady) {
            'standalone+live-revit'
        }
        elseif ([bool]$response.Health.StandaloneChatReady) {
            'standalone-only'
        }
        else {
            'not-ready'
        }

        $summary = if (-not [string]::IsNullOrWhiteSpace($response.Health.ReadinessSummary)) {
            $response.Health.ReadinessSummary
        }
        else {
            'No readiness summary returned'
        }

        $httpDetail = "OK - Mode: $modeLabel, Provider: $($response.ConfiguredProvider), Reasoning: $($response.ReasoningMode), Summary: $summary"

    }

    catch {

        $httpDetail = "Error: $($_.Exception.Message)"

    }

}

if ($statusResponse) {
    Add-Check -Name 'WorkerHost Readiness Mode' `
        -Passed ([bool]$statusResponse.Health.StandaloneChatReady) `
        -Detail $(if ([bool]$statusResponse.Health.LiveRevitReady) {
            "Standalone chat + live Revit ready. $($statusResponse.Health.ReadinessSummary)"
        }
        elseif ([bool]$statusResponse.Health.StandaloneChatReady) {
            "Standalone chat ready, live Revit unavailable. $($statusResponse.Health.ReadinessSummary)"
        }
        else {
            "WorkerHost status returned but readiness is false. $($statusResponse.Health.ReadinessSummary)"
        }) `
        -Fix 'Run .\tools\start_workerhost.ps1 and, for live work, open Revit with an active project'
}

if ($statusResponse) {
    Add-Check -Name 'Canonical Runtime Topology' `
        -Passed ([string]$statusResponse.Health.RuntimeTopology -eq 'workerhost_public_control_plane + revit_private_kernel') `
        -Detail $(if (-not [string]::IsNullOrWhiteSpace($statusResponse.Health.RuntimeTopology)) { $statusResponse.Health.RuntimeTopology } else { 'Runtime topology not reported' }) `
        -Fix 'WorkerHost status should report WorkerHost as canonical public ingress and Revit kernel as private execution lane'
}

if ($statusResponse -and $statusResponse.Health.Diagnostics) {
    $bridgeFallbackDetail = @($statusResponse.Health.Diagnostics | Where-Object { $_ -like 'bridge_fallbacks_present:*' })
    Add-Check -Name 'Bridge Fallback Policy' `
        -Passed ($bridgeFallbackDetail.Count -gt 0) `
        -Detail $(if ($bridgeFallbackDetail.Count -gt 0) { $bridgeFallbackDetail -join ' | ' } else { 'No bridge fallback policy diagnostic found' }) `
        -Fix 'Health diagnostics should explicitly describe bridge fallback lanes as transitional/legacy rather than canonical'
}

if ($statusResponse) {
    $overallReady = $overallReady -and [bool]$statusResponse.Health.StandaloneChatReady
}

if ($statusResponse) {
    $liveWorkReady = [bool]$statusResponse.Health.LiveRevitReady
}
else {
    $liveWorkReady = $false
}

if ($statusResponse) {
    Add-Check -Name 'Live Revit Execution' `
        -Passed $liveWorkReady `
        -Detail $(if ($liveWorkReady) { 'Live Revit execution path is reachable.' } else { 'Live Revit execution path is not ready yet.' }) `
        -Fix 'Open Revit and a project to activate the private kernel lane for live execution'
}

if (-not $statusResponse) {
    $liveWorkReady = $false
}

if (-not $statusResponse) {
    $overallReady = $false
}

if (-not $statusResponse) {
    Add-Check -Name 'WorkerHost Readiness Mode' `
        -Passed $false `
        -Detail 'No WorkerHost status available' `
        -Fix 'Run .\tools\start_workerhost.ps1'
}

if (-not $statusResponse) {
    Add-Check -Name 'Canonical Runtime Topology' `
        -Passed $false `
        -Detail 'No WorkerHost status available' `
        -Fix 'Start WorkerHost so the canonical topology status can be queried'
}

if (-not $statusResponse) {
    Add-Check -Name 'Bridge Fallback Policy' `
        -Passed $false `
        -Detail 'No WorkerHost status available' `
        -Fix 'Start WorkerHost to inspect bridge fallback diagnostics'
}

if (-not $statusResponse) {
    Add-Check -Name 'Live Revit Execution' `
        -Passed $false `
        -Detail 'No WorkerHost status available' `
        -Fix 'Start WorkerHost, then open Revit and a project for live execution'
}

if ($statusResponse) {
    $resultMode = if ($statusResponse.Health.LiveRevitReady) { 'standalone+live-revit' } elseif ($statusResponse.Health.StandaloneChatReady) { 'standalone-only' } else { 'not-ready' }
}
else {
    $resultMode = 'not-ready'
}

$script:readinessMode = $resultMode

$script:liveWorkReady = $liveWorkReady

$script:statusSummary = if ($statusResponse) { $statusResponse.Health.ReadinessSummary } else { '' }

$script:runtimeTopology = if ($statusResponse) { $statusResponse.Health.RuntimeTopology } else { '' }

$script:workerHostHttpReady = $httpHealthOk

$script:workerHostStandaloneReady = if ($statusResponse) { [bool]$statusResponse.Health.StandaloneChatReady } else { $false }

$script:workerHostLiveReady = if ($statusResponse) { [bool]$statusResponse.Health.LiveRevitReady } else { $false }

$script:workerHostSupportsTaskRuntime = if ($statusResponse) { [bool]$statusResponse.SupportsTaskRuntime } else { $false }

$script:workerHostDiagnostics = if ($statusResponse) { @($statusResponse.Health.Diagnostics) } else { @() }

$script:workerHostWarnings = if ($statusResponse) { @($statusResponse.StatusWarnings) } else { @() }

$script:workerHostRestartRequired = if ($statusResponse) { [bool]$statusResponse.RestartRequired } else { $false }

$script:overallReady = $overallReady

$script:workerHostStatusAvailable = $null -ne $statusResponse

$script:workerHostReadinessSummary = $script:statusSummary

$script:workerHostRuntimeTopology = $script:runtimeTopology

$script:workerHostMode = $script:readinessMode

$script:workerHostCanonicalIngress = 'workerhost_public_control_plane'

$script:workerHostPrivateKernelLane = 'revit_private_kernel'

$script:workerHostBridgeFallbackPolicy = if ($statusResponse) { 'transitional_or_legacy_only' } else { '' }

$script:workerHostReadinessVersion = 'phase1'


Add-Check -Name 'WorkerHost HTTP API' `
    -Passed $httpHealthOk `
    -Detail $httpDetail `
    -Fix 'Ensure WorkerHost is running and port 50765 is not blocked'



# 5. LLM Provider Active

$llmActive = $false

$llmDetail = 'No LLM configured (rule-first mode)'



if ($statusResponse) {

    $llmActive = $statusResponse.ReasoningMode -ne 'rule_first'

    if ($llmActive) {

        $llmDetail = "$($statusResponse.ConfiguredProvider) - $($statusResponse.PlannerModel)"

    }

    elseif ($statusResponse.ConfiguredProvider -and $statusResponse.ConfiguredProvider -ne 'RULE FIRST') {

        $llmActive = $true

        $llmDetail = "$($statusResponse.ConfiguredProvider) detected"

    }

}



Add-Check -Name 'LLM Provider Active' `
    -Passed $llmActive `
    -Detail $llmDetail `
    -Fix 'Set API key env var then restart WorkerHost: .\tools\setup_ai_providers.ps1'



# 6. Revit Runtime Sync

$runtimeSyncOk = $true

$runtimeSyncDetail = 'WorkerHost and Revit runtime look aligned'



if ($statusResponse) {

    $restartRequired = [bool]$statusResponse.RestartRequired

    $statusWarnings = @($statusResponse.StatusWarnings | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($restartRequired) {

        $runtimeSyncOk = $false

        if ($statusWarnings.Count -gt 0) {

            $runtimeSyncDetail = $statusWarnings -join ' | '

        }

        elseif (-not [string]::IsNullOrWhiteSpace($statusResponse.RuntimePlannerModel)) {

            $runtimeSyncDetail = "WorkerHost=$($statusResponse.ConfiguredProvider)/$($statusResponse.PlannerModel) vs Revit=$($statusResponse.RuntimeConfiguredProvider)/$($statusResponse.RuntimePlannerModel)"

        }

        else {

            $runtimeSyncDetail = 'Revit runtime is stale. Restart Revit to load the latest 765T add-in.'

        }

    }

    elseif (-not [string]::IsNullOrWhiteSpace($statusResponse.RuntimePlannerModel)) {

        $runtimeSyncDetail = "Revit runtime: $($statusResponse.RuntimeConfiguredProvider) / $($statusResponse.RuntimePlannerModel)"

    }

}



Add-Check -Name 'Revit Runtime Sync' `
    -Passed $runtimeSyncOk `
    -Detail $runtimeSyncDetail `
    -Fix 'Restart Revit after deploying the latest 765T add-in build'



# 7. Named Pipe (Kernel)

$kernelPipeOk = $false

$kernelPipeDetail = 'Cannot check without WorkerHost'



if ($workerHostRunning) {

    try {

        $healthExe = $null

        try {

            $healthExe = Resolve-WorkerHostExe

        }

        catch {

        }



        if ($healthExe) {

            $healthRaw = & $healthExe --health-json 2>$null

            if ($healthRaw) {

                $health = $healthRaw | ConvertFrom-Json

                $kernelPipeOk = [bool]$health.Kernel.Reachable

                $kernelPipeDetail = if ($kernelPipeOk) {

                    "Kernel pipe reachable (pipe: $($health.KernelPipeName))"

                }

                else {

                    "Kernel pipe NOT reachable - Revit add-in may not be loaded"

                }

            }

        }

        else {

            $kernelPipeDetail = 'WorkerHost exe not found for health check'

        }

    }

    catch {

        $kernelPipeDetail = "Health check error: $($_.Exception.Message)"

    }

}



Add-Check -Name 'Kernel Named Pipe' `
    -Passed $kernelPipeOk `
    -Detail $kernelPipeDetail `
    -Fix 'Open a project in Revit to activate the 765T add-in kernel'



# 8. Qdrant (Optional)

$qdrantDetail = 'Not running (optional - system falls back to lexical search)'



try {

    $qdrantResponse = Invoke-RestMethod -Uri 'http://127.0.0.1:6333/collections' -Method Get -TimeoutSec 3 -ErrorAction Stop

    $collectionCount = if ($qdrantResponse.result) { $qdrantResponse.result.collections.Count } else { 0 }

    $qdrantDetail = "Running - $collectionCount collection(s)"

}

catch {

    # Qdrant is optional.

}



Add-Check -Name 'Qdrant (Optional)' `
    -Passed $true `
    -Detail $qdrantDetail `
    -Fix 'Run: .\tools\start_qdrant_local.ps1 (optional, not required)'



$result = [pscustomobject]@{

    Timestamp           = [DateTime]::UtcNow.ToString('O')

    OverallReady        = $overallReady

    ConfiguredProviders = $configuredProviders

    Checks              = $checks.Values | Sort-Object { $_.Passed } -Descending

    WorkerHostUrl       = $WorkerHostUrl

    StatusResponse      = $statusResponse

}



if ($AsJson) {

    $result | ConvertTo-Json -Depth 10

}

else {

    Write-Host ''

    Write-Host '=== BIM765T AI Readiness Check ===' -ForegroundColor Cyan

    Write-Host "Timestamp: $($result.Timestamp)" -ForegroundColor DarkGray

    Write-Host ''



    foreach ($check in $result.Checks) {

        $icon = if ($check.Passed) { '[OK]' } else { '[!!]' }

        $color = if ($check.Passed) { 'Green' } else { 'Red' }



        Write-Host "  $icon " -ForegroundColor $color -NoNewline

        Write-Host "$($check.Name)" -ForegroundColor White

        Write-Host "      $($check.Detail)" -ForegroundColor Gray



        if (-not $check.Passed -and $check.Fix) {

            Write-Host "      Fix: $($check.Fix)" -ForegroundColor Yellow

        }



        Write-Host ''

    }



    $overallIcon = if ($overallReady) { 'READY' } else { 'NOT READY' }

    $overallColor = if ($overallReady) { 'Green' } else { 'Red' }

    Write-Host "  Overall: $overallIcon" -ForegroundColor $overallColor

    Write-Host ''



    if (-not $overallReady) {

        Write-Host '  Quick start:' -ForegroundColor Yellow

        Write-Host '    1. .\tools\setup_ai_providers.ps1        # Set API keys' -ForegroundColor DarkGray
        Write-Host '    2. Open/Re-open 765T pane in Revit         # WorkerHost will auto-start hidden' -ForegroundColor DarkGray
        Write-Host '    3. Open Revit 2024 with a project         # Activate kernel' -ForegroundColor DarkGray
        Write-Host '    4. Restart Revit if runtime is stale      # Load latest add-in' -ForegroundColor DarkGray
        Write-Host '    5. .\tools\start_workerhost.ps1           # Optional manual diagnostic fallback' -ForegroundColor DarkGray
        Write-Host '    6. .\tools\check_ai_readiness.ps1         # Re-check' -ForegroundColor DarkGray


        Write-Host ''

    }

}



exit $(if ($overallReady) { 0 } else { 1 })

