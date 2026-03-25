using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class ToolInvocationProfile
{
    internal string Priority { get; set; } = ToolQueuePriorities.Normal;

    internal string RiskTier { get; set; } = ToolRiskTiers.Tier0;

    internal string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    internal string LatencyClass { get; set; } = ToolLatencyClasses.Standard;
}

internal static class ToolQueuePriorityResolver
{
    internal static ToolInvocationProfile Resolve(ToolRequestEnvelope request, ToolManifest? manifest = null)
    {
        request ??= new ToolRequestEnvelope();
        var riskTier = ResolveRiskTier(request, manifest);
        return new ToolInvocationProfile
        {
            Priority = ResolvePriority(request, manifest, riskTier),
            RiskTier = riskTier,
            ExecutionTier = ResolveExecutionTier(riskTier),
            LatencyClass = ResolveLatencyClass(request, manifest)
        };
    }

    internal static string NormalizePriority(string? priority)
    {
        if (string.Equals(priority, ToolQueuePriorities.High, StringComparison.OrdinalIgnoreCase))
        {
            return ToolQueuePriorities.High;
        }

        if (string.Equals(priority, ToolQueuePriorities.Low, StringComparison.OrdinalIgnoreCase))
        {
            return ToolQueuePriorities.Low;
        }

        return ToolQueuePriorities.Normal;
    }

    private static string ResolvePriority(ToolRequestEnvelope request, ToolManifest? manifest, string riskTier)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedPriority))
        {
            return NormalizePriority(request.RequestedPriority);
        }

        if (string.Equals(riskTier, ToolRiskTiers.Tier2, StringComparison.OrdinalIgnoreCase))
        {
            return ToolQueuePriorities.Low;
        }

        if (manifest != null
            && (manifest.PermissionLevel == PermissionLevel.Read
                || manifest.PermissionLevel == PermissionLevel.Review
                || string.Equals(manifest.UiSurface, ToolUiSurfaces.Queue, StringComparison.OrdinalIgnoreCase)))
        {
            return ToolQueuePriorities.High;
        }

        var toolName = request.ToolName ?? string.Empty;
        if (toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("document.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("view.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolQueuePriorities.High;
        }

        if (toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("file.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("export.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolQueuePriorities.Low;
        }

        return string.Equals(riskTier, ToolRiskTiers.Tier1, StringComparison.OrdinalIgnoreCase)
            ? ToolQueuePriorities.Normal
            : ToolQueuePriorities.High;
    }

    private static string ResolveRiskTier(ToolRequestEnvelope request, ToolManifest? manifest)
    {
        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.RiskTier))
        {
            return manifest.RiskTier;
        }

        var toolName = request.ToolName ?? string.Empty;
        if (toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("document.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("view.", StringComparison.OrdinalIgnoreCase)
            || request.DryRun)
        {
            return ToolRiskTiers.Tier0;
        }

        if (!string.IsNullOrWhiteSpace(request.ApprovalToken)
            || toolName.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("purge", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("save", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("sync", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ToolRiskTiers.Tier2;
        }

        return ToolRiskTiers.Tier1;
    }

    private static string ResolveExecutionTier(string riskTier)
    {
        if (string.Equals(riskTier, ToolRiskTiers.Tier2, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerExecutionTiers.Tier2;
        }

        if (string.Equals(riskTier, ToolRiskTiers.Tier1, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerExecutionTiers.Tier1;
        }

        return WorkerExecutionTiers.Tier0;
    }

    private static string ResolveLatencyClass(ToolRequestEnvelope request, ToolManifest? manifest)
    {
        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.LatencyClass))
        {
            return manifest.LatencyClass;
        }

        var toolName = request.ToolName ?? string.Empty;
        if (toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)
            || toolName.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ToolLatencyClasses.Batch;
        }

        if (toolName.IndexOf("sync", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("save", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("open_background", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ToolLatencyClasses.LongRunning;
        }

        if (toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("document.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolLatencyClasses.Interactive;
        }

        return ToolLatencyClasses.Standard;
    }
}
