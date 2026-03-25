param(
    [Parameter(Mandatory = $true)]
    [string]$ContractsCoveragePath,

    [Parameter(Mandatory = $true)]
    [string]$AgentCoreCoveragePath,

    [double]$ContractsLineRate = 0.55,
    [double]$CopilotCoreLineRate = 0.68,
    [double]$AgentCoreLineRate = 0.85
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-PackageLineRate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CoveragePath,

        [Parameter(Mandatory = $true)]
        [string]$PackageName
    )

    if (-not (Test-Path -LiteralPath $CoveragePath)) {
        throw "Coverage report not found: $CoveragePath"
    }

    [xml]$xml = Get-Content -LiteralPath $CoveragePath
    $package = @($xml.coverage.packages.package) | Where-Object { $_.name -eq $PackageName } | Select-Object -First 1
    if ($null -eq $package) {
        throw "Package '$PackageName' not found in coverage report: $CoveragePath"
    }

    return [double]$package.'line-rate'
}

function Assert-Threshold {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [double]$Actual,

        [Parameter(Mandatory = $true)]
        [double]$Minimum
    )

    $actualPct = [Math]::Round($Actual * 100, 2)
    $minimumPct = [Math]::Round($Minimum * 100, 2)
    if ($Actual -lt $Minimum) {
        throw "$Label coverage gate failed. Actual=$actualPct% Minimum=$minimumPct%"
    }

    Write-Host "[coverage] $Label passed. Actual=$actualPct% Minimum=$minimumPct%"
}

$contractsRate = Get-PackageLineRate -CoveragePath $ContractsCoveragePath -PackageName "BIM765T.Revit.Contracts"
$copilotCoreRate = Get-PackageLineRate -CoveragePath $AgentCoreCoveragePath -PackageName "BIM765T.Revit.Copilot.Core"
$agentCoreRate = Get-PackageLineRate -CoveragePath $AgentCoreCoveragePath -PackageName "BIM765T.Revit.Agent.Core"

Assert-Threshold -Label "Contracts" -Actual $contractsRate -Minimum $ContractsLineRate
Assert-Threshold -Label "Copilot.Core" -Actual $copilotCoreRate -Minimum $CopilotCoreLineRate
Assert-Threshold -Label "Agent.Core" -Actual $agentCoreRate -Minimum $AgentCoreLineRate
