using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Capabilities;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Health;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Routing;
using Microsoft.AspNetCore.Http;

namespace BIM765T.Revit.WorkerHost.ExternalAi;

internal sealed class ExternalAiGatewayService
{
    private readonly MissionOrchestrator _orchestrator;
    private readonly IMissionEventBus _eventBus;
    private readonly RuntimeHealthService _runtimeHealth;
    private readonly CapabilityHostService _capabilities;
    private readonly WorkerHostSettings _settings;
    private readonly ILlmProviderConfigResolver _llmConfigResolver;

    public ExternalAiGatewayService(
        MissionOrchestrator orchestrator,
        IMissionEventBus eventBus,
        RuntimeHealthService runtimeHealth,
        CapabilityHostService capabilities,
        WorkerHostSettings settings,
        ILlmProviderConfigResolver llmConfigResolver)
    {
        _orchestrator = orchestrator;
        _eventBus = eventBus;
        _runtimeHealth = runtimeHealth;
        _capabilities = capabilities;
        _settings = settings;
        _llmConfigResolver = llmConfigResolver;
    }

    public async Task<ExternalAiGatewayStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var health = await _runtimeHealth.CollectAsync(probePublicControlPlane: false, cancellationToken).ConfigureAwait(false);
        var profile = _llmConfigResolver.Resolve();
        var runtimeProfile = TryReadRuntimeProfile(health.Kernel.PayloadJson);
        var statusWarnings = BuildStatusWarnings(health, profile, runtimeProfile, out var restartRequired);
        return new ExternalAiGatewayStatusResponse
        {
            Health = health,
            ConfiguredProvider = profile.ConfiguredProvider,
            PlannerModel = profile.PlannerPrimaryModel,
            ResponseModel = profile.ResponseModel,
            ReasoningMode = profile.ReasoningMode,
            AutonomyMode = _settings.ResolveAutonomyMode(),
            SecretSourceKind = profile.SecretSourceKind,
            RuntimeConfiguredProvider = runtimeProfile?.ConfiguredProvider ?? string.Empty,
            RuntimePlannerModel = runtimeProfile?.PlannerModel ?? string.Empty,
            RuntimeResponseModel = runtimeProfile?.ResponseModel ?? string.Empty,
            RestartRequired = restartRequired,
            StatusWarnings = statusWarnings
        };
    }

    public async Task<ExternalAiMissionResponse> SubmitChatAsync(ExternalAiChatRequest request, CancellationToken cancellationToken)
    {
        request ??= new ExternalAiChatRequest();
        var missionId = string.IsNullOrWhiteSpace(request.MissionId) ? Guid.NewGuid().ToString("N") : request.MissionId;
        var meta = BuildMeta(
            missionId,
            request.SessionId,
            request.ActorId,
            request.DocumentKey,
            request.TargetDocument,
            request.TargetView,
            request.TimeoutMs);

        var result = await _orchestrator.SubmitMissionAsync(
            missionId,
            JsonUtil.Serialize(request),
            request.Message ?? string.Empty,
            request.PersonaId ?? string.Empty,
            WorkerClientSurfaces.Mcp,
            request.ContinueMission,
            meta,
            cancellationToken).ConfigureAwait(false);

        return ToResponse(result.Snapshot, result.Events, result.KernelResult, _llmConfigResolver.Resolve());
    }

    public async Task<ExternalAiMissionResponse> GetMissionAsync(string missionId, CancellationToken cancellationToken)
    {
        var snapshot = await _orchestrator.GetMissionAsync(missionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Mission not found: " + missionId);
        var events = await _orchestrator.GetEventsAsync(missionId, cancellationToken).ConfigureAwait(false);
        return ToResponse(snapshot, events, new KernelInvocationResult
        {
            Succeeded = !string.Equals(snapshot.State, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(snapshot.State, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase),
            StatusCode = string.IsNullOrWhiteSpace(snapshot.LastStatusCode) ? BIM765T.Revit.Contracts.Common.StatusCodes.Ok : snapshot.LastStatusCode,
            PayloadJson = snapshot.ResponseJson,
            ApprovalToken = snapshot.ApprovalToken,
            PreviewRunId = snapshot.PreviewRunId
        }, _llmConfigResolver.Resolve());
    }

    public async Task<ExternalAiMissionResponse> ApproveAsync(string missionId, ExternalAiMissionCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await _orchestrator.ApproveMissionAsync(BuildCommandInput(missionId, request, "approval"), cancellationToken).ConfigureAwait(false);
        return ToResponse(result.Snapshot, result.Events, result.KernelResult, _llmConfigResolver.Resolve());
    }

    public async Task<ExternalAiMissionResponse> RejectAsync(string missionId, ExternalAiMissionCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await _orchestrator.RejectMissionAsync(BuildCommandInput(missionId, request, "reject"), cancellationToken).ConfigureAwait(false);
        return ToResponse(result.Snapshot, result.Events, result.KernelResult, _llmConfigResolver.Resolve());
    }

    public async Task<ExternalAiMissionResponse> CancelAsync(string missionId, ExternalAiMissionCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await _orchestrator.CancelMissionAsync(BuildCommandInput(missionId, request, "cancel"), cancellationToken).ConfigureAwait(false);
        return ToResponse(result.Snapshot, result.Events, result.KernelResult, _llmConfigResolver.Resolve());
    }

    public async Task<ExternalAiMissionResponse> ResumeAsync(string missionId, ExternalAiMissionCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await _orchestrator.ResumeMissionAsync(BuildCommandInput(missionId, request, "resume"), cancellationToken).ConfigureAwait(false);
        return ToResponse(result.Snapshot, result.Events, result.KernelResult, _llmConfigResolver.Resolve());
    }

    public ExternalAiGatewayCatalogResponse GetCatalog(string? workspaceId)
    {
        var ws = string.IsNullOrWhiteSpace(workspaceId) ? "default" : workspaceId!;
        var coverage = _capabilities.GetCoverageReport(new CoverageReportRequest
        {
            WorkspaceId = ws,
            CoverageTier = CommandCoverageTiers.Baseline
        });
        var scripts = _capabilities.GetScriptCatalog(ws);

        return new ExternalAiGatewayCatalogResponse
        {
            WorkspaceId = ws,
            Coverage = coverage,
            Scripts = scripts.Scripts?.ToList() ?? new List<ScriptCatalogEntry>(),
            SupportedRoutes = new List<string>
            {
                "/api/external-ai/status",
                "/api/external-ai/catalog",
                "/api/external-ai/chat",
                "/api/external-ai/missions/{missionId}",
                "/api/external-ai/missions/{missionId}/events",
                "/api/external-ai/missions/{missionId}/approve",
                "/api/external-ai/missions/{missionId}/reject",
                "/api/external-ai/missions/{missionId}/cancel",
                "/api/external-ai/missions/{missionId}/resume"
            }
        };
    }

    public async Task WriteMissionEventsSseAsync(string missionId, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Content-Type"] = "text/event-stream";
        response.Headers["X-Accel-Buffering"] = "no";

        var emittedVersion = 0L;
        var stopwatch = Stopwatch.StartNew();
        await using var subscription = _eventBus.Subscribe(missionId);

        var replayEvents = await _orchestrator.GetEventsAsync(missionId, cancellationToken).ConfigureAwait(false);
        foreach (var record in replayEvents.Where(x => x.Version > emittedVersion))
        {
            emittedVersion = record.Version;
            await WriteSseRecordAsync(response, record, cancellationToken).ConfigureAwait(false);
            if (record.Terminal)
            {
                return;
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (stopwatch.ElapsedMilliseconds >= _settings.StreamingIdleTimeoutMs)
            {
                return;
            }

            if (await subscription.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (subscription.Reader.TryRead(out var record))
                {
                    if (record.Version <= emittedVersion)
                    {
                        continue;
                    }

                    emittedVersion = record.Version;
                    await WriteSseRecordAsync(response, record, cancellationToken).ConfigureAwait(false);
                    stopwatch.Restart();
                    if (record.Terminal)
                    {
                        return;
                    }
                }
                continue;
            }

            await Task.Delay(_settings.StreamingPollIntervalMs, cancellationToken).ConfigureAwait(false);
            var catchUpEvents = await _orchestrator.GetEventsAsync(missionId, cancellationToken).ConfigureAwait(false);
            foreach (var record in catchUpEvents.Where(x => x.Version > emittedVersion))
            {
                emittedVersion = record.Version;
                await WriteSseRecordAsync(response, record, cancellationToken).ConfigureAwait(false);
                if (record.Terminal)
                {
                    return;
                }
            }
        }
    }

    private static async Task WriteSseRecordAsync(HttpResponse response, MissionEventRecord record, CancellationToken cancellationToken)
    {
        var payload = JsonUtil.Serialize(new ExternalAiMissionEvent
        {
            Version = record.Version,
            EventType = record.EventType,
            OccurredUtc = record.OccurredUtc,
            PayloadJson = record.PayloadJson,
            Terminal = record.Terminal
        });
        await response.WriteAsync($"id: {record.Version}\n", cancellationToken).ConfigureAwait(false);
        await response.WriteAsync($"event: {record.EventType}\n", cancellationToken).ConfigureAwait(false);
        await response.WriteAsync($"data: {payload}\n\n", cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static EnvelopeMetadata BuildMeta(string missionId, string? sessionId, string? actorId, string? documentKey, string? targetDocument, string? targetView, int timeoutMs)
    {
        return new EnvelopeMetadata
        {
            MissionId = missionId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ActorId = string.IsNullOrWhiteSpace(actorId) ? "external-ai" : actorId,
            SessionId = sessionId ?? string.Empty,
            DocumentKey = documentKey ?? string.Empty,
            TargetDocument = targetDocument ?? string.Empty,
            TargetView = targetView ?? string.Empty,
            RequestedAtUtc = DateTime.UtcNow.ToString("O"),
            TimeoutMs = timeoutMs > 0 ? timeoutMs : 120_000
        };
    }

    private static MissionCommandInput BuildCommandInput(string missionId, ExternalAiMissionCommandRequest? request, string commandName)
    {
        request ??= new ExternalAiMissionCommandRequest();
        return new MissionCommandInput
        {
            MissionId = missionId,
            CommandName = commandName,
            Meta = BuildMeta(missionId, request.SessionId, request.ActorId, request.DocumentKey, request.TargetDocument, request.TargetView, request.TimeoutMs),
            Note = request.Note ?? string.Empty,
            ApprovalToken = request.ApprovalToken ?? string.Empty,
            PreviewRunId = request.PreviewRunId ?? string.Empty,
            ExpectedContextJson = request.ExpectedContextJson ?? string.Empty,
            AllowMutations = request.AllowMutations,
            RecoveryBranchId = request.RecoveryBranchId ?? string.Empty
        };
    }

    private static ExternalAiMissionResponse ToResponse(MissionSnapshot snapshot, IReadOnlyList<MissionEventRecord> events, KernelInvocationResult result, LlmProviderConfiguration profile)
    {
        var worker = TryReadWorkerResponse(snapshot.ResponseJson);
        return new ExternalAiMissionResponse
        {
            MissionId = snapshot.MissionId,
            SessionId = worker?.SessionId ?? snapshot.SessionId,
            State = snapshot.State,
            Succeeded = result.Succeeded,
            StatusCode = result.StatusCode,
            ResponseText = snapshot.ResponseText ?? string.Empty,
            PayloadJson = snapshot.ResponseJson ?? string.Empty,
            ApprovalToken = string.IsNullOrWhiteSpace(result.ApprovalToken) ? snapshot.ApprovalToken : result.ApprovalToken,
            PreviewRunId = string.IsNullOrWhiteSpace(result.PreviewRunId) ? snapshot.PreviewRunId : result.PreviewRunId,
            HasPendingApproval = !string.IsNullOrWhiteSpace(worker?.PendingApproval?.PendingActionId),
            PendingActionId = worker?.PendingApproval?.PendingActionId ?? string.Empty,
            SuggestedSurface = worker?.SurfaceHint?.SurfaceId ?? string.Empty,
            ArtifactRefs = worker?.ArtifactRefs?.ToList() ?? result.Artifacts?.ToList() ?? new List<string>(),
            Diagnostics = result.Diagnostics?.ToList() ?? new List<string>(),
            ConfiguredProvider = worker?.ConfiguredProvider ?? profile?.ConfiguredProvider ?? string.Empty,
            PlannerModel = worker?.PlannerModel ?? profile?.PlannerPrimaryModel ?? string.Empty,
            ResponseModel = worker?.ResponseModel ?? profile?.ResponseModel ?? string.Empty,
            ReasoningMode = string.IsNullOrWhiteSpace(worker?.ReasoningMode)
                ? profile?.ReasoningMode ?? WorkerReasoningModes.RuleFirst
                : worker.ReasoningMode,
            FlowState = FirstNonEmpty(worker?.Stage, snapshot.FlowState),
            GroundingLevel = FirstNonEmpty(worker?.ContextSummary?.GroundingLevel, snapshot.GroundingLevel),
            PlanningSummary = FirstNonEmpty(worker?.PlanSummary, snapshot.PlanSummary),
            PlannerTraceSummary = snapshot.PlannerTraceSummary,
            ApprovalRequired = snapshot.ApprovalRequired || !string.IsNullOrWhiteSpace(worker?.PendingApproval?.PendingActionId),
            ChosenToolSequence = snapshot.ChosenToolSequence?.ToList() ?? new List<string>(),
            AutonomyMode = FirstNonEmpty(snapshot.AutonomyMode, worker?.AutonomyMode, WorkerAutonomyModes.Bounded),
            EvidenceRefs = (worker?.EvidenceItems?.Select(item => item.ArtifactRef) ?? Array.Empty<string>())
                .Concat(worker?.ContextSummary?.GroundingRefs ?? new List<string>())
                .Concat(snapshot.EvidenceRefs ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Events = events.Select(record => new ExternalAiMissionEvent
            {
                Version = record.Version,
                EventType = record.EventType,
                OccurredUtc = record.OccurredUtc,
                PayloadJson = record.PayloadJson,
                Terminal = record.Terminal
            }).ToList()
        };
    }

    private static WorkerResponse? TryReadWorkerResponse(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonUtil.DeserializeRequired<WorkerResponse>(payloadJson);
        }
        catch
        {
            return null;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static SessionRuntimeHealthResponse? TryReadRuntimeProfile(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonUtil.Deserialize<SessionRuntimeHealthResponse>(payloadJson);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> BuildStatusWarnings(
        RuntimeHealthReport health,
        LlmProviderConfiguration profile,
        SessionRuntimeHealthResponse? runtimeProfile,
        out bool restartRequired)
    {
        var warnings = new List<string>();
        restartRequired = runtimeProfile?.RestartRequired ?? false;

        if (runtimeProfile?.RuntimeWarnings != null)
        {
            warnings.AddRange(runtimeProfile.RuntimeWarnings.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        if (runtimeProfile != null)
        {
            if (!string.IsNullOrWhiteSpace(runtimeProfile.ConfiguredProvider)
                && !string.IsNullOrWhiteSpace(profile.ConfiguredProvider)
                && !string.Equals(runtimeProfile.ConfiguredProvider, profile.ConfiguredProvider, StringComparison.OrdinalIgnoreCase))
            {
                restartRequired = true;
                warnings.Add($"Revit runtime is using {runtimeProfile.ConfiguredProvider} while WorkerHost is configured for {profile.ConfiguredProvider}.");
            }

            if (!string.IsNullOrWhiteSpace(runtimeProfile.PlannerModel)
                && !string.IsNullOrWhiteSpace(profile.PlannerPrimaryModel)
                && !string.Equals(runtimeProfile.PlannerModel, profile.PlannerPrimaryModel, StringComparison.OrdinalIgnoreCase))
            {
                restartRequired = true;
                warnings.Add($"Revit runtime planner model '{runtimeProfile.PlannerModel}' does not match WorkerHost planner model '{profile.PlannerPrimaryModel}'.");
            }

            if (!string.IsNullOrWhiteSpace(runtimeProfile.ResponseModel)
                && !string.IsNullOrWhiteSpace(profile.ResponseModel)
                && !string.Equals(runtimeProfile.ResponseModel, profile.ResponseModel, StringComparison.OrdinalIgnoreCase))
            {
                restartRequired = true;
                warnings.Add($"Revit runtime response model '{runtimeProfile.ResponseModel}' does not match WorkerHost response model '{profile.ResponseModel}'.");
            }
        }

        return warnings
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

 [DataContract]
internal sealed class ExternalAiChatRequest
{
    [DataMember(Order = 1)]
    public string MissionId { get; set; } = string.Empty;
    [DataMember(Order = 2)]
    public string SessionId { get; set; } = string.Empty;
    [DataMember(Order = 3)]
    public string Message { get; set; } = string.Empty;
    [DataMember(Order = 4)]
    public string PersonaId { get; set; } = string.Empty;
    [DataMember(Order = 5)]
    public bool ContinueMission { get; set; } = true;
    [DataMember(Order = 6)]
    public string ActorId { get; set; } = "external-ai";
    [DataMember(Order = 7)]
    public string DocumentKey { get; set; } = string.Empty;
    [DataMember(Order = 8)]
    public string TargetDocument { get; set; } = string.Empty;
    [DataMember(Order = 9)]
    public string TargetView { get; set; } = string.Empty;
    [DataMember(Order = 10)]
    public int TimeoutMs { get; set; } = 120_000;
}

[DataContract]
internal sealed class ExternalAiMissionCommandRequest
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;
    [DataMember(Order = 2)]
    public string ActorId { get; set; } = "external-ai";
    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;
    [DataMember(Order = 4)]
    public string TargetDocument { get; set; } = string.Empty;
    [DataMember(Order = 5)]
    public string TargetView { get; set; } = string.Empty;
    [DataMember(Order = 6)]
    public int TimeoutMs { get; set; } = 120_000;
    [DataMember(Order = 7)]
    public string Note { get; set; } = string.Empty;
    [DataMember(Order = 8)]
    public string ApprovalToken { get; set; } = string.Empty;
    [DataMember(Order = 9)]
    public string PreviewRunId { get; set; } = string.Empty;
    [DataMember(Order = 10)]
    public string ExpectedContextJson { get; set; } = string.Empty;
    [DataMember(Order = 11)]
    public bool AllowMutations { get; set; } = true;
    [DataMember(Order = 12)]
    public string RecoveryBranchId { get; set; } = string.Empty;
}

internal sealed class ExternalAiMissionResponse
{
    public string MissionId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string ResponseText { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string ApprovalToken { get; set; } = string.Empty;
    public string PreviewRunId { get; set; } = string.Empty;
    public bool HasPendingApproval { get; set; }
    public string PendingActionId { get; set; } = string.Empty;
    public string SuggestedSurface { get; set; } = string.Empty;
    public List<string> ArtifactRefs { get; set; } = new List<string>();
    public List<string> Diagnostics { get; set; } = new List<string>();
    public string ConfiguredProvider { get; set; } = string.Empty;
    public string PlannerModel { get; set; } = string.Empty;
    public string ResponseModel { get; set; } = string.Empty;
    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;
    public string FlowState { get; set; } = string.Empty;
    public string GroundingLevel { get; set; } = string.Empty;
    public string PlanningSummary { get; set; } = string.Empty;
    public string PlannerTraceSummary { get; set; } = string.Empty;
    public bool ApprovalRequired { get; set; }
    public List<string> ChosenToolSequence { get; set; } = new List<string>();
    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;
    public List<string> EvidenceRefs { get; set; } = new List<string>();
    public List<ExternalAiMissionEvent> Events { get; set; } = new List<ExternalAiMissionEvent>();
}

[DataContract]
internal sealed class ExternalAiMissionEvent
{
    [DataMember(Order = 1)]
    public long Version { get; set; }
    [DataMember(Order = 2)]
    public string EventType { get; set; } = string.Empty;
    [DataMember(Order = 3)]
    public string OccurredUtc { get; set; } = string.Empty;
    [DataMember(Order = 4)]
    public string PayloadJson { get; set; } = string.Empty;
    [DataMember(Order = 5)]
    public bool Terminal { get; set; }
}

internal sealed class ExternalAiGatewayCatalogResponse
{
    public string WorkspaceId { get; set; } = string.Empty;
    public CoverageReportResponse Coverage { get; set; } = new CoverageReportResponse();
    public List<ScriptCatalogEntry> Scripts { get; set; } = new List<ScriptCatalogEntry>();
    public List<string> SupportedRoutes { get; set; } = new List<string>();
}

internal sealed class ExternalAiGatewayStatusResponse
{
    public RuntimeHealthReport Health { get; set; } = new RuntimeHealthReport();
    public string ConfiguredProvider { get; set; } = string.Empty;
    public string PlannerModel { get; set; } = string.Empty;
    public string ResponseModel { get; set; } = string.Empty;
    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;
    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;
    public string SecretSourceKind { get; set; } = string.Empty;
    public string RuntimeConfiguredProvider { get; set; } = string.Empty;
    public string RuntimePlannerModel { get; set; } = string.Empty;
    public string RuntimeResponseModel { get; set; } = string.Empty;
    public bool RestartRequired { get; set; }
    public List<string> StatusWarnings { get; set; } = new List<string>();
}
