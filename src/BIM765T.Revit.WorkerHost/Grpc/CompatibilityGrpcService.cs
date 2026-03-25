using System;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.WorkerHost.Routing;
using Grpc.Core;

namespace BIM765T.Revit.WorkerHost.Grpc;

internal sealed class CompatibilityGrpcService : CompatibilityService.CompatibilityServiceBase
{
    private readonly MissionOrchestrator _orchestrator;

    public CompatibilityGrpcService(MissionOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public override async Task<CompatToolReply> InvokeTool(CompatToolRequest request, ServerCallContext context)
    {
        var missionId = string.IsNullOrWhiteSpace(request.Meta?.MissionId) ? Guid.NewGuid().ToString("N") : request.Meta.MissionId;
        var kernelRequest = new Kernel.KernelToolRequest
        {
            MissionId = missionId,
            CorrelationId = string.IsNullOrWhiteSpace(request.Meta?.CorrelationId) ? missionId : request.Meta.CorrelationId,
            CausationId = request.Meta?.CausationId ?? string.Empty,
            ActorId = request.Meta?.ActorId ?? string.Empty,
            DocumentKey = request.Meta?.DocumentKey ?? string.Empty,
            RequestedAtUtc = string.IsNullOrWhiteSpace(request.Meta?.RequestedAtUtc) ? DateTime.UtcNow.ToString("O") : request.Meta.RequestedAtUtc,
            TimeoutMs = request.Meta?.TimeoutMs > 0 ? request.Meta.TimeoutMs : 120_000,
            CancellationTokenId = request.Meta?.CancellationTokenId ?? string.Empty,
            ToolName = request.ToolName,
            PayloadJson = request.PayloadJson ?? string.Empty,
            Caller = "BIM765T.Revit.WorkerHost.CompatibilityGrpcService",
            SessionId = request.Meta?.SessionId ?? string.Empty,
            DryRun = request.DryRun,
            TargetDocument = request.Meta?.TargetDocument ?? string.Empty,
            TargetView = request.Meta?.TargetView ?? string.Empty,
            ExpectedContextJson = request.ExpectedContextJson ?? string.Empty,
            ApprovalToken = request.ApprovalToken ?? string.Empty,
            ScopeDescriptorJson = request.ScopeDescriptorJson ?? string.Empty,
            PreviewRunId = request.PreviewRunId ?? string.Empty
        };

        var (_, _, result) = await _orchestrator.InvokeCompatibilityAsync(
            missionId,
            System.Text.Json.JsonSerializer.Serialize(request),
            kernelRequest,
            context.CancellationToken).ConfigureAwait(false);

        var reply = new CompatToolReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = result.Succeeded,
                StatusCode = result.StatusCode,
                Message = result.StatusCode
            },
            ToolName = request.ToolName,
            PayloadJson = result.PayloadJson ?? string.Empty,
            ApprovalToken = result.ApprovalToken ?? string.Empty,
            PreviewRunId = result.PreviewRunId ?? string.Empty,
            DiffSummaryJson = result.DiffSummaryJson ?? string.Empty,
            ReviewSummaryJson = result.ReviewSummaryJson ?? string.Empty,
            ConfirmationRequired = result.ConfirmationRequired,
            ProtocolVersion = result.ProtocolVersion ?? string.Empty
        };
        reply.Status.Diagnostics.AddRange(result.Diagnostics);
        reply.ChangedIds.AddRange(result.ChangedIds);
        reply.Artifacts.AddRange(result.Artifacts);
        return reply;
    }
}
