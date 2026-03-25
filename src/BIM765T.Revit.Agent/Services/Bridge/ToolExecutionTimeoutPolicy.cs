using System;
using System.Collections.Generic;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal static class ToolExecutionTimeoutPolicy
{
    private static readonly HashSet<string> VeryLongRunningTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ToolNames.ExportIfcSafe,
        ToolNames.ExportDwgSafe,
        ToolNames.SheetPrintPdfSafe,
        ToolNames.FamilyBuildRoundProjectWrappersSafe,
        ToolNames.FamilyLoadSafe,
        ToolNames.ScheduleCreateSafe
    };

    private static readonly HashSet<string> LongRunningTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ToolNames.SchedulePreviewCreate,
        ToolNames.WorkflowFixLoopApply,
        ToolNames.WorkflowApply,
        ToolNames.WorkflowResume,
        ToolNames.WorkflowPlan,
        ToolNames.FileSaveDocument,
        ToolNames.FileSaveAsDocument,
        ToolNames.WorksharingSynchronizeWithCentral,
        ToolNames.DocumentOpenBackgroundRead,
        ToolNames.DataImportSafe
    };

    internal static int ResolveExecutionTimeoutMs(AgentSettings settings, ToolManifest? manifest)
    {
        if (manifest != null && manifest.ExecutionTimeoutMs > 0)
        {
            return manifest.ExecutionTimeoutMs;
        }

        return GetRecommendedTimeoutMs(settings, manifest?.ToolName ?? string.Empty);
    }

    internal static int GetRecommendedTimeoutMs(AgentSettings settings, string toolName)
    {
        var baseTimeoutMs = Math.Max(1_000, settings.RequestTimeoutSeconds * 1_000);

        if (VeryLongRunningTools.Contains(toolName))
        {
            return Math.Max(baseTimeoutMs, 300_000);
        }

        if (LongRunningTools.Contains(toolName) || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(baseTimeoutMs, 180_000);
        }

        return baseTimeoutMs;
    }
}
