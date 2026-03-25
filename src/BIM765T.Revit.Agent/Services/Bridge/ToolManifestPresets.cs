using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal static class ToolManifestPresets
{
    internal static ToolManifestMetadata Read(params string[] requiredContext)
    {
        return new ToolManifestMetadata(
            requiredContext,
            idempotency: "read_only",
            riskTier: ToolRiskTiers.Tier0,
            canAutoExecute: true,
            latencyClass: ToolLatencyClasses.Interactive,
            uiSurface: ToolUiSurfaces.WorkerHome,
            progressMode: ToolProgressModes.None);
    }

    internal static ToolManifestMetadata Review(params string[] requiredContext)
    {
        return new ToolManifestMetadata(
            requiredContext,
            idempotency: "read_only",
            riskTier: ToolRiskTiers.Tier0,
            canAutoExecute: true,
            latencyClass: ToolLatencyClasses.Standard,
            uiSurface: ToolUiSurfaces.Evidence,
            progressMode: ToolProgressModes.None);
    }

    internal static ToolManifestMetadata Mutation(params string[] requiredContext)
    {
        return new ToolManifestMetadata(
            requiredContext,
            idempotency: "non_idempotent",
            previewArtifacts: new List<string> { "execution_result", "diff_summary" },
            riskTier: ToolRiskTiers.Tier1,
            canAutoExecute: false,
            latencyClass: ToolLatencyClasses.Standard,
            uiSurface: ToolUiSurfaces.Approvals,
            progressMode: ToolProgressModes.StageOnly);
    }

    internal static ToolManifestMetadata FileLifecycle(params string[] requiredContext)
    {
        return new ToolManifestMetadata(
            requiredContext,
            idempotency: "non_idempotent",
            previewArtifacts: new List<string> { "execution_result", "diff_summary" },
            riskTier: ToolRiskTiers.Tier2,
            canAutoExecute: false,
            latencyClass: ToolLatencyClasses.LongRunning,
            uiSurface: ToolUiSurfaces.Approvals,
            progressMode: ToolProgressModes.Heartbeat);
    }

    internal static ToolManifestMetadata WorkflowRead(params string[] requiredContext)
    {
        return new ToolManifestMetadata(
            requiredContext,
            batchMode: "chunked",
            idempotency: "read_only",
            previewArtifacts: new List<string> { "workflow_evidence" },
            riskTags: new List<string> { "workflow" },
            riskTier: ToolRiskTiers.Tier0,
            canAutoExecute: true,
            latencyClass: ToolLatencyClasses.LongRunning,
            uiSurface: ToolUiSurfaces.Queue,
            progressMode: ToolProgressModes.Heartbeat);
    }

    internal static ToolManifestMetadata WorkflowMutate(params string[] requiredContext)
    {
        return new ToolManifestMetadata(
            requiredContext,
            batchMode: "chunked",
            idempotency: "checkpointed",
            previewArtifacts: new List<string> { "workflow_evidence" },
            riskTags: new List<string> { "workflow" },
            riskTier: ToolRiskTiers.Tier1,
            canAutoExecute: false,
            latencyClass: ToolLatencyClasses.Batch,
            uiSurface: ToolUiSurfaces.Queue,
            progressMode: ToolProgressModes.Heartbeat);
    }
}
