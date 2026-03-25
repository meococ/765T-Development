param(
    [string]$CatalogJson = ".\docs\generated\revit-tool-catalog.json",
    [string]$WorkflowPath = ".\revit-agent-workflow.html",
    [switch]$GenerateIfMissing,
    [string]$BridgeExe = ""
)

$startMarker = '<!-- AUTO-GENERATED: TOOL_CATALOG_START -->'
$endMarker = '<!-- AUTO-GENERATED: TOOL_CATALOG_END -->'

if ($GenerateIfMissing -or -not (Test-Path $CatalogJson)) {
    if ([string]::IsNullOrWhiteSpace($BridgeExe)) {
        powershell -ExecutionPolicy Bypass -File ".\tools\generate_tool_catalog.ps1" | Out-Null
    }
    else {
        powershell -ExecutionPolicy Bypass -File ".\tools\generate_tool_catalog.ps1" -BridgeExe $BridgeExe | Out-Null
    }
}

if (-not (Test-Path $CatalogJson)) {
    throw "Catalog JSON not found: $CatalogJson"
}

if (-not (Test-Path $WorkflowPath)) {
    throw "Workflow HTML not found: $WorkflowPath"
}

$catalog = Get-Content -Path $CatalogJson -Raw -Encoding UTF8 | ConvertFrom-Json
$html = Get-Content -Path $WorkflowPath -Raw -Encoding UTF8

function Encode-Html {
    param([string]$Text)
    if ($null -eq $Text) {
        return [System.Net.WebUtility]::HtmlEncode('')
    }

    return [System.Net.WebUtility]::HtmlEncode($Text)
}

function Get-PermissionTagClass {
    param([string]$PermissionName)
    switch ($PermissionName) {
        "Read" { return "tag-blue" }
        "Review" { return "tag-green" }
        "Mutate" { return "tag-yellow" }
        "FileLifecycle" { return "tag-red" }
        default { return "tag-purple" }
    }
}

function Get-GroupCard {
    param($Group)

    $toolTags = @()
    foreach ($tool in $Group.Tools) {
        $tagClass = Get-PermissionTagClass -PermissionName $tool.PermissionName
        $suffix = if (-not $tool.Enabled) { " (off)" } elseif ($tool.SupportsDryRun) { " (dry-run)" } else { "" }
        $toolTags += "<span class=""tag $tagClass"">$(Encode-Html($tool.ToolName + $suffix))</span>"
    }

    return @"
    <div class="card">
      <div class="card-header">
        <div class="card-icon" style="background:rgba(0,212,255,0.1)">TG</div>
        <div class="card-title">$(Encode-Html($Group.Name)) - $($Group.Count)</div>
      </div>
      <div class="card-body">
        <p class="subtitle" style="margin-bottom:12px">Enabled $($Group.EnabledCount) / $($Group.Count) - DryRun $($Group.DryRunCount)</p>
        <div class="tag-list">
          $($toolTags -join "`n          ")
        </div>
      </div>
    </div>
"@
}

$summaryCards = @"
  <div class="grid3">
    <div class="card">
      <div class="card-header">
        <div class="card-icon" style="background:rgba(0,212,255,0.1)">TL</div>
        <div class="card-title">Tool Summary</div>
      </div>
      <div class="card-body">
        <div class="tag-list">
          <span class="tag tag-blue">Total: $($catalog.Stats.TotalTools)</span>
          <span class="tag tag-green">Enabled: $($catalog.Stats.EnabledTools)</span>
          <span class="tag tag-red">Disabled: $($catalog.Stats.DisabledTools)</span>
          <span class="tag tag-yellow">DryRun: $($catalog.Stats.DryRunTools)</span>
        </div>
      </div>
    </div>
    <div class="card">
      <div class="card-header">
        <div class="card-icon" style="background:rgba(16,185,129,0.1)">RT</div>
        <div class="card-title">Runtime Capability</div>
      </div>
      <div class="card-body">
        <div class="tag-list">
          <span class="tag tag-green">Write: $($catalog.Capabilities.AllowWriteTools)</span>
          <span class="tag tag-yellow">Save: $($catalog.Capabilities.AllowSaveTools)</span>
          <span class="tag tag-red">Sync: $($catalog.Capabilities.AllowSyncTools)</span>
          <span class="tag tag-purple">MCP: $($catalog.Capabilities.SupportsMcpHost)</span>
        </div>
      </div>
    </div>
    <div class="card">
      <div class="card-header">
        <div class="card-icon" style="background:rgba(124,58,237,0.1)">SN</div>
        <div class="card-title">Catalog Snapshot</div>
      </div>
      <div class="card-body">
        <p class="subtitle" style="margin-bottom:8px">Auto-sync from bridge runtime</p>
        <div class="tag-list">
          <span class="tag tag-purple">Revit: $(Encode-Html($catalog.RevitYear))</span>
          <span class="tag tag-blue">Source: $(Encode-Html($catalog.Source))</span>
        </div>
        <p class="subtitle" style="margin-top:10px">Generated at: $(Encode-Html($catalog.GeneratedAtUtc))</p>
      </div>
    </div>
  </div>
"@

$legendCard = @"
  <div class="card">
    <div class="card-header">
      <div class="card-icon" style="background:rgba(245,158,11,0.1)">LG</div>
      <div class="card-title">Legend</div>
    </div>
    <div class="card-body">
      <div class="tag-list">
        <span class="tag tag-blue">Read</span>
        <span class="tag tag-green">Review</span>
        <span class="tag tag-yellow">Mutate</span>
        <span class="tag tag-red">FileLifecycle</span>
        <span class="tag tag-purple">Other</span>
      </div>
      <p class="subtitle" style="margin-top:10px">Generated block nay duoc dong bo tu bridge runtime, khong phai phan roadmap viet tay.</p>
    </div>
  </div>
"@

$groupCards = @($catalog.Groups | ForEach-Object { Get-GroupCard -Group $_ }) -join "`n"

$generatedBlock = @"
$startMarker
  <hr class="divider">

  <div class="section-label">03B - Generated Tool Catalog</div>
  <h2>Tool catalog synced from bridge runtime</h2>
  <p class="subtitle">Data source: <code>session.list_tools</code> + <code>session.get_capabilities</code></p>

$summaryCards

  <div class="grid3">
$legendCard
$groupCards
  </div>
$endMarker
"@

if ($html.Contains($startMarker) -and $html.Contains($endMarker)) {
    $pattern = [regex]::Escape($startMarker) + '.*?' + [regex]::Escape($endMarker)
    $html = [regex]::Replace($html, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $generatedBlock }, [System.Text.RegularExpressions.RegexOptions]::Singleline)
}
else {
    $insertBefore = '<!-- SECTION 4: REVIT API LAYERS -->'
    if (-not $html.Contains($insertBefore)) {
        throw "Khong tim thay marker SECTION 4 trong workflow HTML de insert generated catalog."
    }

    $html = $html.Replace($insertBefore, "$generatedBlock`r`n`r`n  $insertBefore")
}

Set-Content -Path $WorkflowPath -Value $html -Encoding UTF8

[pscustomobject]@{
    WorkflowPath = (Resolve-Path $WorkflowPath).Path
    CatalogJson = (Resolve-Path $CatalogJson).Path
    TotalTools = $catalog.Stats.TotalTools
    GroupCount = @($catalog.Groups).Count
} | ConvertTo-Json -Compress
