using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.McpHost.Tests;

public sealed class McpToolCatalogLoaderTests
{
    [Fact]
    public void ParseOrThrow_Uses_ServerOwned_Catalog_Without_ClientFiltering()
    {
        var response = new ToolResponseEnvelope
        {
            Succeeded = true,
            StatusCode = StatusCodes.Ok,
            PayloadJson = JsonUtil.Serialize(new ToolCatalogResponse
            {
                Tools = new List<ToolManifest>
                {
                    new ToolManifest
                    {
                        ToolName = "internal.connector.tool",
                        Description = "Server-owned test catalog item.",
                        Audience = WorkerAudience.Internal,
                        Visibility = WorkerVisibility.Visible
                    }
                }
            })
        };

        var catalog = McpToolCatalogLoader.ParseOrThrow(response);

        var tool = Assert.Single(catalog.Tools);
        Assert.Equal("internal.connector.tool", tool.ToolName);
    }

    [Fact]
    public void ParseOrThrow_Fails_When_Payload_Is_Missing()
    {
        var response = new ToolResponseEnvelope
        {
            Succeeded = true,
            StatusCode = StatusCodes.Ok,
            PayloadJson = string.Empty
        };

        var ex = Assert.Throws<InvalidOperationException>(() => McpToolCatalogLoader.ParseOrThrow(response));
        Assert.Contains("tool catalog", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
