param(
    [string]$RevitExe = 'C:\Program Files\Autodesk\Revit 2024\Revit.exe',
    [string]$ModelPath = $env:BIM765T_DEFAULT_MODEL_PATH,
    [string]$BridgeExe = '',
    [int]$LaunchTimeoutSeconds = 180,
    [int]$BridgeTimeoutSeconds = 180,
    [switch]$AutoTrustUnsignedAddin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

if ([string]::IsNullOrWhiteSpace($BridgeExe)) {
    $BridgeExe = Resolve-BridgeExe
}

if (-not (Test-Path $RevitExe)) {
    throw "RevitExe not found: $RevitExe"
}

if ([string]::IsNullOrWhiteSpace($ModelPath)) {
    throw "ModelPath is required. Pass -ModelPath or set BIM765T_DEFAULT_MODEL_PATH."
}

if (-not (Test-Path $ModelPath)) {
    throw "ModelPath not found: $ModelPath"
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class RevitWindowInterop
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    public const uint BM_CLICK = 0x00F5;
    public const int SW_RESTORE = 9;

    public static string GetText(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static IntPtr FindVisibleTopLevelWindowByTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var text = GetText(hWnd);
            if (string.Equals(text, title, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static IntPtr FindChildWindowByTitle(IntPtr parent, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (hWnd, lParam) =>
        {
            var text = GetText(hWnd);
            if (string.Equals(text, title, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
"@

function Get-RevitProcesses {
    @(Get-Process Revit -ErrorAction SilentlyContinue)
}

function Stop-RevitProcesses {
    foreach ($proc in Get-RevitProcesses) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-TrustUnsignedAddinIfPrompted {
    param(
        [int]$TimeoutSeconds = 60,
        [switch]$AllowAutoTrust
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $dialog = [RevitWindowInterop]::FindVisibleTopLevelWindowByTitle('Security - Unsigned Add-In')
        if ($dialog -ne [IntPtr]::Zero) {
            if (-not $AllowAutoTrust) {
                throw 'Unsigned add-in trust prompt detected. Re-run with -AutoTrustUnsignedAddin only if you explicitly accept the risk, or sign the add-in first.'
            }

            [RevitWindowInterop]::ShowWindowAsync($dialog, [RevitWindowInterop]::SW_RESTORE) | Out-Null
            [RevitWindowInterop]::SetForegroundWindow($dialog) | Out-Null
            Start-Sleep -Milliseconds 250

            $alwaysLoadButton = [RevitWindowInterop]::FindChildWindowByTitle($dialog, 'Always Load')
            if ($alwaysLoadButton -eq [IntPtr]::Zero) {
                throw 'Always Load button not found in Security - Unsigned Add-In dialog.'
            }

            [RevitWindowInterop]::SendMessage($alwaysLoadButton, [RevitWindowInterop]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Wait-ForBridgeDocument {
    param([int]$TimeoutSeconds = 180)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $raw = & $BridgeExe document.get_active
            if ($LASTEXITCODE -eq 0 -and $raw) {
                $response = $raw | ConvertFrom-Json
                if ($response.Succeeded) {
                    return $response
                }
            }
        }
        catch {
            # keep waiting
        }

        Start-Sleep -Seconds 2
    }

    throw "Bridge did not come online with an active document after $TimeoutSeconds seconds."
}

Stop-RevitProcesses
Start-Sleep -Seconds 3
Start-Process -FilePath $RevitExe -ArgumentList ('"' + $ModelPath + '"')

$trusted = Invoke-TrustUnsignedAddinIfPrompted -TimeoutSeconds $LaunchTimeoutSeconds -AllowAutoTrust:$AutoTrustUnsignedAddin
$documentResponse = Wait-ForBridgeDocument -TimeoutSeconds $BridgeTimeoutSeconds
$document = $documentResponse.PayloadJson | ConvertFrom-Json

[pscustomobject]@{
    RevitExe = $RevitExe
    ModelPath = $ModelPath
    TrustedUnsignedAddinPrompt = [bool]$trusted
    DocumentTitle = [string]$document.Title
    DocumentKey = [string]$document.DocumentKey
    BridgeOnline = $true
} | ConvertTo-Json -Depth 8
