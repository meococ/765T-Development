using System;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.WorkerHost.Kernel;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class KernelPipeClientTests
{
    [Fact]
    public async Task InvokeAsync_WhenPipeIsUnavailable_Returns_RevitUnavailable_Fast()
    {
        var client = new KernelPipeClient("bim765t-missing-pipe-" + Guid.NewGuid().ToString("N"));
        var request = new KernelToolRequest
        {
            ToolName = ToolNames.WorkerMessage,
            PayloadJson = "{}",
            TimeoutMs = 30_000
        };

        var startedAt = DateTime.UtcNow;
        var result = await client.InvokeAsync(request, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startedAt;

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.RevitUnavailable, result.StatusCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Revit kernel pipe unavailable", StringComparison.Ordinal));
        Assert.True(elapsed < TimeSpan.FromSeconds(10), "Missing kernel pipe should fail fast instead of waiting for the full request timeout.");
    }
}
