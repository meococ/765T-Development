using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ChatTimelineProjectorTests
{
    [Fact]
    public void Projector_Renders_Single_Approval_Turn_And_Deduplicates_Artifacts()
    {
        var worker = new WorkerResponse
        {
            SessionId = "session-1",
            MissionId = "mission-1",
            MissionStatus = WorkerMissionStates.AwaitingApproval,
            Stage = WorkerFlowStages.Preview,
            PlanSummary = "Preview generated for sheet package changes.",
            PendingApproval = new PendingApprovalRef
            {
                PendingActionId = "approval-1",
                Summary = "Review the pending sheet package preview.",
                ExecutionTier = WorkerExecutionTiers.Tier1
            },
            FallbackProposal = new FallbackArtifactProposal
            {
                Summary = "A fallback artifact was also prepared.",
                ArtifactKinds = { FallbackArtifactKinds.Playbook },
                ArtifactPaths = { @"C:\temp\sheet-package.playbook.json" }
            }
        };
        worker.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.User,
            Content = "Create sheets for the permit set."
        });
        worker.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.Worker,
            Content = "I prepared a guarded preview for the sheet package."
        });
        worker.ArtifactRefs.Add(@"C:\temp\sheet-package.playbook.json");
        worker.ArtifactRefs.Add(@"C:\temp\sheet-package.playbook.json");

        var missionResponse = new WorkerHostMissionResponse
        {
            SessionId = worker.SessionId,
            MissionId = worker.MissionId,
            State = WorkerMissionStates.AwaitingApproval,
            HasPendingApproval = true,
            ApprovalToken = "token-1",
            PreviewRunId = "preview-1",
            PayloadJson = JsonUtil.Serialize(worker),
            Events =
            {
                new WorkerHostMissionEvent
                {
                    Version = 1,
                    EventType = "TaskStarted",
                    OccurredUtc = "2026-03-23T01:00:00Z"
                },
                new WorkerHostMissionEvent
                {
                    Version = 2,
                    EventType = "PreviewGenerated",
                    OccurredUtc = "2026-03-23T01:00:05Z"
                }
            }
        };

        var store = new ChatSessionStore();
        store.SeedFromWorkerResponse(worker);
        store.ApplyMissionResponse(missionResponse);

        var vm = new ChatTimelineProjector().Project(store);

        var approvalTurns = vm.Entries
            .Where(x => string.Equals(x.Kind, TimelineEntryKinds.SystemStateTurn, StringComparison.Ordinal))
            .Select(x => x.SystemTurn)
            .Where(x => string.Equals(x.TurnKind, SystemTurnKinds.Approval, StringComparison.Ordinal))
            .ToList();
        var artifactRows = vm.Entries.Where(x => string.Equals(x.Kind, TimelineEntryKinds.ArtifactRow, StringComparison.Ordinal)).ToList();
        var traceTurns = vm.Entries.Where(x => string.Equals(x.Kind, TimelineEntryKinds.MissionTraceTurn, StringComparison.Ordinal)).ToList();

        Assert.Single(approvalTurns);
        Assert.Equal("Review the pending sheet package preview.", approvalTurns[0].Summary);
        Assert.Equal(2, traceTurns.Single().Trace.Events.Count);
        Assert.Single(artifactRows);
        Assert.Equal(@"C:\temp\sheet-package.playbook.json", artifactRows[0].Artifact.Path);
    }

    [Fact]
    public void Projector_Renders_Compact_Onboarding_Turn_When_Workspace_Is_Not_Ready()
    {
        var worker = new WorkerResponse
        {
            SessionId = "session-2",
            MissionId = "mission-2",
            OnboardingStatus = new OnboardingStatusDto
            {
                WorkspaceId = "workspace-alpha",
                InitStatus = ProjectOnboardingStatuses.NotInitialized,
                DeepScanStatus = ProjectDeepScanStatuses.NotStarted,
                Summary = "Project context has not been initialized yet."
            }
        };

        var store = new ChatSessionStore();
        store.SeedFromWorkerResponse(worker);

        var vm = new ChatTimelineProjector().Project(store);
        var onboardingTurn = vm.Entries
            .Where(x => string.Equals(x.Kind, TimelineEntryKinds.SystemStateTurn, StringComparison.Ordinal))
            .Select(x => x.SystemTurn)
            .Single();

        Assert.Equal(SystemTurnKinds.Onboarding, onboardingTurn.TurnKind);
        Assert.Equal("Project context initialization needed", onboardingTurn.Title);
        var initAction = Assert.Single(onboardingTurn.Actions, x => string.Equals(x.ActionKind, SystemTurnActionKinds.InitWorkspace, StringComparison.Ordinal));
        Assert.Equal(string.Empty, initAction.CommandText);
        Assert.DoesNotContain(vm.Entries, x => string.Equals(x.Kind, TimelineEntryKinds.ArtifactRow, StringComparison.Ordinal));
    }

    [Fact]
    public void Projector_Hides_Onboarding_Turn_After_Conversation_Starts()
    {
        var worker = new WorkerResponse
        {
            SessionId = "session-3",
            MissionId = "mission-3",
            OnboardingStatus = new OnboardingStatusDto
            {
                WorkspaceId = "workspace-alpha",
                InitStatus = ProjectOnboardingStatuses.NotInitialized,
                DeepScanStatus = ProjectDeepScanStatuses.NotStarted,
                Summary = "Project context has not been initialized yet."
            }
        };
        worker.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.User,
            Content = "chao"
        });
        worker.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.Worker,
            Content = "Em dang san sang."
        });

        var store = new ChatSessionStore();
        store.SeedFromWorkerResponse(worker);

        var vm = new ChatTimelineProjector().Project(store);

        Assert.DoesNotContain(vm.Entries, x =>
            string.Equals(x.Kind, TimelineEntryKinds.SystemStateTurn, StringComparison.Ordinal)
            && string.Equals(x.SystemTurn.TurnKind, SystemTurnKinds.Onboarding, StringComparison.Ordinal));
    }

    [Fact]
    public void BeginUserTurn_Persists_Local_User_Message_Without_Duplicating_Pending_Bubble()
    {
        var store = new ChatSessionStore();
        store.SeedFromWorkerResponse(new WorkerResponse
        {
            SessionId = "session-local",
            MissionId = "mission-local"
        });

        store.BeginUserTurn("check model health");

        var vm = new ChatTimelineProjector().Project(store);
        var userMessages = vm.Entries
            .Where(x => string.Equals(x.Kind, TimelineEntryKinds.UserMessage, StringComparison.Ordinal))
            .Select(x => x.Message.Content)
            .ToList();
        var traceEntry = Assert.Single(vm.Entries, x => string.Equals(x.Kind, TimelineEntryKinds.MissionTraceTurn, StringComparison.Ordinal));
        var trace = traceEntry.Trace;

        Assert.Single(userMessages);
        Assert.Equal("check model health", userMessages[0]);
        Assert.True(vm.IsBusy);
        Assert.Equal(WorkerMissionStates.Understanding, store.LatestMissionResponse.State);
        Assert.Equal("Waiting", Assert.Single(trace.Events).EventType);
    }

    [Fact]
    public void Projector_Renders_Streaming_Assistant_Placeholder_For_InFlight_Mission()
    {
        var store = new ChatSessionStore();
        store.SeedFromWorkerResponse(new WorkerResponse
        {
            SessionId = "session-stream",
            MissionId = "mission-stream",
            ContextSummary = new WorkerContextSummary
            {
                DocumentTitle = "Model A",
                ActiveViewName = "{3D}"
            }
        });

        store.BeginUserTurn("hi");
        store.BeginMissionStream("mission-stream");

        var vm = new ChatTimelineProjector().Project(store);
        var assistantTurn = Assert.Single(vm.Entries, x =>
            string.Equals(x.Kind, TimelineEntryKinds.AssistantMessage, StringComparison.Ordinal)
            && string.Equals(x.Message.Role, WorkerMessageRoles.Worker, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Message.StatusCode, "STREAMING", StringComparison.OrdinalIgnoreCase));

        Assert.True(vm.IsBusy);
        Assert.Contains("Reading context", assistantTurn.Message.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projector_Updates_Streaming_Assistant_Copy_From_Mission_Event()
    {
        var store = new ChatSessionStore();
        store.SeedFromWorkerResponse(new WorkerResponse
        {
            SessionId = "session-stream-2",
            MissionId = "mission-stream-2"
        });

        store.BeginUserTurn("kiem tra context hien tai");
        store.BeginMissionStream("mission-stream-2");
        store.ApplyMissionEvent(new WorkerHostMissionEvent
        {
            Version = 1,
            EventType = "PlanBuilt",
            OccurredUtc = "2026-03-25T01:00:00Z"
        });

        var vm = new ChatTimelineProjector().Project(store);
        var assistantTurn = Assert.Single(vm.Entries, x =>
            string.Equals(x.Kind, TimelineEntryKinds.AssistantMessage, StringComparison.Ordinal)
            && string.Equals(x.Message.StatusCode, "STREAMING", StringComparison.OrdinalIgnoreCase));
        var traceTurn = Assert.Single(vm.Entries, x => string.Equals(x.Kind, TimelineEntryKinds.MissionTraceTurn, StringComparison.Ordinal));

        Assert.Contains("Composing response", assistantTurn.Message.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PlanBuilt", Assert.Single(traceTurn.Trace.Events).EventType);
        Assert.Equal("Safe plan built for this turn.", Assert.Single(traceTurn.Trace.Events).Summary);
    }

    [Fact]
    public void Projector_Renders_DeepScan_Action_As_Native_Action_Not_Command_Text()
    {
        var worker = new WorkerResponse
        {
            SessionId = "session-4",
            MissionId = "mission-4",
            OnboardingStatus = new OnboardingStatusDto
            {
                WorkspaceId = "workspace-alpha",
                InitStatus = ProjectOnboardingStatuses.Initialized,
                DeepScanStatus = ProjectDeepScanStatuses.NotStarted,
                Summary = "Workspace has basic context."
            }
        };

        var store = new ChatSessionStore();
        store.SeedFromWorkerResponse(worker);

        var vm = new ChatTimelineProjector().Project(store);
        var onboardingTurn = vm.Entries
            .Where(x => string.Equals(x.Kind, TimelineEntryKinds.SystemStateTurn, StringComparison.Ordinal))
            .Select(x => x.SystemTurn)
            .Single();
        var deepScanAction = Assert.Single(onboardingTurn.Actions, x => string.Equals(x.ActionKind, SystemTurnActionKinds.RunDeepScan, StringComparison.Ordinal));

        Assert.Equal(string.Empty, deepScanAction.CommandText);
    }

    [Fact]
    public async Task WorkerHostMissionClient_Surfaces_ErrorEnvelope_Message()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"statusCode\":\"MISSION_ERROR\",\"message\":\"Planner failed validation.\"}",
                Encoding.UTF8,
                "application/json")
        });
        using var client = new WorkerHostMissionClient("http://localhost:50765/", handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetMissionAsync("mission-404", CancellationToken.None));

        Assert.Contains("Planner failed validation.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkerHostMissionClient_Surfaces_Runtime_Readiness_Summary()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"readinessSummary\":\"WorkerHost ready for standalone chat only; live Revit execution unavailable.\"}",
                Encoding.UTF8,
                "application/json")
        });
        using var client = new WorkerHostMissionClient("http://localhost:50765/", handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetMissionAsync("mission-standalone", CancellationToken.None));

        Assert.Contains("standalone chat only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkerHostMissionClient_Deserializes_Gateway_Readiness_Status()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{" +
                "\"Health\":{" +
                "\"Ready\":true," +
                "\"StandaloneChatReady\":true," +
                "\"LiveRevitReady\":false," +
                "\"Degraded\":true," +
                "\"ReadinessSummary\":\"WorkerHost ready for standalone chat only; live Revit execution unavailable.\"," +
                "\"RuntimeTopology\":\"workerhost_public_control_plane + revit_private_kernel\"}," +
                "\"SupportsTaskRuntime\":false," +
                "\"ConfiguredProvider\":\"RULE FIRST\"," +
                "\"PlannerModel\":\"\"," +
                "\"ResponseModel\":\"\"," +
                "\"ReasoningMode\":\"RULE_FIRST\"," +
                "\"SecretSourceKind\":\"none\"}",
                Encoding.UTF8,
                "application/json")
        });
        using var client = new WorkerHostMissionClient("http://localhost:50765/", handler);

        var status = await client.GetStatusAsync(CancellationToken.None);

        Assert.True(status.Health.StandaloneChatReady);
        Assert.False(status.Health.LiveRevitReady);
        Assert.False(status.SupportsTaskRuntime);
        Assert.Contains("standalone chat only", status.Health.ReadinessSummary, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
