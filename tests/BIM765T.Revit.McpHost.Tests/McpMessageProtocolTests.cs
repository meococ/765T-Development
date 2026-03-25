using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BIM765T.Revit.McpHost.Tests;

public sealed class McpMessageProtocolTests
{
    [Fact]
    public void ParseContentLength_Rejects_NonNumeric_Header()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => McpMessageProtocol.ParseContentLength("Content-Length: !!!\r\n\r\n"));

        Assert.Contains("Invalid Content-Length", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadMessageAsync_Reads_Complete_Frame()
    {
        var payload = "{\"jsonrpc\":\"2.0\"}";
        var bytes = Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(payload)}\r\n\r\n{payload}");
        await using var stream = new MemoryStream(bytes);

        var result = await McpMessageProtocol.ReadMessageAsync(stream);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task WriteMessageAsync_Writes_Protocol_Frame()
    {
        await using var stream = new MemoryStream();
        await McpMessageProtocol.WriteMessageAsync(stream, new JsonObject { ["hello"] = "world" });
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
        var raw = await reader.ReadToEndAsync();

        Assert.Contains("Content-Length:", raw, StringComparison.Ordinal);
        Assert.Contains("{\"hello\":\"world\"}", raw, StringComparison.Ordinal);
    }
}
