using System;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.McpHost;

internal static class McpToolCatalogLoader
{
    internal static ToolCatalogResponse ParseOrThrow(ToolResponseEnvelope response)
    {
        if (!response.Succeeded)
        {
            throw new InvalidOperationException(BuildFailureMessage(response));
        }

        if (string.IsNullOrWhiteSpace(response.PayloadJson))
        {
            throw new InvalidOperationException("Bridge did not return a tool catalog. Start Revit and make sure BIM765T.Revit.Agent is initialized before calling tools/list.");
        }

        ToolCatalogResponse catalog;
        try
        {
            catalog = JsonUtil.Deserialize<ToolCatalogResponse>(response.PayloadJson);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Bridge returned invalid tool catalog JSON: " + ex.Message, ex);
        }

        if (catalog.Tools.Count == 0)
        {
            throw new InvalidOperationException("Bridge returned an empty tool catalog. Start Revit and make sure BIM765T.Revit.Agent is initialized before calling tools/list.");
        }

        catalog.Tools = catalog.Tools
            .Where(tool => tool != null)
            .OrderBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (catalog.Tools.Count == 0)
        {
            throw new InvalidOperationException("Bridge returned an empty MCP catalog. Start Revit and make sure WorkerHost can resolve the product tool catalog before calling tools/list.");
        }

        return catalog;
    }

    private static string BuildFailureMessage(ToolResponseEnvelope response)
    {
        var detail = response.Diagnostics.FirstOrDefault()?.Message;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            return $"Bridge unavailable while listing tools ({response.StatusCode}): {detail}";
        }

        if (!string.IsNullOrWhiteSpace(response.StatusCode))
        {
            return $"Bridge unavailable while listing tools ({response.StatusCode}). Start Revit and make sure BIM765T.Revit.Agent is initialized before calling tools/list.";
        }

        return "Bridge unavailable while listing tools. Start Revit and make sure BIM765T.Revit.Agent is initialized before calling tools/list.";
    }
}
