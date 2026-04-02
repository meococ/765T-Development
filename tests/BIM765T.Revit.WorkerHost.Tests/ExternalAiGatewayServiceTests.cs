using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.WorkerHost.Capabilities;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.ExternalAi;
using BIM765T.Revit.WorkerHost.Health;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.WorkerHost.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class ExternalAiGatewayServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BIM765T.ExternalAiGatewayTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }

    [Fact]
    public void GetCatalog_Returns_Routes_And_Script_Catalog()
    {
        var gateway = CreateGateway();

        var catalog = gateway.GetCatalog("default");

        Assert.Equal("default", catalog.WorkspaceId);
        Assert.NotNull(catalog.Coverage);
        Assert.NotEmpty(catalog.SupportedRoutes);
        Assert.Contains("/api/external-ai/chat", catalog.SupportedRoutes);
        Assert.Contains("/api/external-ai/missions/{missionId}/events", catalog.SupportedRoutes);
        Assert.Empty(catalog.Scripts);
    }

    [Fact]
    public async Task GetStatus_Returns_Runtime_And_Llm_Profile_Metadata()
    {
        var gateway = CreateGateway();

        var status = await gateway.GetStatusAsync(CancellationToken.None);

        Assert.NotNull(status.Health);
        Assert.True(status.Health.StandaloneChatReady);
        Assert.True(status.SupportsTaskRuntime);
        Assert.Equal(LlmProviderKinds.RuleFirst, status.ConfiguredProvider);
        Assert.Equal(WorkerReasoningModes.RuleFirst, status.ReasoningMode);
        Assert.Equal(WorkerAutonomyModes.Ship, status.AutonomyMode);
        Assert.Contains(status.Health.Diagnostics, value => value.Contains("canonical_public_ingress", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStatus_WhenRuntimeDiffersFromConfiguredProfile_RequestsRestart()
    {
        var gateway = CreateGateway(
            new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new SessionRuntimeHealthResponse
                    {
                        SupportsTaskRuntime = true,
                        ToolCount = 245,
                        ConfiguredProvider = LlmProviderKinds.Anthropic,
                        PlannerModel = "claude-sonnet-4-20250514",
                        ResponseModel = "claude-sonnet-4-20250514",
                        ReasoningMode = WorkerReasoningModes.LlmValidated
                    })
                }),
            new LlmProviderConfiguration
            {
                IsConfigured = true,
                ConfiguredProvider = LlmProviderKinds.MiniMax,
                ProviderKind = "openai_compatible",
                PlannerPrimaryModel = "MiniMax-M2.7-highspeed",
                PlannerFallbackModel = "MiniMax-M2.7",
                ResponseModel = "MiniMax-M2.7-highspeed",
                SecretSourceKind = LlmSecretSourceKinds.Environment
            });

        var status = await gateway.GetStatusAsync(CancellationToken.None);

        Assert.True(status.RestartRequired);
        Assert.Equal(LlmProviderKinds.MiniMax, status.ConfiguredProvider);
        Assert.Equal(LlmProviderKinds.Anthropic, status.RuntimeConfiguredProvider);
        Assert.Equal("MiniMax-M2.7-highspeed", status.PlannerModel);
        Assert.Equal("claude-sonnet-4-20250514", status.RuntimePlannerModel);
        Assert.Contains(status.StatusWarnings, warning => warning.Contains("Revit runtime is using ANTHROPIC", StringComparison.Ordinal));
        Assert.Contains(status.StatusWarnings, warning => warning.Contains("does not match WorkerHost planner model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStatus_DoesNotRequestRestart_WhenRuntimeProfileMatches_ConfigEvenIfCatalogHeuristicFlagsStale()
    {
        var gateway = CreateGateway(
            new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new SessionRuntimeHealthResponse
                    {
                        SupportsTaskRuntime = true,
                        ToolCount = 235,
                        ConfiguredProvider = LlmProviderKinds.MiniMax,
                        PlannerModel = "MiniMax-M2.7-highspeed",
                        ResponseModel = "MiniMax-M2.7-highspeed",
                        ReasoningMode = WorkerReasoningModes.LlmValidated
                    })
                }),
            new LlmProviderConfiguration
            {
                IsConfigured = true,
                ConfiguredProvider = LlmProviderKinds.MiniMax,
                ProviderKind = "openai_compatible",
                PlannerPrimaryModel = "MiniMax-M2.7-highspeed",
                PlannerFallbackModel = "MiniMax-M2.7",
                ResponseModel = "MiniMax-M2.7-highspeed",
                SecretSourceKind = LlmSecretSourceKinds.Environment
            });

        var status = await gateway.GetStatusAsync(CancellationToken.None);

        Assert.True(status.Health.RuntimeLooksStale);
        Assert.False(status.RestartRequired);
        Assert.Equal(LlmProviderKinds.MiniMax, status.ConfiguredProvider);
        Assert.Equal(LlmProviderKinds.MiniMax, status.RuntimeConfiguredProvider);
        Assert.Equal("MiniMax-M2.7-highspeed", status.PlannerModel);
        Assert.Equal("MiniMax-M2.7-highspeed", status.RuntimePlannerModel);
        Assert.Empty(status.StatusWarnings);
    }

    [Fact]
    public async Task SubmitChat_Then_Approve_RoundTrips_Mission_State()
    {
        var gateway = CreateGateway(
            new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    ConfirmationRequired = true,
                    StatusCode = StatusCodes.TaskApprovalRequired,
                    ApprovalToken = "approval-01",
                    PreviewRunId = "preview-01",
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-01",
                        MissionStatus = WorkerMissionStates.AwaitingApproval,
                        Messages = new List<WorkerChatMessage>
                        {
                            new WorkerChatMessage
                            {
                                Role = WorkerMessageRoles.Worker,
                                Content = "Dang cho phe duyet."
                            }
                        },
                        PendingApproval = new PendingApprovalRef
                        {
                            PendingActionId = "pending-01",
                            ToolName = ToolNames.CommandExecuteSafe,
                            Summary = "Preview da san sang."
                        },
                        SurfaceHint = new WorkerSurfaceHint
                        {
                            SurfaceId = WorkerSurfaceIds.Assistant
                        }
                    })
                },
                new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-01",
                        MissionStatus = WorkerMissionStates.Completed,
                        Messages = new List<WorkerChatMessage>
                        {
                            new WorkerChatMessage
                            {
                                Role = WorkerMessageRoles.Worker,
                                Content = "Da xong."
                            }
                        },
                        ArtifactRefs = new List<string> { "artifacts/run-01.json" },
                        SurfaceHint = new WorkerSurfaceHint
                        {
                            SurfaceId = WorkerSurfaceIds.Evidence
                        }
                    })
                }));

        var submit = await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-01",
            SessionId = "session-01",
            Message = "tao sheet moi",
            PersonaId = "revit_worker"
        }, CancellationToken.None);

        Assert.Equal("mission-01", submit.MissionId);
        Assert.Equal(WorkerMissionStates.AwaitingApproval, submit.State);
        Assert.True(submit.HasPendingApproval);
        Assert.Equal("approval-01", submit.ApprovalToken);
        Assert.Equal("preview-01", submit.PreviewRunId);
        Assert.Equal(WorkerSurfaceIds.Assistant, submit.SuggestedSurface);

        var approve = await gateway.ApproveAsync("mission-01", new ExternalAiMissionCommandRequest
        {
            SessionId = "session-01",
            ApprovalToken = "approval-01",
            PreviewRunId = "preview-01",
            AllowMutations = true
        }, CancellationToken.None);

        Assert.Equal(WorkerMissionStates.Completed, approve.State);
        Assert.True(approve.Succeeded);
        Assert.False(approve.HasPendingApproval);
        Assert.Equal(WorkerSurfaceIds.Evidence, approve.SuggestedSurface);
        Assert.Contains("artifacts/run-01.json", approve.ArtifactRefs);
    }

    [Fact]
    public async Task ApproveAsync_Blocks_WhenAllowMutations_IsOmitted()
    {
        var gateway = CreateGateway(
            new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    ConfirmationRequired = true,
                    StatusCode = StatusCodes.TaskApprovalRequired,
                    ApprovalToken = "approval-01",
                    PreviewRunId = "preview-01",
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-allow",
                        MissionStatus = WorkerMissionStates.AwaitingApproval,
                        PendingApproval = new PendingApprovalRef
                        {
                            PendingActionId = "pending-allow",
                            ToolName = ToolNames.CommandExecuteSafe,
                            ExpectedContextJson = """{"doc":"A"}"""
                        }
                    })
                }));

        await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-allow",
            SessionId = "session-allow",
            Message = "create sheet moi"
        }, CancellationToken.None);

        var approve = await gateway.ApproveAsync("mission-allow", new ExternalAiMissionCommandRequest
        {
            SessionId = "session-allow",
            ApprovalToken = "approval-01",
            PreviewRunId = "preview-01",
            ExpectedContextJson = """{"doc":"A"}"""
        }, CancellationToken.None);

        Assert.Equal(WorkerMissionStates.Blocked, approve.State);
        Assert.False(approve.Succeeded);
        Assert.Equal(StatusCodes.PolicyBlocked, approve.StatusCode);
    }

    [Fact]
    public async Task SubmitChat_Surfaces_BoundedPlanner_Metadata_WithoutBreaking_Current_Routes()
    {
        var gateway = CreateGateway(
            new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-meta",
                        MissionStatus = WorkerMissionStates.Completed,
                        Stage = WorkerFlowStages.Scan,
                        PlanSummary = "Da tong hop overview grounded.",
                        ContextSummary = new WorkerContextSummary
                        {
                            DocumentTitle = "ProjectABC",
                            GroundingLevel = WorkerGroundingLevels.DeepScanGrounded,
                            GroundingRefs = new List<string> { "workspace:default", "artifact:deep-scan" }
                        },
                        EvidenceItems = new List<WorkerEvidenceItem>
                        {
                            new WorkerEvidenceItem
                            {
                                ArtifactRef = "artifact:deep-scan",
                                Title = "Deep scan",
                                Summary = "Project deep scan report"
                            }
                        }
                    })
                }));

        var response = await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-meta",
            SessionId = "session-meta",
            Message = "tong quan du an hien tai"
        }, CancellationToken.None);

        Assert.Equal(WorkerFlowStages.Scan, response.FlowState);
        Assert.Equal(WorkerGroundingLevels.DeepScanGrounded, response.GroundingLevel);
        Assert.Equal(WorkerAutonomyModes.Ship, response.AutonomyMode);
        Assert.NotEmpty(response.PlanningSummary);
        Assert.NotEmpty(response.ChosenToolSequence);
        Assert.Contains(response.EvidenceRefs, value => value.Contains("workspace:default", StringComparison.Ordinal) || value.Contains("artifact:deep-scan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WriteMissionEventsSseAsync_Stops_After_Terminal_Event()
    {
        var gateway = CreateGateway(
            new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-terminal-stream",
                        MissionStatus = WorkerMissionStates.Completed,
                        Messages = new List<WorkerChatMessage>
                        {
                            new WorkerChatMessage
                            {
                                Role = WorkerMessageRoles.Worker,
                                Content = "Da xong."
                            }
                        }
                    })
                }));

        await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-terminal-stream",
            SessionId = "session-terminal-stream",
            Message = "kiem tra stream terminal"
        }, CancellationToken.None);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await gateway.WriteMissionEventsSseAsync("mission-terminal-stream", httpContext.Response, CancellationToken.None);

        body.Position = 0;
        using var reader = new StreamReader(body);
        var content = await reader.ReadToEndAsync();

        Assert.Equal(1, CountOccurrences(content, "event: TaskCompleted"));
        Assert.Contains("event: TaskCompleted", content, StringComparison.Ordinal);
        Assert.DoesNotContain("event: ApprovalRequested", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteMissionEventsSseAsync_Writes_Streamed_Event_Frames()
    {
        var gateway = CreateGateway(
            new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-stream",
                        MissionStatus = WorkerMissionStates.Completed,
                        Messages = new List<WorkerChatMessage>
                        {
                            new WorkerChatMessage
                            {
                                Role = WorkerMessageRoles.Worker,
                                Content = "Da xong."
                            }
                        }
                    })
                }));

        await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-stream",
            SessionId = "session-stream",
            Message = "kiem tra context hien tai",
            PersonaId = "revit_worker"
        }, CancellationToken.None);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await gateway.WriteMissionEventsSseAsync("mission-stream", httpContext.Response, CancellationToken.None);

        body.Position = 0;
        using var reader = new StreamReader(body);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("event: TaskStarted", content, StringComparison.Ordinal);
        Assert.Contains("event: TaskCompleted", content, StringComparison.Ordinal);
        Assert.Contains("data:", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitChat_WhenConversational_UsesStandaloneFastPath()
    {
        var kernel = new QueueKernelClient(
            new KernelInvocationResult
            {
                Succeeded = false,
                StatusCode = StatusCodes.RevitUnavailable,
                Diagnostics = new List<string>
                {
                    "Revit kernel pipe unavailable. Open Revit with an active project and try again."
                }
            });
        var gateway = CreateGateway(kernel);

        var response = await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-kernel-offline",
            SessionId = "session-kernel-offline",
            Message = "hello"
        }, CancellationToken.None);

        Assert.Equal(WorkerMissionStates.Completed, response.State);
        Assert.True(response.Succeeded);
        Assert.Equal(StatusCodes.Ok, response.StatusCode);
        Assert.Contains("WorkerHost standalone mode", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(kernel.Requests);
        Assert.False(response.HasPendingApproval);
        Assert.NotEmpty(response.Events);
        Assert.Contains(response.Events, x => x.EventType == "TaskCompleted");
    }

    [Fact]
    public async Task SubmitChat_WhenActionRequested_StillReturnsRevitUnavailable()
    {
        var kernel = new QueueKernelClient(
            new KernelInvocationResult
            {
                Succeeded = false,
                StatusCode = StatusCodes.RevitUnavailable,
                Diagnostics = new List<string>
                {
                    "Revit kernel pipe unavailable. Open Revit with an active project and try again."
                }
            });
        var gateway = CreateGateway(kernel);

        var response = await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-kernel-offline",
            SessionId = "session-kernel-offline",
            Message = "create sheet moi"
        }, CancellationToken.None);

        Assert.Equal(WorkerMissionStates.Blocked, response.State);
        Assert.False(response.Succeeded);
        Assert.Equal(StatusCodes.RevitUnavailable, response.StatusCode);
        Assert.Contains("Open Revit with an active project", response.ResponseText, StringComparison.Ordinal);
        Assert.Single(kernel.Requests);
    }

    [Fact]
    public async Task SubmitChat_WhenLiveContextRequested_DoesNotUseStandaloneFastPath()
    {
        var kernel = new QueueKernelClient(
            new KernelInvocationResult
            {
                Succeeded = false,
                StatusCode = StatusCodes.RevitUnavailable,
                Diagnostics = new List<string>
                {
                    "Revit kernel pipe unavailable. Open Revit with an active project and try again."
                }
            });
        var gateway = CreateGateway(kernel);

        var response = await gateway.SubmitChatAsync(new ExternalAiChatRequest
        {
            MissionId = "mission-live-context",
            SessionId = "session-live-context",
            Message = "check current view trong revit"
        }, CancellationToken.None);

        Assert.Equal(WorkerMissionStates.Blocked, response.State);
        Assert.False(response.Succeeded);
        Assert.Equal(StatusCodes.RevitUnavailable, response.StatusCode);
        Assert.Single(kernel.Requests);
    }

    private ExternalAiGatewayService CreateGateway(IKernelClient? kernel = null, LlmProviderConfiguration? profile = null)
    {
        Directory.CreateDirectory(_root);

        var settings = new WorkerHostSettings
        {
            StateRootPath = Path.Combine(_root, "state"),
            LegacyStateRootPath = Path.Combine(_root, "legacy"),
            QdrantUrl = "http://127.0.0.1:6333",
            PublicPipeName = "bim765t-workerhost-test",
            KernelPipeName = "bim765t-kernel-test"
        };
        settings.EnsureCreated();

        var store = new SqliteMissionEventStore(settings.EventStorePath);
        var memory = new MemorySearchService(store, new ThrowingSemanticMemoryClient());
        var kernelClient = kernel ?? new QueueKernelClient(new KernelInvocationResult
        {
            Succeeded = true,
            StatusCode = StatusCodes.Ok,
            PayloadJson = JsonUtil.Serialize(new SessionRuntimeHealthResponse { SupportsTaskRuntime = true })
        });

        var packs = new PackCatalogService();
        var workspaces = new WorkspaceCatalogService();
        var standards = new StandardsCatalogService(packs, workspaces);
        var playbooks = new PlaybookOrchestrationService(
            new PlaybookLoaderService(PlaybookLoaderService.LoadAll(AppContext.BaseDirectory)),
            packs,
            workspaces,
            standards);
        var policies = new PolicyResolutionService(packs, workspaces);
        var specialists = new SpecialistRegistryService(packs, workspaces);
        var compiler = new CapabilityTaskCompilerService(
            new ToolCapabilitySearchService(),
            playbooks,
            policies,
            specialists);
        var curated = new CuratedScriptRegistryService(_root);
        var capabilityHost = new CapabilityHostService(
            policies,
            specialists,
            compiler,
            new CommandAtlasService(packs, workspaces, curated),
            curated,
            memory,
            kernelClient,
            settings);

        var runtimeHealth = new RuntimeHealthService(settings, store, kernelClient, new StubHttpClientFactory());
        var eventBus = new InMemoryMissionEventBus();
        var orchestrator = new MissionOrchestrator(
            store,
            eventBus,
            settings,
            kernelClient,
            new PlannerAgent(),
            new RetrieverAgent(memory),
            new ExecutionPolicyEvaluator(new SafetyAgent()),
            new VerifierAgent());

        return new ExternalAiGatewayService(
            orchestrator,
            eventBus,
            runtimeHealth,
            capabilityHost,
            settings,
            new StandaloneConversationService(memory),
            new FixedLlmProviderConfigResolver(profile ?? new LlmProviderConfiguration()),
            NullLogger<ExternalAiGatewayService>.Instance);
    }

    private static int CountOccurrences(string content, string value)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class ThrowingSemanticMemoryClient : ISemanticMemoryClient
    {
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Semantic memory intentionally unavailable for lexical fallback test.");
        }

        public Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Semantic memory intentionally unavailable for lexical fallback test.");
        }
    }

    private sealed class QueueKernelClient : IKernelClient
    {
        private readonly Queue<KernelInvocationResult> _responses;

        public QueueKernelClient(params KernelInvocationResult[] responses)
        {
            _responses = new Queue<KernelInvocationResult>(responses);
        }

        public List<KernelToolRequest> Requests { get; } = new List<KernelToolRequest>();

        public Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = "{}"
                });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler())
            {
                BaseAddress = new Uri("http://127.0.0.1:6333/")
            };
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private sealed class FixedLlmProviderConfigResolver : ILlmProviderConfigResolver
    {
        private readonly LlmProviderConfiguration _profile;

        public FixedLlmProviderConfigResolver(LlmProviderConfiguration profile)
        {
            _profile = profile;
        }

        public LlmProviderConfiguration Resolve()
        {
            return _profile;
        }
    }
}
