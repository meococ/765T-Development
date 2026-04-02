using System;
using System.Linq;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Health;
using BIM765T.Revit.WorkerHost.Routing;
using Grpc.Core;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;

namespace BIM765T.Revit.WorkerHost.Grpc;

internal sealed class CatalogGrpcService : CatalogService.CatalogServiceBase
{
    private readonly MissionOrchestrator _orchestrator;
    private readonly RuntimeHealthService _runtimeHealth;

    public CatalogGrpcService(MissionOrchestrator orchestrator, RuntimeHealthService runtimeHealth)
    {
        _orchestrator = orchestrator;
        _runtimeHealth = runtimeHealth;
    }

    public override async Task<CatalogReply> ListTools(CatalogRequest request, ServerCallContext context)
    {
        var compat = new CompatibilityGrpcService(_orchestrator);
        var invoke = await compat.InvokeTool(new CompatToolRequest
        {
            Meta = request.Meta ?? new EnvelopeMetadata { CorrelationId = Guid.NewGuid().ToString("N") },
            ToolName = ToolNames.SessionListTools,
            PayloadJson = JsonUtil.Serialize(new ToolCatalogRequest { Audience = ToolCatalogAudiences.Mcp }),
            DryRun = true
        }, context).ConfigureAwait(false);

        var catalog = JsonUtil.Deserialize<ToolCatalogResponse>(invoke.PayloadJson);
        catalog.Tools = catalog.Tools
            .Where(tool => tool != null)
            .OrderBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var reply = new CatalogReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = invoke.Status.Succeeded,
                StatusCode = invoke.Status.StatusCode,
                Message = invoke.Status.StatusCode
            },
            PayloadJson = JsonUtil.Serialize(catalog)
        };
        reply.Status.Diagnostics.AddRange(invoke.Status.Diagnostics);
        foreach (var tool in catalog.Tools)
        {
            var summary = new ToolSummary
            {
                ToolName = tool.ToolName,
                Description = tool.Description,
                Enabled = tool.Enabled,
                SupportsDryRun = tool.SupportsDryRun,
                MutatesModel = tool.MutatesModel,
                ExecutionTimeoutMs = tool.ExecutionTimeoutMs,
                PermissionLevel = tool.PermissionLevel.ToString(),
                ApprovalRequirement = tool.ApprovalRequirement.ToString()
            };
            if (tool.RiskTags != null)
            {
                summary.RiskTags.AddRange(tool.RiskTags);
            }

            reply.Tools.Add(summary);
        }
        return reply;
    }

    public override Task<CapabilitiesReply> GetCapabilities(CatalogRequest request, ServerCallContext context)
    {
        return InvokeScalarAsync(request, ToolNames.SessionGetCapabilities, context);
    }

    public override async Task<RuntimeHealthReply> GetRuntimeHealth(CatalogRequest request, ServerCallContext context)
    {
        var report = await _runtimeHealth.CollectAsync(probePublicControlPlane: false, context.CancellationToken).ConfigureAwait(false);
        return new RuntimeHealthReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = report.Ready,
                StatusCode = report.Ready ? StatusCodes.Ok : StatusCodes.BridgeUnavailable,
                Message = BuildRuntimeHealthMessage(report)
            },
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(report)
        };
    }

    private static string BuildRuntimeHealthMessage(RuntimeHealthReport report)
    {
        if (report.RuntimeLooksStale)
        {
            return $"WorkerHost runtime ready but tool catalog looks stale ({report.RuntimeToolCount}/{report.SourceToolCount}).";
        }

        return report.Degraded
            ? "WorkerHost ready but running in degraded mode."
            : "WorkerHost runtime health ready.";
    }

    private async Task<CapabilitiesReply> InvokeScalarAsync(CatalogRequest request, string toolName, ServerCallContext context)
    {
        var compat = new CompatibilityGrpcService(_orchestrator);
        var invoke = await compat.InvokeTool(new CompatToolRequest
        {
            Meta = request.Meta ?? new EnvelopeMetadata { CorrelationId = Guid.NewGuid().ToString("N") },
            ToolName = toolName,
            PayloadJson = JsonUtil.Serialize(new ToolCatalogRequest { Audience = ToolCatalogAudiences.Mcp }),
            DryRun = true
        }, context).ConfigureAwait(false);

        var reply = new CapabilitiesReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = invoke.Status.Succeeded,
                StatusCode = invoke.Status.StatusCode,
                Message = invoke.Status.StatusCode
            },
            PayloadJson = invoke.PayloadJson ?? string.Empty
        };
        reply.Status.Diagnostics.AddRange(invoke.Status.Diagnostics);
        return reply;
    }
}
