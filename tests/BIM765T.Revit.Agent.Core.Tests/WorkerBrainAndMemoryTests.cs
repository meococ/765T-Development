using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.Copilot.Core.Memory;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class WorkerBrainAndMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BIM765T-WorkerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void ConversationManager_Creates_Updates_And_Ends_Session()
    {
        var manager = new ConversationManager();

        var session = manager.GetOrCreateSession(string.Empty, WorkerPersonas.RevitWorker, WorkerClientSurfaces.Ui, "path:a");
        manager.AddMessage(session.SessionId, new WorkerChatMessage
        {
            Role = WorkerMessageRoles.User,
            Content = "kiem tra model health"
        });

        var listed = manager.ListSessions(10, includeEnded: false);
        var ended = manager.EndSession(session.SessionId);

        Assert.Single(listed);
        Assert.Equal(session.SessionId, listed[0].SessionId);
        Assert.Equal(WorkerSessionStates.Ended, ended.Status);
        Assert.True(manager.TryGetSession(session.SessionId, out var restored));
        Assert.NotNull(restored);
        Assert.Single(restored!.Messages);
    }

    [Fact]
    public void MissionCoordinator_Transitions_Through_Planned_AwaitingApproval_And_Completed()
    {
        var session = new WorkerConversationSessionState();
        var coordinator = new MissionCoordinator();
        var decision = new WorkerDecision
        {
            Intent = "mutation_request",
            Goal = "Preview purge unused",
            ReasoningSummary = "Rule-first routing.",
            PlanSummary = "Preview before execute.",
            DecisionRationale = "Mutation requires approval.",
            PlannedTools = new List<string> { ToolNames.AuditPurgeUnusedSafe },
            SuggestedActions = new List<WorkerActionCard>
            {
                new WorkerActionCard { Title = "Preview purge", IsPrimary = true }
            }
        };

        coordinator.EnsureMission(session, decision.Intent, decision.Goal, continueMission: true);
        coordinator.SetPlan(session, decision);
        coordinator.AwaitApproval(session, "Preview xong", decision.PlannedTools);
        coordinator.MarkRunning(session, "execute");
        coordinator.MarkVerifying(session, "Dang verify");
        coordinator.Complete(session, "Hoan tat");

        Assert.Equal(WorkerMissionStates.Completed, session.Mission.Status);
        Assert.Equal("Hoan tat", session.Mission.PlanSummary);
        Assert.Equal(decision.ReasoningSummary, session.Mission.ReasoningSummary);
        Assert.Equal(ToolNames.AuditPurgeUnusedSafe, Assert.Single(session.Mission.PlannedTools));
    }

    [Fact]
    public void IntentClassifier_Maps_Common_Worker_Intents()
    {
        var classifier = new IntentClassifier();

        Assert.Equal("qc_request", classifier.Classify("kiem tra model health", false).Intent);
        Assert.Equal("sheet_analysis_request", classifier.Classify("QC sheet A101", false).Intent);
        Assert.Equal("family_analysis_request", classifier.Classify("phan tich family \"Title Block\"", false).Intent);
        Assert.Equal("model_manage_request", classifier.Classify("preview purge unused", false).Intent);
        Assert.Equal("view_authoring_request", classifier.Classify("create 3d view", false).Intent);
        Assert.Equal("documentation_request", classifier.Classify("renumber sheet A101 to A201", false).Intent);
        Assert.Equal("documentation_request", classifier.Classify("place view on sheet A101", false).Intent);
        Assert.Equal("coordination_request", classifier.Classify("xu ly hard clash ong nuoc va dam", false).Intent);
        Assert.Equal("systems_request", classifier.Classify("kiem tra disconnected sanitary system", false).Intent);
        Assert.Equal("intent_compile_request", classifier.Classify("highlight toan bo cua tang 3 thieu Fire Rating", false).Intent);
        Assert.Equal("greeting", classifier.Classify("hi", false).Intent);
        Assert.Equal("identity_query", classifier.Classify("em la gi", false).Intent);
        Assert.Equal("approval", classifier.Classify("dong y", true).Intent);
        Assert.Equal("reject", classifier.Classify("tu choi", true).Intent);
    }

    [Fact]
    public void WorkerReasoningEngine_Routes_Qc_And_Approval_Correctly()
    {
        var personas = new PersonaRegistry();
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas);
        var session = new WorkerConversationSessionState
        {
            PersonaId = WorkerPersonas.QaReviewer
        };

        var qcDecision = engine.ProcessMessage(session, "kiem tra model health", continueMission: true);
        var identityDecision = engine.ProcessMessage(session, "em la gi", continueMission: true);
        session.PendingApprovalState = new WorkerPendingApprovalState { PendingActionId = "pending-1" };
        var approvalDecision = engine.ProcessMessage(session, "dong y", continueMission: true);

        Assert.Equal("qc_request", qcDecision.Intent);
        Assert.Contains(ToolNames.ReviewSmartQc, qcDecision.PlannedTools);
        Assert.Equal("identity_query", identityDecision.Intent);
        Assert.Contains("vai tro worker", identityDecision.PlanSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("approval", approvalDecision.Intent);
        Assert.Contains("pending_approval", approvalDecision.PlannedTools);
    }

    [Fact]
    public void WorkerReasoningEngine_Routes_Coordination_And_System_Intents_To_Capability_Tools()
    {
        var personas = new PersonaRegistry();
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas);
        var session = new WorkerConversationSessionState
        {
            PersonaId = WorkerPersonas.RevitWorker
        };

        var coordination = engine.ProcessMessage(session, "giai quyet hard clash ong nuoc va dam", continueMission: true);
        var systems = engine.ProcessMessage(session, "kiem tra disconnected sanitary system", continueMission: true);

        Assert.Equal("coordination_request", coordination.Intent);
        Assert.Contains(ToolNames.PolicyResolve, coordination.PlannedTools);
        Assert.Contains(ToolNames.IntentCompile, coordination.PlannedTools);

        Assert.Equal("systems_request", systems.Intent);
        Assert.Contains(ToolNames.SystemCaptureGraph, systems.PlannedTools);
        Assert.Contains(ToolNames.SpecialistResolve, systems.PlannedTools);
    }

    [Fact]
    public void WorkerReasoningEngine_Routes_Quick_View_Actions_To_Atlas_FastPath()
    {
        var personas = new PersonaRegistry();
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas);
        var session = new WorkerConversationSessionState
        {
            PersonaId = WorkerPersonas.RevitWorker
        };

        var decision = engine.ProcessMessage(session, "create 3d view", continueMission: true);

        Assert.Equal("view_authoring_request", decision.Intent);
        Assert.Contains(ToolNames.WorkflowQuickPlan, decision.PlannedTools);
        Assert.Contains(ToolNames.CommandExecuteSafe, decision.PlannedTools);
    }

    [Fact]
    public void SessionMemoryStore_Evicts_Oldest_And_Searches_By_Document()
    {
        var store = new SessionMemoryStore();
        const string sessionId = "session-1";

        for (var i = 0; i < 151; i++)
        {
            store.Add(sessionId, new SessionMemoryEntry
            {
                EntryId = "entry-" + i,
                Kind = i == 150 ? WorkerMemoryKinds.ToolResult : WorkerMemoryKinds.UserMessage,
                Content = i == 150 ? "qc summary for current model" : "message " + i,
                Tags = new List<string> { i == 150 ? "qc" : "chat" },
                DocumentKey = i == 150 ? "path:a" : "path:b",
                ToolName = i == 150 ? ToolNames.ReviewSmartQc : string.Empty,
                CreatedUtc = new DateTime(2026, 3, 20, 8, 0, 0, DateTimeKind.Utc).AddMinutes(i)
            });
        }

        var listed = store.List(sessionId, 200);
        var searched = store.Search(sessionId, "qc current", "path:a", 1);

        Assert.Equal(150, listed.Count);
        Assert.DoesNotContain(listed, x => string.Equals(x.EntryId, "entry-0", StringComparison.OrdinalIgnoreCase));
        Assert.Single(searched);
        Assert.Equal("entry-150", searched[0].EntryId);
    }

    [Fact]
    public void EpisodicMemoryStore_Saves_Loads_And_Searches()
    {
        var store = new EpisodicMemoryStore(new CopilotStatePaths(_root));
        var first = store.Save(new EpisodicRecord
        {
            EpisodeId = "episode-1",
            RunId = "run-1",
            MissionType = "qc_request",
            Outcome = "completed",
            KeyObservations = new List<string> { "3 warnings" },
            KeyDecisions = new List<string> { "giu read-only" },
            ToolSequence = new List<string> { ToolNames.ReviewSmartQc },
            ArtifactRefs = new List<string> { "artifacts/qc/report.json" },
            DocumentKey = "path:a",
            CreatedUtc = new DateTime(2026, 3, 20, 8, 0, 0, DateTimeKind.Utc)
        });
        store.Save(new EpisodicRecord
        {
            EpisodeId = "episode-2",
            RunId = "run-2",
            MissionType = "family_analysis_request",
            Outcome = "completed",
            KeyObservations = new List<string> { "nested family found" },
            KeyDecisions = new List<string> { "review connector mapping" },
            ToolSequence = new List<string> { ToolNames.FamilyXray },
            ArtifactRefs = new List<string> { "artifacts/family/xray.json" },
            DocumentKey = "path:b",
            CreatedUtc = new DateTime(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc)
        });

        var loaded = store.TryGet(first.EpisodeId, out var restored);
        var searched = store.Search("warnings qc", "path:a", "qc_request", 5);

        Assert.True(loaded);
        Assert.NotNull(restored);
        Assert.Equal("run-1", restored!.RunId);
        Assert.NotEmpty(searched);
        Assert.Equal("episode-1", searched[0].EpisodeId);
    }

    [Fact]
    public void PersonaRegistry_Loads_Json_Persona_And_Falls_Back_To_BuiltIn()
    {
        var personaDir = Path.Combine(_root, "personas");
        Directory.CreateDirectory(personaDir);
        File.WriteAllText(
            Path.Combine(personaDir, "custom.json"),
            JsonUtil.Serialize(new WorkerPersonaSummary
            {
                PersonaId = "custom_worker",
                DisplayName = "Custom Worker",
                Tone = "focused",
                Expertise = new List<string> { "qc" },
                Guardrails = new List<string> { "preview first" },
                GreetingTemplate = "Xin chao"
            }));

        var registry = new PersonaRegistry(personaDir);
        var custom = registry.Resolve("custom_worker");
        var fallback = registry.Resolve("unknown");

        Assert.Equal("custom_worker", custom.PersonaId);
        Assert.Equal("Custom Worker", custom.DisplayName);
        Assert.Equal(WorkerPersonas.RevitWorker, fallback.PersonaId);
    }

    // ── Phase 1: LLM integration tests ─────────────────────────

    [Fact]
    public void AnthropicLlmClient_Throws_On_Empty_ApiKey()
    {
        Assert.Throws<ArgumentException>(() =>
            new AnthropicLlmClient(new System.Net.Http.HttpClient(), ""));
        Assert.Throws<ArgumentException>(() =>
            new AnthropicLlmClient(new System.Net.Http.HttpClient(), "  "));
    }

    [Fact]
    public void AnthropicLlmClient_Throws_On_Null_HttpClient()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AnthropicLlmClient(null!, "sk-test-key"));
    }

    [Fact]
    public void AnthropicLlmClient_IsConfigured_Returns_True_With_Valid_Key()
    {
        var client = new AnthropicLlmClient(new System.Net.Http.HttpClient(), "sk-test-key");
        Assert.True(client.IsConfigured);
        Assert.Equal("claude-sonnet-4-20250514", client.Model);
    }

    [Fact]
    public void AnthropicLlmClient_Respects_Custom_Model_And_MaxTokens()
    {
        var client = new AnthropicLlmClient(
            new System.Net.Http.HttpClient(), "sk-test-key", "claude-3-haiku-20240307", 512);
        Assert.Equal("claude-3-haiku-20240307", client.Model);
    }

    [Fact]
    public async Task AnthropicLlmClient_Normalizes_Anthropic_BaseUrl_To_Messages()
    {
        var handler = new CapturingAnthropicHandler();
        var client = new AnthropicLlmClient(
            new HttpClient(handler),
            "sk-test-key",
            model: "MiniMax-M2.7-highspeed",
            apiUrl: "https://api.minimax.io/anthropic");

        var result = await client.CompleteAsync("system", "hello", System.Threading.CancellationToken.None);

        Assert.Equal("https://api.minimax.io/anthropic/v1/messages", handler.LastRequestUri);
        Assert.Equal("anthropic ok", result);
    }

    [Fact]
    public async System.Threading.Tasks.Task NullLlmClient_Returns_Diagnostic_Message()
    {
        var client = new NullLlmClient();
        var result = await client.CompleteAsync("system", "hello", System.Threading.CancellationToken.None);
        Assert.Contains("[LLM not configured]", result);
    }

    [Fact]
    public void LlmResponseEnhancer_Returns_Fallback_When_NullClient()
    {
        var enhancer = new LlmResponseEnhancer(null);
        Assert.False(enhancer.IsLlmConfigured);

        var result = enhancer.EnhanceResponseText(
            "qc_request",
            "rule-based text",
            new[] { "tool1: summary1" },
            new WorkerContextSummary { DocumentTitle = "test.rvt" },
            new WorkerPersonaSummary { PersonaId = WorkerPersonas.RevitWorker, DisplayName = "Worker" });

        Assert.Equal("rule-based text", result);

        var narration = enhancer.EnhanceResponse(
            "qc_request",
            "rule-based text",
            Array.Empty<string>(),
            new WorkerContextSummary(),
            new WorkerPersonaSummary());

        Assert.Equal(WorkerNarrationModes.RuleOnly, narration.Mode);
        Assert.Equal("rule-based text", narration.Text);
    }

    [Fact]
    public void LlmResponseEnhancer_Uses_UserMessage_And_History_In_NarrationPrompt()
    {
        var recorder = new RecordingLlmClient("enhanced");
        var enhancer = new LlmResponseEnhancer(recorder);
        var context = new WorkerContextSummary
        {
            DocumentTitle = "Test.rvt",
            ActiveViewName = "{3D}",
            Summary = "context summary"
        };
        var persona = new WorkerPersonaSummary
        {
            PersonaId = WorkerPersonas.RevitWorker,
            DisplayName = "765T Worker",
            Tone = "pragmatic"
        };
        var history = new[]
        {
            new WorkerChatMessage { Role = WorkerMessageRoles.User, Content = "chao em" },
            new WorkerChatMessage { Role = WorkerMessageRoles.Worker, Content = "Chao anh." },
            new WorkerChatMessage { Role = WorkerMessageRoles.User, Content = "em la gi" }
        };

        var result = enhancer.EnhanceResponse(
            "em la gi",
            "identity_query",
            "rule-based text",
            Array.Empty<string>(),
            context,
            persona,
            history,
            "reasoning",
            "plan");

        Assert.Equal(WorkerNarrationModes.LlmEnhanced, result.Mode);
        Assert.Contains("User message", recorder.LastUserPrompt, StringComparison.Ordinal);
        Assert.Contains("em la gi", recorder.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recent conversation", recorder.LastUserPrompt, StringComparison.Ordinal);
        Assert.Contains("Test.rvt", recorder.LastUserPrompt, StringComparison.Ordinal);
        Assert.Contains("autoresponder", recorder.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LlmResponseEnhancer_Uses_Compact_Prompt_For_Greeting()
    {
        var recorder = new RecordingLlmClient("enhanced");
        var enhancer = new LlmResponseEnhancer(recorder);
        var context = new WorkerContextSummary
        {
            DocumentTitle = "Test.rvt",
            ActiveViewName = "{3D}",
            ProjectSummary = "Deep scan failed: docs=0/3"
        };

        var result = enhancer.EnhanceResponse(
            "hi",
            "greeting",
            "fallback greeting",
            new[] { "context.get_delta_summary: queue info" },
            context,
            new WorkerPersonaSummary { PersonaId = WorkerPersonas.RevitWorker, DisplayName = "765T Worker" },
            new[]
            {
                new WorkerChatMessage { Role = WorkerMessageRoles.User, Content = "hi" }
            },
            "reasoning",
            "plan");

        Assert.Equal(WorkerNarrationModes.LlmEnhanced, result.Mode);
        Assert.Contains("Reply rules", recorder.LastUserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Fallback safety draft", recorder.LastUserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Project summary", recorder.LastUserPrompt, StringComparison.Ordinal);
        Assert.Contains("greet once", recorder.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LlmResponseEnhancer_Returns_Fallback_When_NullLlmClient_Explicit()
    {
        var enhancer = new LlmResponseEnhancer(new NullLlmClient());
        Assert.False(enhancer.IsLlmConfigured);

        var result = enhancer.EnhanceReasoningSummary(
            "qc_request",
            "fallback reasoning",
            new List<string> { ToolNames.ReviewSmartQc },
            null);

        Assert.Equal("fallback reasoning", result);
    }

    [Fact]
    public void LlmResponseEnhancer_Returns_Fallback_When_PlanSummary_NullClient()
    {
        var enhancer = new LlmResponseEnhancer(null);

        var result = enhancer.EnhancePlanSummary(
            "family_analysis_request",
            "fallback plan",
            new List<string> { ToolNames.FamilyXray },
            new WorkerContextSummary { DocumentTitle = "doc.rvt", ActiveViewName = "View1" },
            null);

        Assert.Equal("fallback plan", result);
    }

    [Fact]
    public void WorkerReasoningEngine_Backward_Compatible_Without_Enhancer()
    {
        // Original 2-arg constructor still works
        var personas = new PersonaRegistry();
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas);
        var session = new WorkerConversationSessionState { PersonaId = WorkerPersonas.RevitWorker };

        var decision = engine.ProcessMessage(session, "kiem tra model health", true);

        Assert.Equal("qc_request", decision.Intent);
        Assert.NotEmpty(decision.PlanSummary);
        Assert.NotEmpty(decision.ReasoningSummary);
    }

    [Fact]
    public void WorkerReasoningEngine_With_NullEnhancer_Still_Returns_Rule_Text()
    {
        // 3-arg constructor with null enhancer works identically to 2-arg
        var personas = new PersonaRegistry();
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas, null);
        var session = new WorkerConversationSessionState { PersonaId = WorkerPersonas.RevitWorker };

        var decision = engine.ProcessMessage(session, "QC sheet A101", true);

        Assert.Equal("sheet_analysis_request", decision.Intent);
        Assert.NotEmpty(decision.PlanSummary);
    }

    [Fact]
    public void WorkerReasoningEngine_With_NullLlmEnhancer_Uses_Fallback()
    {
        var personas = new PersonaRegistry();
        var enhancer = new LlmResponseEnhancer(new NullLlmClient());
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas, enhancer);
        var session = new WorkerConversationSessionState { PersonaId = WorkerPersonas.QaReviewer };

        var decision = engine.ProcessMessage(session, "kiem tra model health", true);

        Assert.Equal("qc_request", decision.Intent);
        // With NullLlmClient, text should remain the original rule-based text
        Assert.DoesNotContain("[LLM not configured]", decision.PlanSummary);
    }

    [Fact]
    public void OpenRouterFirstResolver_Prefers_OpenRouter_And_Default_Model_Profile()
    {
        var previousOverride = Environment.GetEnvironmentVariable("BIM765T_LLM_PROVIDER");
        var previousKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var previousPrimary = Environment.GetEnvironmentVariable("OPENROUTER_PRIMARY_MODEL");
        var previousFallback = Environment.GetEnvironmentVariable("OPENROUTER_FALLBACK_MODEL");
        var previousResponse = Environment.GetEnvironmentVariable("OPENROUTER_RESPONSE_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("BIM765T_LLM_PROVIDER", "AUTO");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test-openrouter-key");
            Environment.SetEnvironmentVariable("OPENROUTER_PRIMARY_MODEL", null);
            Environment.SetEnvironmentVariable("OPENROUTER_FALLBACK_MODEL", null);
            Environment.SetEnvironmentVariable("OPENROUTER_RESPONSE_MODEL", null);

            var resolver = new OpenRouterFirstLlmProviderConfigResolver(new EnvSecretProvider());
            var profile = resolver.Resolve();

            Assert.True(profile.IsConfigured);
            Assert.Equal(LlmProviderKinds.OpenRouter, profile.ConfiguredProvider);
            Assert.Equal("openai/gpt-5.2", profile.PlannerPrimaryModel);
            Assert.Equal("openai/gpt-5-mini", profile.PlannerFallbackModel);
            Assert.Equal("openai/gpt-5-mini", profile.ResponseModel);
            Assert.Equal(LlmSecretSourceKinds.Environment, profile.SecretSourceKind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIM765T_LLM_PROVIDER", previousOverride);
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("OPENROUTER_PRIMARY_MODEL", previousPrimary);
            Environment.SetEnvironmentVariable("OPENROUTER_FALLBACK_MODEL", previousFallback);
            Environment.SetEnvironmentVariable("OPENROUTER_RESPONSE_MODEL", previousResponse);
        }
    }

    [Fact]
    public void OpenRouterFirstResolver_Prefers_MiniMax_Before_OpenAi_And_Anthropic()
    {
        var previousOverride = Environment.GetEnvironmentVariable("BIM765T_LLM_PROVIDER");
        var previousMiniMaxKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
        var previousMiniMaxBaseUrl = Environment.GetEnvironmentVariable("MINIMAX_BASE_URL");
        var previousMiniMaxModel = Environment.GetEnvironmentVariable("MINIMAX_MODEL");
        var previousMiniMaxFallbackModel = Environment.GetEnvironmentVariable("MINIMAX_FALLBACK_MODEL");
        var previousMiniMaxResponseModel = Environment.GetEnvironmentVariable("MINIMAX_RESPONSE_MODEL");
        var previousOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var previousAnthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("BIM765T_LLM_PROVIDER", "AUTO");
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", "test-minimax-key");
            Environment.SetEnvironmentVariable("MINIMAX_BASE_URL", "https://api.minimax.io/v1");
            Environment.SetEnvironmentVariable("MINIMAX_MODEL", "MiniMax-M2.7-highspeed");
            Environment.SetEnvironmentVariable("MINIMAX_FALLBACK_MODEL", "MiniMax-M2.7");
            Environment.SetEnvironmentVariable("MINIMAX_RESPONSE_MODEL", "MiniMax-M2.7-highspeed");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-openai-key");
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-anthropic-key");

            var resolver = new OpenRouterFirstLlmProviderConfigResolver(new EnvSecretProvider());
            var profile = resolver.Resolve();

            Assert.True(profile.IsConfigured);
            Assert.Equal(LlmProviderKinds.MiniMax, profile.ConfiguredProvider);
            Assert.Equal("openai_compatible", profile.ProviderKind);
            Assert.Equal("https://api.minimax.io/v1", profile.ApiUrl);
            Assert.Equal("MiniMax-M2.7-highspeed", profile.PlannerPrimaryModel);
            Assert.Equal("MiniMax-M2.7-highspeed", profile.ResponseModel);
            Assert.Equal("MiniMax-M2.7", profile.PlannerFallbackModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIM765T_LLM_PROVIDER", previousOverride);
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", previousMiniMaxKey);
            Environment.SetEnvironmentVariable("MINIMAX_BASE_URL", previousMiniMaxBaseUrl);
            Environment.SetEnvironmentVariable("MINIMAX_MODEL", previousMiniMaxModel);
            Environment.SetEnvironmentVariable("MINIMAX_FALLBACK_MODEL", previousMiniMaxFallbackModel);
            Environment.SetEnvironmentVariable("MINIMAX_RESPONSE_MODEL", previousMiniMaxResponseModel);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousOpenAiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousAnthropicKey);
        }
    }

    [Fact]
    public void OpenRouterFirstResolver_Honors_Explicit_Minimax_Override_Over_HigherPriority_Providers()
    {
        var previousOverride = Environment.GetEnvironmentVariable("BIM765T_LLM_PROVIDER");
        var previousOpenRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var previousMiniMaxKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
        var previousMiniMaxModel = Environment.GetEnvironmentVariable("MINIMAX_MODEL");
        var previousMiniMaxFallbackModel = Environment.GetEnvironmentVariable("MINIMAX_FALLBACK_MODEL");
        var previousMiniMaxResponseModel = Environment.GetEnvironmentVariable("MINIMAX_RESPONSE_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("BIM765T_LLM_PROVIDER", "MINIMAX");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test-openrouter-key");
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", "test-minimax-key");
            Environment.SetEnvironmentVariable("MINIMAX_MODEL", "MiniMax-M2.7-highspeed");
            Environment.SetEnvironmentVariable("MINIMAX_FALLBACK_MODEL", "MiniMax-M2.7");
            Environment.SetEnvironmentVariable("MINIMAX_RESPONSE_MODEL", "MiniMax-M2.7-highspeed");

            var resolver = new OpenRouterFirstLlmProviderConfigResolver(new EnvSecretProvider());
            var profile = resolver.Resolve();

            Assert.True(profile.IsConfigured);
            Assert.Equal(LlmProviderKinds.MiniMax, profile.ConfiguredProvider);
            Assert.Equal("MiniMax-M2.7-highspeed", profile.PlannerPrimaryModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIM765T_LLM_PROVIDER", previousOverride);
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previousOpenRouterKey);
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", previousMiniMaxKey);
            Environment.SetEnvironmentVariable("MINIMAX_MODEL", previousMiniMaxModel);
            Environment.SetEnvironmentVariable("MINIMAX_FALLBACK_MODEL", previousMiniMaxFallbackModel);
            Environment.SetEnvironmentVariable("MINIMAX_RESPONSE_MODEL", previousMiniMaxResponseModel);
        }
    }

    [Fact]
    public async Task OpenAiCompatibleLlmClient_Normalizes_V1_BaseUrl_To_ChatCompletions()
    {
        var handler = new CapturingLlmHandler("{\"status\":\"ok\"}");
        var client = new OpenAiCompatibleLlmClient(
            new HttpClient(handler),
            "test-minimax-key",
            model: "MiniMax-M2.7",
            apiUrl: "https://api.minimax.io/v1",
            providerLabel: LlmProviderKinds.MiniMax);

        await client.CompleteJsonAsync("system", "hello", System.Threading.CancellationToken.None);

        Assert.Equal("https://api.minimax.io/v1/chat/completions", handler.LastRequestUri);
    }

    [Fact]
    public async Task OpenAiCompatibleLlmClient_Adds_ReasoningSplit_For_MiniMax()
    {
        var handler = new CapturingLlmHandler("{\"status\":\"ok\"}");
        var client = new OpenAiCompatibleLlmClient(
            new HttpClient(handler),
            "test-minimax-key",
            model: "MiniMax-M2.7-highspeed",
            apiUrl: "https://api.minimax.io/v1/chat/completions",
            providerLabel: LlmProviderKinds.MiniMax);

        await client.CompleteJsonAsync("system", "hello", System.Threading.CancellationToken.None);

        Assert.Contains("\"reasoning_split\":true", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiCompatibleLlmClient_DoesNot_Add_ReasoningSplit_For_Text_Narration()
    {
        var handler = new CapturingLlmHandler("Chao anh");
        var client = new OpenAiCompatibleLlmClient(
            new HttpClient(handler),
            "test-minimax-key",
            model: "MiniMax-M2.7-highspeed",
            apiUrl: "https://api.minimax.io/v1/chat/completions",
            providerLabel: LlmProviderKinds.MiniMax);

        await client.CompleteAsync("system", "hello", System.Threading.CancellationToken.None);

        Assert.DoesNotContain("\"reasoning_split\":true", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiCompatibleLlmClient_Parses_Text_Content_Array()
    {
        var payload = "{\"choices\":[{\"message\":{\"content\":[{\"text\":\"Dong 1\"},{\"text\":\"Dong 2\"}]}}]}";
        var handler = new RawPayloadLlmHandler(payload);
        var client = new OpenAiCompatibleLlmClient(
            new HttpClient(handler),
            "test-minimax-key",
            model: "MiniMax-M2.7-highspeed",
            apiUrl: "https://api.minimax.io/v1/chat/completions",
            providerLabel: LlmProviderKinds.MiniMax);

        var result = await client.CompleteAsync("system", "hello", System.Threading.CancellationToken.None);

        Assert.Contains("Dong 1", result, StringComparison.Ordinal);
        Assert.Contains("Dong 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerReasoningEngine_Accepts_MiniMax_ThinkWrapped_Json_Response()
    {
        var personas = new PersonaRegistry();
        var profile = new LlmProviderConfiguration
        {
            IsConfigured = true,
            ConfiguredProvider = LlmProviderKinds.MiniMax,
            ProviderKind = "openai_compatible",
            PlannerPrimaryModel = "MiniMax-M2.7-highspeed",
            PlannerFallbackModel = "MiniMax-M2.7",
            ResponseModel = "MiniMax-M2.7-highspeed"
        };
        var payload = "<think>plan first</think>\n```json\n{\"status\":\"ok\",\"intent\":\"qc_request\",\"goal\":\"QC model.\",\"reasoning_summary\":\"Dung QC lane.\",\"plan_summary\":\"Chay read-only QC.\",\"requires_clarification\":false,\"clarification_question\":\"\",\"tool_candidates\":[\"review.smart_qc\"],\"command_candidates\":[],\"playbook_hints\":[],\"confidence\":0.88}\n```";
        var planner = new LlmPlanningService(
            profile,
            new OpenAiCompatibleLlmClient(new HttpClient(new StaticLlmHandler(payload)), "key", model: profile.PlannerPrimaryModel, apiUrl: "https://api.minimax.io/v1/chat/completions", providerLabel: profile.ConfiguredProvider),
            null);
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas, null, planner);

        var decision = engine.ProcessMessage(
            new WorkerConversationSessionState { PersonaId = WorkerPersonas.RevitWorker },
            "kiem tra model health",
            true,
            new WorkerContextSummary { DocumentTitle = "Test.rvt", ActiveViewName = "Level 1" },
            "default");

        Assert.Equal(WorkerReasoningModes.LlmValidated, decision.ReasoningMode);
        Assert.Contains(ToolNames.ReviewSmartQc, decision.PlannedTools);
        Assert.Equal(LlmProviderKinds.MiniMax, decision.ConfiguredProvider);
    }

    [Fact]
    public void WorkerReasoningEngine_Accepts_Validated_Command_Candidate_From_LlmPlanner()
    {
        var personas = new PersonaRegistry();
        var profile = new LlmProviderConfiguration
        {
            IsConfigured = true,
            ConfiguredProvider = LlmProviderKinds.OpenRouter,
            ProviderKind = "openai_compatible",
            PlannerPrimaryModel = "openai/gpt-5.2",
            PlannerFallbackModel = "openai/gpt-5-mini",
            ResponseModel = "openai/gpt-5-mini"
        };
        var payload = "{\"status\":\"ok\",\"intent\":\"view_authoring_request\",\"goal\":\"Tao view 3D an toan.\",\"reasoning_summary\":\"Dung curated quick-path.\",\"plan_summary\":\"Resolve command roi preview.\",\"requires_clarification\":false,\"clarification_question\":\"\",\"tool_candidates\":[],\"command_candidates\":[\"revit.view.create_3d\"],\"playbook_hints\":[],\"confidence\":0.91}";
        var planner = new LlmPlanningService(
            profile,
            new OpenAiCompatibleLlmClient(new HttpClient(new StaticLlmHandler(payload)), "key", model: profile.PlannerPrimaryModel, apiUrl: "http://llm.local/v1/chat/completions", providerLabel: profile.ConfiguredProvider),
            null);
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas, null, planner);

        var decision = engine.ProcessMessage(
            new WorkerConversationSessionState { PersonaId = WorkerPersonas.RevitWorker },
            "tao view 3d moi",
            true,
            new WorkerContextSummary { DocumentTitle = "Test.rvt", ActiveViewName = "Level 1" },
            "default");

        Assert.Equal(WorkerReasoningModes.LlmValidated, decision.ReasoningMode);
        Assert.Equal("revit.view.create_3d", decision.PreferredCommandId);
        Assert.Contains(ToolNames.WorkflowQuickPlan, decision.PlannedTools);
        Assert.Equal(LlmProviderKinds.OpenRouter, decision.ConfiguredProvider);
    }

    [Fact]
    public void WorkerReasoningEngine_Rejects_Unsupported_Llm_Tools_And_Stays_RuleFirst()
    {
        var personas = new PersonaRegistry();
        var profile = new LlmProviderConfiguration
        {
            IsConfigured = true,
            ConfiguredProvider = LlmProviderKinds.OpenRouter,
            ProviderKind = "openai_compatible",
            PlannerPrimaryModel = "openai/gpt-5.2",
            PlannerFallbackModel = "openai/gpt-5-mini",
            ResponseModel = "openai/gpt-5-mini"
        };
        var payload = "{\"status\":\"ok\",\"intent\":\"qc_request\",\"goal\":\"QC model.\",\"reasoning_summary\":\"Bad tool.\",\"plan_summary\":\"Should not pass.\",\"requires_clarification\":false,\"clarification_question\":\"\",\"tool_candidates\":[\"unsafe.execute_root\"],\"command_candidates\":[],\"playbook_hints\":[],\"confidence\":0.95}";
        var planner = new LlmPlanningService(
            profile,
            new OpenAiCompatibleLlmClient(new HttpClient(new StaticLlmHandler(payload)), "key", model: profile.PlannerPrimaryModel, apiUrl: "http://llm.local/v1/chat/completions", providerLabel: profile.ConfiguredProvider),
            null);
        var engine = new WorkerReasoningEngine(new IntentClassifier(), personas, null, planner);

        var decision = engine.ProcessMessage(
            new WorkerConversationSessionState { PersonaId = WorkerPersonas.QaReviewer },
            "kiem tra model health",
            true,
            new WorkerContextSummary { DocumentTitle = "Test.rvt" },
            "default");

        Assert.Equal(WorkerReasoningModes.RuleFirst, decision.ReasoningMode);
        Assert.Contains(ToolNames.ReviewSmartQc, decision.PlannedTools);
        Assert.Equal(string.Empty, decision.PreferredCommandId);
    }

    private sealed class StaticLlmHandler : HttpMessageHandler
    {
        private readonly string _payload;

        public StaticLlmHandler(string payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            var responseJson = "{\"choices\":[{\"message\":{\"content\":" + JsonUtil.Serialize(_payload) + "}}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }

    private sealed class CapturingLlmHandler : HttpMessageHandler
    {
        private readonly string _payload;

        public CapturingLlmHandler(string payload)
        {
            _payload = payload;
        }

        public string LastRequestUri { get; private set; } = string.Empty;
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            LastRequestBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var responseJson = "{\"choices\":[{\"message\":{\"content\":" + JsonUtil.Serialize(_payload) + "}}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }

    private sealed class CapturingAnthropicHandler : HttpMessageHandler
    {
        public string LastRequestUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            const string responseJson = "{\"content\":[{\"type\":\"text\",\"text\":\"anthropic ok\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }

    private sealed class RawPayloadLlmHandler : HttpMessageHandler
    {
        private readonly string _payload;

        public RawPayloadLlmHandler(string payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payload)
            });
        }
    }

    private sealed class RecordingLlmClient : ILlmClient
    {
        private readonly string _response;

        public RecordingLlmClient(string response)
        {
            _response = response;
        }

        public string LastSystemPrompt { get; private set; } = string.Empty;

        public string LastUserPrompt { get; private set; } = string.Empty;

        public Task<string> CompleteAsync(string systemPrompt, string userMessage, System.Threading.CancellationToken cancellationToken)
        {
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userMessage;
            return Task.FromResult(_response);
        }
    }
}
