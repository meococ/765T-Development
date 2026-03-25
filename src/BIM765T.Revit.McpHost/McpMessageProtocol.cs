using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.McpHost;

internal static class McpMessageProtocol
{
    private static readonly byte[] HeaderSeparator = { 13, 10, 13, 10 };
    private const int MaxHeaderBytes = 16 * 1024;
    internal static readonly TimeSpan DefaultBridgeResponseTimeout = TimeSpan.FromSeconds(BridgeConstants.DefaultRequestTimeoutSeconds);

    internal static async Task<string?> ReadBridgeResponseLineAsync(StreamReader reader, TimeSpan timeout)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        var readTask = reader.ReadLineAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != readTask)
        {
            throw new TimeoutException($"Timed out waiting for bridge response after {timeout.TotalSeconds:F0}s.");
        }

        return await readTask.ConfigureAwait(false);
    }

    internal static async Task<string?> ReadMessageAsync(Stream input, CancellationToken cancellationToken = default)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[BridgeConstants.PipeBufferSize];
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await input.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                if (buffer.Length == 0)
                {
                    return null;
                }

                throw new EndOfStreamException("Unexpected EOF while reading MCP headers.");
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxHeaderBytes)
            {
                throw new InvalidOperationException($"MCP header exceeds maximum allowed size of {MaxHeaderBytes} bytes.");
            }

            headerEnd = IndexOf(buffer.GetBuffer(), checked((int)buffer.Length), HeaderSeparator);
        }

        var accumulated = buffer.ToArray();
        var headerLength = headerEnd + HeaderSeparator.Length;
        var headerText = Encoding.ASCII.GetString(accumulated, 0, headerLength);
        var contentLength = ParseContentLength(headerText);

        var bodyBytesAlreadyBuffered = accumulated.Length - headerLength;
        if (bodyBytesAlreadyBuffered < 0)
        {
            bodyBytesAlreadyBuffered = 0;
        }

        var body = new byte[contentLength];
        if (bodyBytesAlreadyBuffered > 0)
        {
            Array.Copy(accumulated, headerLength, body, 0, Math.Min(bodyBytesAlreadyBuffered, contentLength));
        }

        var offset = Math.Min(bodyBytesAlreadyBuffered, contentLength);
        while (offset < contentLength)
        {
            var read = await input.ReadAsync(body.AsMemory(offset, contentLength - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected EOF while reading MCP body.");
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(body);
    }

    internal static int ParseContentLength(string headerText)
    {
        if (string.IsNullOrWhiteSpace(headerText))
        {
            throw new InvalidOperationException("MCP header is empty.");
        }

        var rawLength = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Substring("Content-Length:".Length).Trim())
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(rawLength))
        {
            throw new InvalidOperationException("Missing Content-Length header.");
        }

        if (!int.TryParse(rawLength, out var contentLength))
        {
            throw new InvalidOperationException($"Invalid Content-Length header value: {rawLength}");
        }

        if (contentLength <= 0)
        {
            throw new InvalidOperationException("Content-Length must be greater than zero.");
        }

        if (contentLength > BridgeConstants.MaxMcpPayloadBytes)
        {
            throw new InvalidOperationException($"Content-Length {contentLength} exceeds maximum allowed {BridgeConstants.MaxMcpPayloadBytes} bytes.");
        }

        return contentLength;
    }

    internal static async Task WriteMessageAsync(Stream output, JsonObject payload, CancellationToken cancellationToken = default)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (payload == null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var json = payload.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await output.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int IndexOf(byte[] source, int length, byte[] value)
    {
        for (var i = 0; i <= length - value.Length; i++)
        {
            var match = true;
            for (var j = 0; j < value.Length; j++)
            {
                if (source[i + j] != value[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
