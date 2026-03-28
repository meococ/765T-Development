using System;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

/// <summary>
/// Validates the conversational fast-path routing: which intents skip the full 5-step pipeline
/// and use the 3-step Gather → Conversational → BuildResponse path instead.
/// </summary>
public sealed class ConversationalFastPathTests
{
    // ── IsConversationalIntent: should return TRUE for all fast-path intents ──

    [Theory]
    [InlineData("greeting")]
    [InlineData("identity_query")]
    [InlineData("help")]
    [InlineData("context_query")]
    [InlineData("project_research_request")]
    [InlineData("qc_request")]
    [InlineData("family_analysis_request")]
    public void IsConversationalIntent_TrueForFastPathIntents(string intent)
    {
        Assert.True(WorkerReasoningEngine.IsConversationalIntent(intent));
    }

    [Theory]
    [InlineData("Greeting")]
    [InlineData("HELP")]
    [InlineData("QC_REQUEST")]
    [InlineData("Project_Research_Request")]
    public void IsConversationalIntent_CaseInsensitive(string intent)
    {
        Assert.True(WorkerReasoningEngine.IsConversationalIntent(intent));
    }

    // ── IsConversationalIntent: should return FALSE for action intents that need full pipeline ──

    [Theory]
    [InlineData("mutation_request")]
    [InlineData("sheet_authoring_request")]
    [InlineData("view_authoring_request")]
    [InlineData("documentation_request")]
    [InlineData("model_manage_request")]
    [InlineData("command_palette_request")]
    [InlineData("element_authoring_request")]
    [InlineData("governance_request")]
    [InlineData("annotation_request")]
    [InlineData("coordination_request")]
    [InlineData("systems_request")]
    [InlineData("integration_request")]
    [InlineData("approval")]
    [InlineData("reject")]
    [InlineData("resume")]
    [InlineData("cancel")]
    public void IsConversationalIntent_FalseForActionIntents(string intent)
    {
        Assert.False(WorkerReasoningEngine.IsConversationalIntent(intent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown_intent")]
    [InlineData("random_text")]
    public void IsConversationalIntent_FalseForUnknownOrEmpty(string intent)
    {
        Assert.False(WorkerReasoningEngine.IsConversationalIntent(intent));
    }

    // ── ConversationalTimeoutSeconds: validate the tighter timeout ──

    [Fact]
    public void ConversationalTimeout_IsShorterThanDefaultTimeout()
    {
        // Conversational fast-path should be 8s — significantly shorter than the default 20s.
        Assert.Equal(8, LlmResponseEnhancer.ConversationalTimeoutSeconds);
    }

    // ── EnhanceConversationalAsync: no LLM configured → rule-only fallback ──

    [Fact]
    public async Task EnhanceConversationalAsync_NoLlm_ReturnsFallbackText()
    {
        // null client → NullLlmClient internally → IsLlmConfigured = false
        var enhancer = new LlmResponseEnhancer(null);
        var fallback = "Chao anh. Em dang san sang.";

        var result = await enhancer.EnhanceConversationalAsync(
            "chao em", "greeting", fallback,
            new WorkerContextSummary { DocumentTitle = "Test.rvt", ActiveViewName = "{3D}" },
            null, null, CancellationToken.None);

        Assert.Equal(fallback, result.Text);
        Assert.Equal(WorkerNarrationModes.RuleOnly, result.Mode);
    }

    // ── ProcessMessage: conversational intents produce correct decision structure ──

    [Theory]
    [InlineData("project_research_request")]
    [InlineData("qc_request")]
    [InlineData("family_analysis_request")]
    public void ProcessMessage_NewFastPathIntents_ProducePlanSummary(string expectedIntent)
    {
        // Use the reasoning engine with the IntentClassifier to verify
        // that the new intents are properly handled in PopulatePlanByIntent.
        var classifier = new IntentClassifier();
        var personas = new PersonaRegistry();
        var engine = new WorkerReasoningEngine(classifier, personas);

        // Build a message that the classifier would map to the expected intent.
        var message = expectedIntent switch
        {
            "project_research_request" => "tong quan project hien tai",
            "qc_request" => "kiem tra model health",
            "family_analysis_request" => "phan tich family",
            _ => "test"
        };

        var session = new WorkerConversationSessionState();
        var decision = engine.ProcessMessage(session, message, false);

        // The decision must have a non-empty PlanSummary
        // (PopulatePlanByIntent was already handling these intents before — we just routed them to fast-path)
        Assert.False(string.IsNullOrWhiteSpace(decision.PlanSummary),
            $"Intent '{expectedIntent}' should produce a PlanSummary but got empty.");
    }
}
