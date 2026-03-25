using System;
using System.Linq;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Routing;
using Grpc.Core;

namespace BIM765T.Revit.WorkerHost.Grpc;

internal sealed class MissionGrpcService : MissionService.MissionServiceBase
{
    private readonly MissionOrchestrator _orchestrator;

    public MissionGrpcService(MissionOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public override async Task<MissionReply> SubmitMission(SubmitMissionRequest request, ServerCallContext context)
    {
        var missionId = string.IsNullOrWhiteSpace(request.Meta?.MissionId) ? Guid.NewGuid().ToString("N") : request.Meta.MissionId;
        var result = await _orchestrator.SubmitMissionAsync(
            missionId,
            System.Text.Json.JsonSerializer.Serialize(request),
            request.Message ?? string.Empty,
            request.PersonaId ?? string.Empty,
            request.ClientSurface ?? string.Empty,
            request.ContinueMission,
            request.Meta ?? new EnvelopeMetadata { MissionId = missionId, CorrelationId = missionId },
            context.CancellationToken).ConfigureAwait(false);

        return ToReply(result.Snapshot, result.Events, result.KernelResult);
    }

    public override async Task<MissionReply> GetMission(MissionQuery request, ServerCallContext context)
    {
        var snapshot = await _orchestrator.GetMissionAsync(request.MissionId, context.CancellationToken).ConfigureAwait(false);
        if (snapshot == null)
        {
            return new MissionReply
            {
                Status = new StatusEnvelope
                {
                    Succeeded = false,
                    StatusCode = BIM765T.Revit.Contracts.Common.StatusCodes.InvalidRequest,
                    Message = "Mission not found."
                },
                MissionId = request.MissionId,
                State = "Blocked"
            };
        }

        var events = await _orchestrator.GetEventsAsync(request.MissionId, context.CancellationToken).ConfigureAwait(false);
        return new MissionReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = true,
                StatusCode = BIM765T.Revit.Contracts.Common.StatusCodes.ReadSucceeded,
                Message = snapshot.LastStatusCode
            },
            MissionId = snapshot.MissionId,
            State = snapshot.State,
            PayloadJson = snapshot.ResponseJson ?? string.Empty,
            ResponseText = snapshot.ResponseText ?? string.Empty,
            Events = { events.Select(ToEventEnvelope) }
        };
    }

    public override async Task<MissionReply> ApproveMission(MissionCommandRequest request, ServerCallContext context)
    {
        var result = await _orchestrator.ApproveMissionAsync(BuildCommandInput(request, "approval"), context.CancellationToken).ConfigureAwait(false);
        return ToReply(result.Snapshot, result.Events, result.KernelResult);
    }

    public override async Task<MissionReply> RejectMission(MissionCommandRequest request, ServerCallContext context)
    {
        var result = await _orchestrator.RejectMissionAsync(BuildCommandInput(request, "reject"), context.CancellationToken).ConfigureAwait(false);
        return ToReply(result.Snapshot, result.Events, result.KernelResult);
    }

    public override async Task<MissionReply> CancelMission(MissionCommandRequest request, ServerCallContext context)
    {
        var result = await _orchestrator.CancelMissionAsync(BuildCommandInput(request, "cancel"), context.CancellationToken).ConfigureAwait(false);
        return ToReply(result.Snapshot, result.Events, result.KernelResult);
    }

    public override async Task<MissionReply> ResumeMission(MissionCommandRequest request, ServerCallContext context)
    {
        var result = await _orchestrator.ResumeMissionAsync(BuildCommandInput(request, "resume"), context.CancellationToken).ConfigureAwait(false);
        return ToReply(result.Snapshot, result.Events, result.KernelResult);
    }

    private static MissionReply ToReply(MissionSnapshot snapshot, System.Collections.Generic.IReadOnlyList<MissionEventRecord> events, Kernel.KernelInvocationResult result)
    {
        var reply = new MissionReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = result.Succeeded,
                StatusCode = result.StatusCode,
                Message = result.StatusCode
            },
            MissionId = snapshot.MissionId,
            State = snapshot.State,
            PayloadJson = snapshot.ResponseJson ?? string.Empty,
            ResponseText = snapshot.ResponseText ?? string.Empty
        };
        reply.Status.Diagnostics.AddRange(result.Diagnostics);
        reply.Events.AddRange(events.Select(ToEventEnvelope));
        return reply;
    }

    private static EventEnvelope ToEventEnvelope(MissionEventRecord record)
    {
        return new EventEnvelope
        {
            StreamId = record.StreamId,
            Version = record.Version,
            EventType = record.EventType,
            PayloadJson = record.PayloadJson,
            OccurredUtc = record.OccurredUtc,
            CorrelationId = record.CorrelationId,
            CausationId = record.CausationId,
            ActorId = record.ActorId,
            DocumentKey = record.DocumentKey,
            Terminal = record.Terminal
        };
    }

    private static MissionCommandInput BuildCommandInput(MissionCommandRequest request, string commandName)
    {
        return new MissionCommandInput
        {
            Meta = request.Meta ?? new EnvelopeMetadata(),
            MissionId = request.MissionId ?? string.Empty,
            CommandName = commandName,
            Note = request.Note ?? string.Empty,
            ApprovalToken = request.ApprovalToken ?? string.Empty,
            PreviewRunId = request.PreviewRunId ?? string.Empty,
            ExpectedContextJson = request.ExpectedContextJson ?? string.Empty,
            AllowMutations = request.AllowMutations,
            RecoveryBranchId = request.RecoveryBranchId ?? string.Empty
        };
    }
}
