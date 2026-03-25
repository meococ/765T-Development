using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Kernel;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class MissionOrchestrator
{
    private readonly SqliteMissionEventStore _store;
    private readonly IMissionEventBus _eventBus;
    private readonly WorkerHostSettings _settings;
    private readonly IKernelClient _kernel;
    private readonly PlannerAgent _planner;
    private readonly RetrieverAgent _retriever;
    private readonly IExecutionPolicyEvaluator _policyEvaluator;
    private readonly VerifierAgent _verifier;

    public MissionOrchestrator(
        SqliteMissionEventStore store,
        IMissionEventBus eventBus,
        WorkerHostSettings settings,
        IKernelClient kernel,
        PlannerAgent planner,
        RetrieverAgent retriever,
        IExecutionPolicyEvaluator policyEvaluator,
        VerifierAgent verifier)
    {
        _store = store;
        _eventBus = eventBus;
        _settings = settings;
        _kernel = kernel;
        _planner = planner;
        _retriever = retriever;
        _policyEvaluator = policyEvaluator;
        _verifier = verifier;
    }

    public async Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> InvokeCompatibilityAsync(
        string streamId,
        string requestJson,
        KernelToolRequest kernelRequest,
        CancellationToken cancellationToken)
    {
        var snapshot = new MissionSnapshot
        {
            MissionId = streamId,
            State = "Running",
            SessionId = kernelRequest.SessionId,
            RequestJson = requestJson
        };

        await AppendAsync(streamId, "TaskStarted", new { tool = kernelRequest.ToolName, request = requestJson }, snapshot, kernelRequest, cancellationToken).ConfigureAwait(false);
        var result = await _kernel.InvokeAsync(kernelRequest, cancellationToken).ConfigureAwait(false);
        var verification = _verifier.Evaluate(result, snapshot, kernelRequest);
        ApplyResult(snapshot, result, kernelRequest, verification);
        await AppendVerificationEventsAsync(streamId, snapshot, kernelRequest, verification, cancellationToken).ConfigureAwait(false);

        var events = await _store.ListAsync(streamId, cancellationToken).ConfigureAwait(false);
        return (snapshot, events, result);
    }

    public async Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> SubmitMissionAsync(
        string missionId,
        string requestJson,
        string message,
        string personaId,
        string clientSurface,
        bool continueMission,
        EnvelopeMetadata meta,
        CancellationToken cancellationToken)
    {
        var normalizedMissionId = string.IsNullOrWhiteSpace(missionId) ? Guid.NewGuid().ToString("N") : missionId;
        var retrieval = await _retriever.RetrieveAsync(message, meta.DocumentKey, 3, cancellationToken).ConfigureAwait(false);
        var plan = _planner.BuildSubmissionPlan(new MissionPlanningContext
        {
            SessionId = meta.SessionId ?? string.Empty,
            PersonaId = personaId ?? string.Empty,
            ClientSurface = string.IsNullOrWhiteSpace(clientSurface) ? WorkerClientSurfaces.Mcp : clientSurface,
            UserMessage = message ?? string.Empty,
            DocumentKey = meta.DocumentKey ?? string.Empty,
            TargetDocument = meta.TargetDocument ?? string.Empty,
            TargetView = meta.TargetView ?? string.Empty,
            WorkspaceId = ResolveWorkspaceId(meta.DocumentKey),
            ContinueMission = continueMission,
            AutonomyMode = _settings.ResolveAutonomyMode()
        }, retrieval);
        var safety = _policyEvaluator.EvaluateSubmission(plan);
        if (!safety.Allowed)
        {
            return await BuildBlockedMissionAsync(normalizedMissionId, requestJson, meta, safety, cancellationToken).ConfigureAwait(false);
        }

        var workerRequest = new WorkerMessageRequest
        {
            SessionId = meta.SessionId ?? string.Empty,
            Message = message ?? string.Empty,
            PersonaId = personaId ?? string.Empty,
            ClientSurface = string.IsNullOrWhiteSpace(clientSurface) ? WorkerClientSurfaces.Mcp : clientSurface,
            ContinueMission = continueMission,
            AutonomyMode = plan.AutonomyMode,
            PlanningSummary = plan.Summary,
            PlannerTraceSummary = plan.PlannerTraceSummary,
            GroundingLevel = plan.GroundingLevel,
            ChosenToolSequence = plan.ChosenToolSequence?.ToList() ?? new List<string>(),
            EvidenceRefs = plan.EvidenceRefs?.ToList() ?? new List<string>()
        };

        var snapshot = new MissionSnapshot
        {
            MissionId = normalizedMissionId,
            SessionId = workerRequest.SessionId,
            Intent = plan.Intent,
            RequestJson = requestJson,
            State = WorkerMissionStates.Understanding,
            FlowState = plan.FlowState,
            GroundingLevel = plan.GroundingLevel,
            PlanSummary = plan.Summary,
            PlannerTraceSummary = plan.PlannerTraceSummary,
            ApprovalRequired = plan.ApprovalRequired,
            ChosenToolSequence = plan.ChosenToolSequence?.ToList() ?? new List<string>(),
            EvidenceRefs = plan.EvidenceRefs?.ToList() ?? new List<string>(),
            AutonomyMode = plan.AutonomyMode
        };

        var kernelRequest = CreateKernelRequest(meta, ToolNames.WorkerMessage, JsonUtil.Serialize(workerRequest), dryRun: true);
        kernelRequest.MissionId = normalizedMissionId;

        await AppendAsync(normalizedMissionId, "TaskStarted", new { message, personaId, clientSurface }, snapshot, kernelRequest, cancellationToken).ConfigureAwait(false);
        await AppendAsync(normalizedMissionId, "IntentClassified", new
        {
            plan.Intent,
            plan.TargetHint,
            plan.FlowState,
            plan.GroundingLevel,
            plan.AutonomyMode
        }, snapshot, kernelRequest, cancellationToken).ConfigureAwait(false);
        await AppendAsync(normalizedMissionId, "ContextResolved", new
        {
            hits = retrieval.Hits.ConvertAll(x => new { x.Title, x.SourceRef, x.Score }),
            retrieval.Summary,
            plan.GroundingLevel,
            plan.EvidenceRefs
        }, snapshot, kernelRequest, cancellationToken).ConfigureAwait(false);
        await AppendAsync(normalizedMissionId, "PlanBuilt", new
        {
            tool = ToolNames.WorkerMessage,
            summary = plan.Summary,
            plan.FlowState,
            plan.ChosenToolSequence,
            plan.GroundingLevel,
            plan.ApprovalRequired,
            plan.PlannerTraceSummary,
            plan.AutonomyMode
        }, snapshot, kernelRequest, cancellationToken).ConfigureAwait(false);

        var result = await _kernel.InvokeAsync(kernelRequest, cancellationToken).ConfigureAwait(false);
        var verification = _verifier.Evaluate(result, snapshot, kernelRequest);
        ApplyResult(snapshot, result, kernelRequest, verification);
        await AppendVerificationEventsAsync(normalizedMissionId, snapshot, kernelRequest, verification, cancellationToken).ConfigureAwait(false);

        var events = await _store.ListAsync(normalizedMissionId, cancellationToken).ConfigureAwait(false);
        return (snapshot, events, result);
    }

    public Task<MissionSnapshot?> GetMissionAsync(string missionId, CancellationToken cancellationToken)
    {
        return _store.TryGetSnapshotAsync(missionId, cancellationToken);
    }

    public Task<List<MissionEventRecord>> GetEventsAsync(string missionId, CancellationToken cancellationToken)
    {
        return _store.ListAsync(missionId, cancellationToken);
    }

    public Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> ApproveMissionAsync(
        MissionCommandInput input,
        CancellationToken cancellationToken)
    {
        return RunMissionCommandAsync(input, "UserApproved", cancellationToken);
    }

    public Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> RejectMissionAsync(
        MissionCommandInput input,
        CancellationToken cancellationToken)
    {
        return RunMissionCommandAsync(input, "UserRejected", cancellationToken);
    }

    public Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> CancelMissionAsync(
        MissionCommandInput input,
        CancellationToken cancellationToken)
    {
        return RunMissionCommandAsync(input, "TaskCanceled", cancellationToken);
    }

    public Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> ResumeMissionAsync(
        MissionCommandInput input,
        CancellationToken cancellationToken)
    {
        return RunMissionCommandAsync(input, "ExecutionStarted", cancellationToken);
    }

    private async Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> RunMissionCommandAsync(
        MissionCommandInput input,
        string eventType,
        CancellationToken cancellationToken)
    {
        var snapshot = await _store.TryGetSnapshotAsync(input.MissionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Mission not found: " + input.MissionId);

        var plan = _planner.BuildCommandPlan(input.CommandName);
        var safety = _policyEvaluator.EvaluateCommand(input, snapshot);
        if (!safety.Allowed)
        {
            return await BuildBlockedMissionAsync(input.MissionId, snapshot.RequestJson, input.Meta, safety, cancellationToken, snapshot).ConfigureAwait(false);
        }

        var workerRequest = new WorkerMessageRequest
        {
            SessionId = string.IsNullOrWhiteSpace(input.Meta.SessionId) ? snapshot.SessionId : input.Meta.SessionId,
            Message = safety.ResolvedCommandText,
            ContinueMission = true,
            ClientSurface = WorkerClientSurfaces.Mcp,
            AutonomyMode = snapshot.AutonomyMode,
            PlanningSummary = snapshot.PlanSummary,
            PlannerTraceSummary = snapshot.PlannerTraceSummary,
            GroundingLevel = snapshot.GroundingLevel,
            ChosenToolSequence = snapshot.ChosenToolSequence?.ToList() ?? new List<string>(),
            EvidenceRefs = snapshot.EvidenceRefs?.ToList() ?? new List<string>()
        };

        snapshot.FlowState = plan.FlowState;
        snapshot.GroundingLevel = plan.GroundingLevel;
        snapshot.PlanSummary = plan.Summary;
        snapshot.PlannerTraceSummary = plan.PlannerTraceSummary;
        snapshot.ApprovalRequired = plan.ApprovalRequired;
        snapshot.ChosenToolSequence = plan.ChosenToolSequence;
        snapshot.EvidenceRefs = plan.EvidenceRefs;
        snapshot.AutonomyMode = plan.AutonomyMode;

        var kernelRequest = CreateKernelRequest(input.Meta, ToolNames.WorkerMessage, JsonUtil.Serialize(workerRequest), dryRun: true);
        kernelRequest.MissionId = input.MissionId;
        kernelRequest.ApprovalToken = string.IsNullOrWhiteSpace(input.ApprovalToken) ? snapshot.ApprovalToken : input.ApprovalToken;
        kernelRequest.PreviewRunId = string.IsNullOrWhiteSpace(input.PreviewRunId) ? snapshot.PreviewRunId : input.PreviewRunId;
        kernelRequest.ExpectedContextJson = string.IsNullOrWhiteSpace(input.ExpectedContextJson) ? snapshot.ExpectedContextJson : input.ExpectedContextJson;

        await AppendAsync(input.MissionId, eventType, new
        {
            command = plan.CommandText,
            plan.FlowState,
            plan.ApprovalRequired,
            plan.PlannerTraceSummary,
            input.Note,
            input.RecoveryBranchId,
            input.AllowMutations
        }, snapshot, kernelRequest, cancellationToken).ConfigureAwait(false);
        var result = await _kernel.InvokeAsync(kernelRequest, cancellationToken).ConfigureAwait(false);
        var verification = _verifier.Evaluate(result, snapshot, kernelRequest, input);
        ApplyResult(snapshot, result, kernelRequest, verification);
        await AppendVerificationEventsAsync(input.MissionId, snapshot, kernelRequest, verification, cancellationToken).ConfigureAwait(false);
        var events = await _store.ListAsync(input.MissionId, cancellationToken).ConfigureAwait(false);
        return (snapshot, events, result);
    }

    private async Task AppendAsync(string missionId, string eventType, object payload, MissionSnapshot snapshot, KernelToolRequest kernelRequest, CancellationToken cancellationToken, bool? terminalOverride = null)
    {
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot);
        var record = new MissionEventRecord
        {
            StreamId = missionId,
            EventType = eventType,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
            OccurredUtc = DateTime.UtcNow.ToString("O"),
            CorrelationId = kernelRequest.CorrelationId,
            CausationId = string.IsNullOrWhiteSpace(kernelRequest.CausationId) ? kernelRequest.CorrelationId : kernelRequest.CausationId,
            ActorId = kernelRequest.ActorId ?? string.Empty,
            DocumentKey = kernelRequest.DocumentKey ?? string.Empty,
            Terminal = terminalOverride ?? snapshot.Terminal
        };
        snapshot.Version = await _store.AppendAsync(record, snapshotJson, cancellationToken).ConfigureAwait(false);
        _eventBus.Publish(record);
    }

    private static KernelToolRequest CreateKernelRequest(EnvelopeMetadata meta, string toolName, string payloadJson, bool dryRun)
    {
        return new KernelToolRequest
        {
            MissionId = meta.MissionId,
            CorrelationId = string.IsNullOrWhiteSpace(meta.CorrelationId) ? Guid.NewGuid().ToString("N") : meta.CorrelationId,
            CausationId = meta.CausationId ?? string.Empty,
            ActorId = meta.ActorId ?? string.Empty,
            DocumentKey = meta.DocumentKey ?? string.Empty,
            RequestedAtUtc = string.IsNullOrWhiteSpace(meta.RequestedAtUtc) ? DateTime.UtcNow.ToString("O") : meta.RequestedAtUtc,
            TimeoutMs = meta.TimeoutMs > 0 ? meta.TimeoutMs : 120_000,
            CancellationTokenId = meta.CancellationTokenId ?? string.Empty,
            ToolName = toolName,
            PayloadJson = payloadJson,
            Caller = "BIM765T.Revit.WorkerHost",
            SessionId = meta.SessionId ?? string.Empty,
            DryRun = dryRun,
            TargetDocument = meta.TargetDocument ?? string.Empty,
            TargetView = meta.TargetView ?? string.Empty,
            ExpectedContextJson = string.Empty,
            ApprovalToken = string.Empty,
            ScopeDescriptorJson = string.Empty,
            PreviewRunId = string.Empty
        };
    }

    private static void ApplyResult(MissionSnapshot snapshot, KernelInvocationResult result, KernelToolRequest kernelRequest, VerificationResult verification)
    {
        snapshot.ResponseJson = result.PayloadJson ?? string.Empty;
        snapshot.ResponseText = verification.ResponseText;
        snapshot.LastStatusCode = result.StatusCode;
        snapshot.ApprovalToken = result.ApprovalToken;
        snapshot.PreviewRunId = result.PreviewRunId;
        snapshot.ExpectedContextJson = kernelRequest.ExpectedContextJson;
        snapshot.State = verification.State;
        snapshot.Terminal = verification.Terminal;
    }

    private static string ResolveWorkspaceId(string? documentKey)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            return "default";
        }

        return "default";
    }

    private async Task AppendVerificationEventsAsync(string missionId, MissionSnapshot snapshot, KernelToolRequest kernelRequest, VerificationResult verification, CancellationToken cancellationToken)
    {
        foreach (var descriptor in verification.Events)
        {
            await AppendAsync(missionId, descriptor.EventType, descriptor.Payload, snapshot, kernelRequest, cancellationToken, descriptor.Terminal).ConfigureAwait(false);
        }
    }

    private async Task<(MissionSnapshot Snapshot, IReadOnlyList<MissionEventRecord> Events, KernelInvocationResult KernelResult)> BuildBlockedMissionAsync(
        string missionId,
        string requestJson,
        EnvelopeMetadata meta,
        SafetyAssessment safety,
        CancellationToken cancellationToken,
        MissionSnapshot? existingSnapshot = null)
    {
        var snapshot = existingSnapshot ?? new MissionSnapshot
        {
            MissionId = missionId,
            SessionId = meta.SessionId ?? string.Empty,
            RequestJson = requestJson
        };

        snapshot.State = WorkerMissionStates.Blocked;
        snapshot.Terminal = true;
        snapshot.LastStatusCode = safety.StatusCode;
        snapshot.ResponseText = safety.Diagnostics.Count == 0 ? safety.StatusCode : string.Join(" | ", safety.Diagnostics);
        snapshot.ResponseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            blocked = true,
            safety.StatusCode,
            safety.Diagnostics
        });

        var kernelRequest = CreateKernelRequest(meta, ToolNames.WorkerMessage, snapshot.RequestJson, dryRun: true);
        kernelRequest.MissionId = missionId;
        await AppendAsync(missionId, "TaskBlocked", new { safety.StatusCode, safety.Diagnostics }, snapshot, kernelRequest, cancellationToken, terminalOverride: true).ConfigureAwait(false);

        var result = new KernelInvocationResult
        {
            Succeeded = false,
            StatusCode = safety.StatusCode,
            PayloadJson = snapshot.ResponseJson,
            ProtocolVersion = BridgeProtocol.PipeV1,
            Diagnostics = safety.Diagnostics
        };

        var events = await _store.ListAsync(missionId, cancellationToken).ConfigureAwait(false);
        return (snapshot, events, result);
    }
}
