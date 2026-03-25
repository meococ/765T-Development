param(
    [string]$McpExe = ""
)
. (Join-Path $PSScriptRoot 'Assistant.Common.ps1')

$McpExe = Resolve-McpExe -RequestedPath $McpExe

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $McpExe
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8

$process = [System.Diagnostics.Process]::Start($psi)

function Send-McpMessage {
    param(
        [System.Diagnostics.Process]$Process,
        [object]$Payload
    )

    $json = $Payload | ConvertTo-Json -Depth 30 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $header = "Content-Length: $($bytes.Length)`r`n`r`n"
    $Process.StandardInput.Write($header)
    $Process.StandardInput.Write($json)
    $Process.StandardInput.Flush()
}

function Read-McpMessage {
    param(
        [System.Diagnostics.Process]$Process
    )

    $stream = $Process.StandardOutput.BaseStream
    $headers = @()
    while ($true) {
        $line = Read-AsciiLine -Stream $stream
        if ($null -eq $line) { throw 'EOF while reading MCP header.' }
        if ($line -eq '') { break }
        $headers += $line
    }

    $lengthHeader = $headers | Where-Object { $_ -like 'Content-Length:*' } | Select-Object -First 1
    if (-not $lengthHeader) { throw 'Missing Content-Length header.' }
    $length = [int](($lengthHeader -split ':', 2)[1].Trim())

    $buffer = New-Object byte[] $length
    $offset = 0
    while ($offset -lt $length) {
        $chunk = $stream.Read($buffer, $offset, $length - $offset)
        if ($chunk -le 0) { throw 'EOF while reading MCP body.' }
        $offset += $chunk
    }

    return ([System.Text.Encoding]::UTF8.GetString($buffer)) | ConvertFrom-Json
}

function Read-AsciiLine {
    param(
        [System.IO.Stream]$Stream
    )

    $bytes = New-Object System.Collections.Generic.List[byte]
    while ($true) {
        $value = $Stream.ReadByte()
        if ($value -lt 0) {
            if ($bytes.Count -eq 0) { return $null }
            throw 'EOF while reading line.'
        }

        if ($value -eq 10) { break }
        if ($value -ne 13) {
            $bytes.Add([byte]$value) | Out-Null
        }
    }

    return [System.Text.Encoding]::ASCII.GetString($bytes.ToArray())
}

function Wait-McpResponse {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$RequestId
    )

    while ($true) {
        $message = Read-McpMessage -Process $Process
        if ($null -ne $message.id -and [string]$message.id -eq [string]$RequestId) {
            return $message
        }
    }
}

try {
    Send-McpMessage -Process $process -Payload @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-06-18'
            capabilities = @{}
            clientInfo = @{
                name = 'BIM765T-MCP-Smoke'
                version = '1.0'
            }
        }
    }
    $initialize = Wait-McpResponse -Process $process -RequestId 1

    Send-McpMessage -Process $process -Payload @{
        jsonrpc = '2.0'
        id = 2
        method = 'tools/list'
        params = @{}
    }
    $tools = Wait-McpResponse -Process $process -RequestId 2

    Send-McpMessage -Process $process -Payload @{
        jsonrpc = '2.0'
        id = 3
        method = 'tools/call'
        params = @{
            name = 'document.get_active'
            arguments = @{}
        }
    }
    $call = Wait-McpResponse -Process $process -RequestId 3

    [pscustomobject]@{
        McpExe = $McpExe
        InitializeOk = ($null -ne $initialize.result)
        ToolsCount = $tools.result.tools.Count
        ToolCallOk = (-not $call.result.isError)
        ToolCallStatus = $call.result.structuredContent.statusCode
    } | ConvertTo-Json -Compress
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill()
    }
}
