param(
    [string]$BridgeExe = "",
    [string]$FamilyName = "Penetration Alpha",
    [switch]$CreateSchedule
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$BridgeExe = Resolve-BridgeExe -RequestedPath $BridgeExe

function Invoke-BridgeTool {
    param(
        [string]$Tool,
        [object]$Payload
    )

    $tempPayload = [System.IO.Path]::GetTempFileName() + ".json"
    try {
        $Payload | ConvertTo-Json -Depth 20 | Set-Content -Path $tempPayload -Encoding UTF8
        (& $BridgeExe $Tool --payload $tempPayload) | ConvertFrom-Json
    }
    finally {
        Remove-Item $tempPayload -Force -ErrorAction SilentlyContinue
    }
}

$inventory = Invoke-BridgeTool -Tool 'report.penetration_alpha_inventory' -Payload @{
    FamilyName = $FamilyName
    MaxResults = 5000
    IncludeAxisStatus = $true
}

$plan = Invoke-BridgeTool -Tool 'report.penetration_round_shadow_plan' -Payload @{
    SourceFamilyName = $FamilyName
    RoundFamilyName = 'Round'
    PreferredReferenceMark = 'test'
    MaxResults = 5000
}

[pscustomobject]@{
    InventoryStatus = $inventory.StatusCode
    InventoryCount  = if ($inventory.PayloadJson) { (($inventory.PayloadJson | ConvertFrom-Json).Count) } else { 0 }
    PlanStatus      = $plan.StatusCode
    CreatableCount  = if ($plan.PayloadJson) { (($plan.PayloadJson | ConvertFrom-Json).CreatableCount) } else { 0 }
} | Format-List

if ($CreateSchedule) {
    $active = (& $BridgeExe 'document.get_active') | ConvertFrom-Json
    $doc = $active.PayloadJson | ConvertFrom-Json
    $fingerprint = (& $BridgeExe 'document.get_context_fingerprint' --target-document $doc.DocumentKey) | ConvertFrom-Json
    $payload = @{
        FamilyName = $FamilyName
        ScheduleName = 'BIM765T_PenetrationAlpha_Inventory'
        OverwriteIfExists = $true
        Itemized = $true
    }

    $tempPayload = [System.IO.Path]::GetTempFileName() + ".json"
    $tempContext = [System.IO.Path]::GetTempFileName() + ".json"
    try {
        $payload | ConvertTo-Json -Depth 20 | Set-Content -Path $tempPayload -Encoding UTF8
        $fingerprint.PayloadJson | Set-Content -Path $tempContext -Encoding UTF8
        $preview = (& $BridgeExe 'schedule.create_penetration_alpha_inventory_safe' --target-document $doc.DocumentKey --dry-run true --payload $tempPayload --expected-context $tempContext) | ConvertFrom-Json
        if ($preview.ConfirmationRequired -and $preview.ApprovalToken) {
            $execute = (& $BridgeExe 'schedule.create_penetration_alpha_inventory_safe' --target-document $doc.DocumentKey --dry-run false --payload $tempPayload --expected-context $tempContext --approval-token $preview.ApprovalToken) | ConvertFrom-Json
            $execute | ConvertTo-Json -Depth 20
        }
    }
    finally {
        Remove-Item $tempPayload, $tempContext -Force -ErrorAction SilentlyContinue
    }
}
