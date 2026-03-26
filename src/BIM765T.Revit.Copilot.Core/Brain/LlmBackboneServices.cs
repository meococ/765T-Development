using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core.Brain;

public static class LlmProviderKinds
{
    public const string RuleFirst = "RULE FIRST";
    public const string OpenRouter = "OPENROUTER";
    public const string MiniMax = "MINIMAX";
    public const string OpenAi = "OPENAI";
    public const string Anthropic = "ANTHROPIC";
}

public static class LlmSecretSourceKinds
{
    public const string None = "none";
    public const string Environment = "env";
}

public sealed class LlmProviderConfiguration
{
    public bool IsConfigured { get; set; }

    public string ConfiguredProvider { get; set; } = LlmProviderKinds.RuleFirst;

    public string ProviderKind { get; set; } = string.Empty;

    public string SecretSourceKind { get; set; } = LlmSecretSourceKinds.None;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiUrl { get; set; } = string.Empty;

    public string PlannerPrimaryModel { get; set; } = string.Empty;

    public string PlannerFallbackModel { get; set; } = string.Empty;

    public string ResponseModel { get; set; } = string.Empty;

    public string HttpReferer { get; set; } = string.Empty;

    public string XTitle { get; set; } = "BIM765T Revit Worker";

    public string Organization { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string ReasoningMode => IsConfigured ? WorkerReasoningModes.LlmValidated : WorkerReasoningModes.RuleFirst;

    public string DisplayLabel
    {
        get
        {
            if (!IsConfigured)
            {
                return LlmProviderKinds.RuleFirst;
            }

            if (string.IsNullOrWhiteSpace(PlannerPrimaryModel))
            {
                return ConfiguredProvider;
            }

            return $"{ConfiguredProvider} {PlannerPrimaryModel}";
        }
    }
}

public interface ISecretProvider
{
    string SourceKind { get; }

    string GetSecret(string name);
}

public sealed class EnvSecretProvider : ISecretProvider
{
    public string SourceKind => LlmSecretSourceKinds.Environment;

    public string GetSecret(string name)
    {
        var normalizedName = name ?? string.Empty;
        var processValue = Environment.GetEnvironmentVariable(normalizedName);
        if (!string.IsNullOrWhiteSpace(processValue))
        {
            return processValue;
        }

        var userValue = Environment.GetEnvironmentVariable(normalizedName, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(userValue))
        {
            return userValue;
        }

        return Environment.GetEnvironmentVariable(normalizedName, EnvironmentVariableTarget.Machine) ?? string.Empty;
    }
}

public interface ILlmProviderConfigResolver
{
    LlmProviderConfiguration Resolve();
}

public sealed class OpenRouterFirstLlmProviderConfigResolver : ILlmProviderConfigResolver
{
    private const string OpenRouterApiUrl = "https://openrouter.ai/api/v1/chat/completions";
    private readonly ISecretProvider _secrets;

    public OpenRouterFirstLlmProviderConfigResolver(ISecretProvider secrets)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    public LlmProviderConfiguration Resolve()
    {
        var providerOverride = NormalizeProviderOverride(ResolveValue("BIM765T_LLM_PROVIDER", string.Empty));
        if (string.Equals(providerOverride, LlmProviderKinds.RuleFirst, StringComparison.OrdinalIgnoreCase))
        {
            return new LlmProviderConfiguration();
        }

        if (ShouldResolveProvider(providerOverride, LlmProviderKinds.OpenRouter))
        {
            var openRouterKey = _secrets.GetSecret("OPENROUTER_API_KEY");
            if (!string.IsNullOrWhiteSpace(openRouterKey))
            {
                return new LlmProviderConfiguration
                {
                    IsConfigured = true,
                    ConfiguredProvider = LlmProviderKinds.OpenRouter,
                    ProviderKind = "openai_compatible",
                    SecretSourceKind = _secrets.SourceKind,
                    ApiKey = openRouterKey.Trim(),
                    ApiUrl = OpenRouterApiUrl,
                    PlannerPrimaryModel = ResolveValue("OPENROUTER_PRIMARY_MODEL", "openai/gpt-5.2"),
                    PlannerFallbackModel = ResolveValue("OPENROUTER_FALLBACK_MODEL", "openai/gpt-5-mini"),
                    ResponseModel = ResolveValue("OPENROUTER_RESPONSE_MODEL", "openai/gpt-5-mini"),
                    HttpReferer = ResolveValue("OPENROUTER_HTTP_REFERER", string.Empty),
                    XTitle = ResolveValue("OPENROUTER_X_TITLE", "BIM765T Revit Worker")
                };
            }
        }

        if (ShouldResolveProvider(providerOverride, LlmProviderKinds.MiniMax))
        {
            var miniMaxKey = ResolveFirstNonEmpty("MINIMAX_API_KEY", "MINIMAX_AUTH_TOKEN");
            if (!string.IsNullOrWhiteSpace(miniMaxKey))
            {
                var primaryModel = ResolveValue("MINIMAX_MODEL", "MiniMax-M2.7-highspeed");
                var fallbackModel = string.Equals(primaryModel, "MiniMax-M2.7-highspeed", StringComparison.OrdinalIgnoreCase)
                    ? "MiniMax-M2.7"
                    : primaryModel;
                return new LlmProviderConfiguration
                {
                    IsConfigured = true,
                    ConfiguredProvider = LlmProviderKinds.MiniMax,
                    ProviderKind = "openai_compatible",
                    SecretSourceKind = _secrets.SourceKind,
                    ApiKey = miniMaxKey.Trim(),
                    ApiUrl = ResolveValue("MINIMAX_BASE_URL", "https://api.minimax.io/v1"),
                    PlannerPrimaryModel = primaryModel,
                    PlannerFallbackModel = ResolveValue("MINIMAX_FALLBACK_MODEL", fallbackModel),
                    ResponseModel = ResolveValue("MINIMAX_RESPONSE_MODEL", primaryModel),
                    XTitle = ResolveValue("MINIMAX_X_TITLE", "BIM765T Revit Worker")
                };
            }
        }

        if (ShouldResolveProvider(providerOverride, LlmProviderKinds.OpenAi))
        {
            var openAiKey = ResolveFirstNonEmpty("OPENAI_API_KEY", "OPENAI_AUTH_TOKEN");
            if (!string.IsNullOrWhiteSpace(openAiKey))
            {
                return new LlmProviderConfiguration
                {
                    IsConfigured = true,
                    ConfiguredProvider = LlmProviderKinds.OpenAi,
                    ProviderKind = "openai_compatible",
                    SecretSourceKind = _secrets.SourceKind,
                    ApiKey = openAiKey.Trim(),
                    ApiUrl = ResolveValue("OPENAI_BASE_URL", "https://api.openai.com/v1/chat/completions"),
                    PlannerPrimaryModel = ResolveValue("OPENAI_MODEL", "gpt-5-mini"),
                    PlannerFallbackModel = ResolveValue("OPENAI_FALLBACK_MODEL", ResolveValue("OPENAI_MODEL", "gpt-5-mini")),
                    ResponseModel = ResolveValue("OPENAI_RESPONSE_MODEL", ResolveValue("OPENAI_MODEL", "gpt-5-mini")),
                    Organization = ResolveValue("OPENAI_ORGANIZATION", string.Empty),
                    Project = ResolveValue("OPENAI_PROJECT", string.Empty),
                    XTitle = "BIM765T Revit Worker"
                };
            }
        }

        if (ShouldResolveProvider(providerOverride, LlmProviderKinds.Anthropic))
        {
            var anthropicKey = ResolveFirstNonEmpty("ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_API_KEY");
            if (!string.IsNullOrWhiteSpace(anthropicKey))
            {
                var baseUrl = ResolveValue("ANTHROPIC_BASE_URL", string.Empty);
                return new LlmProviderConfiguration
                {
                    IsConfigured = true,
                    ConfiguredProvider = LlmProviderKinds.Anthropic,
                    ProviderKind = "anthropic",
                    SecretSourceKind = _secrets.SourceKind,
                    ApiKey = anthropicKey.Trim(),
                    ApiUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.anthropic.com/v1/messages" : baseUrl,
                    PlannerPrimaryModel = ResolveValue("ANTHROPIC_MODEL", "claude-sonnet-4-20250514"),
                    PlannerFallbackModel = ResolveValue("ANTHROPIC_FALLBACK_MODEL", ResolveValue("ANTHROPIC_MODEL", "claude-sonnet-4-20250514")),
                    ResponseModel = ResolveValue("ANTHROPIC_RESPONSE_MODEL", ResolveValue("ANTHROPIC_MODEL", "claude-sonnet-4-20250514")),
                    XTitle = "BIM765T Revit Worker"
                };
            }
        }

        return new LlmProviderConfiguration();
    }

    private static bool ShouldResolveProvider(string providerOverride, string candidate)
    {
        return string.IsNullOrWhiteSpace(providerOverride)
            || string.Equals(providerOverride, "AUTO", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerOverride, candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProviderOverride(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "RULE_FIRST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "RULE-FIRST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "RULE FIRST", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderKinds.RuleFirst;
        }

        return normalized.ToUpperInvariant();
    }

    private string ResolveValue(string name, string fallback)
    {
        var value = _secrets.GetSecret(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private string ResolveFirstNonEmpty(params string[] names)
    {
        foreach (var name in names ?? Array.Empty<string>())
        {
            var value = _secrets.GetSecret(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}

public sealed class LlmPlanningRequest
{
    public WorkerConversationSessionState Session { get; set; } = new WorkerConversationSessionState();

    public WorkerDecision RuleDecision { get; set; } = new WorkerDecision();

    public WorkerIntentClassification Classification { get; set; } = new WorkerIntentClassification();

    public WorkerPersonaSummary Persona { get; set; } = new WorkerPersonaSummary();

    public WorkerContextSummary ContextSummary { get; set; } = new WorkerContextSummary();

    public string WorkspaceId { get; set; } = string.Empty;

    public string UserMessage { get; set; } = string.Empty;

    public bool ContinueMission { get; set; } = true;

    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;

    public List<string> CandidateTools { get; set; } = new List<string>();

    public List<string> CandidateCommands { get; set; } = new List<string>();
}

public sealed class LlmPlanProposal
{
    public bool Parsed { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Intent { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string ReasoningSummary { get; set; } = string.Empty;

    public string PlanSummary { get; set; } = string.Empty;

    public bool RequiresClarification { get; set; }

    public string ClarificationQuestion { get; set; } = string.Empty;

    public List<string> ToolCandidates { get; set; } = new List<string>();

    public List<string> CommandCandidates { get; set; } = new List<string>();

    public List<string> PlaybookHints { get; set; } = new List<string>();

    public double Confidence { get; set; }

    public string ModelUsed { get; set; } = string.Empty;

    public string RawJson { get; set; } = string.Empty;
}

public sealed class LlmPlanValidationResult
{
    public bool Accepted { get; set; }

    public string Reason { get; set; } = string.Empty;

    public LlmPlanProposal Proposal { get; set; } = new LlmPlanProposal();

    public string ConfiguredProvider { get; set; } = string.Empty;

    public string PlannerModel { get; set; } = string.Empty;

    public string ResponseModel { get; set; } = string.Empty;

    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;

    public string PreferredCommandId { get; set; } = string.Empty;

    public List<string> PlannedTools { get; set; } = new List<string>();
}

public interface ILlmPlanner
{
    bool IsConfigured { get; }

    LlmProviderConfiguration RuntimeProfile { get; }

    LlmPlanValidationResult Plan(LlmPlanningRequest request);

    /// <summary>
    /// Async planning — avoids blocking the Revit UI thread during HTTP calls to LLM providers.
    /// </summary>
    Task<LlmPlanValidationResult> PlanAsync(LlmPlanningRequest request, CancellationToken cancellationToken);
}

public sealed class NullLlmPlanner : ILlmPlanner
{
    public bool IsConfigured => false;

    public LlmProviderConfiguration RuntimeProfile { get; } = new LlmProviderConfiguration();

    public LlmPlanValidationResult Plan(LlmPlanningRequest request)
    {
        return new LlmPlanValidationResult
        {
            Accepted = false,
            Reason = "llm_not_configured",
            ConfiguredProvider = LlmProviderKinds.RuleFirst,
            ReasoningMode = WorkerReasoningModes.RuleFirst
        };
    }

    public Task<LlmPlanValidationResult> PlanAsync(LlmPlanningRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Plan(request));
    }
}

public sealed class LlmPlanningService : ILlmPlanner
{
    private const int PlannerTimeoutSeconds = 4;
    private static readonly HashSet<string> AllowedIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "greeting",
        "help",
        "context_query",
        "project_research_request",
        "sheet_analysis_request",
        "sheet_authoring_request",
        "view_authoring_request",
        "documentation_request",
        "model_manage_request",
        "command_palette_request",
        "element_authoring_request",
        "governance_request",
        "annotation_request",
        "coordination_request",
        "systems_request",
        "integration_request",
        "intent_compile_request",
        "family_analysis_request",
        "qc_request",
        "mutation_request",
        "approval",
        "reject",
        "resume",
        "cancel"
    };

    private static readonly HashSet<string> AllowedPlannerTools = new(StringComparer.OrdinalIgnoreCase)
    {
        ToolNames.SessionGetTaskContext,
        ToolNames.ContextGetDeltaSummary,
        ToolNames.ProjectGetManifest,
        ToolNames.ProjectInitPreview,
        ToolNames.ProjectInitApply,
        ToolNames.ProjectDeepScan,
        ToolNames.ProjectGetContextBundle,
        ToolNames.ProjectGetDeepScan,
        ToolNames.ArtifactSummarize,
        ToolNames.MemorySearchScoped,
        ToolNames.MemoryFindSimilarRuns,
        ToolNames.ReviewModelHealth,
        ToolNames.ReviewSmartQc,
        ToolNames.ReviewSheetSummary,
        ToolNames.FamilyXray,
        ToolNames.WorkspaceGetManifest,
        ToolNames.StandardsResolve,
        ToolNames.PlaybookMatch,
        ToolNames.PlaybookPreview,
        ToolNames.WorkflowQuickPlan,
        ToolNames.CommandDescribe,
        ToolNames.CommandExecuteSafe,
        ToolNames.CommandSearch,
        ToolNames.CommandCoverageReport,
        ToolNames.ToolGetGuidance,
        ToolNames.PolicyResolve,
        ToolNames.SpecialistResolve,
        ToolNames.IntentCompile,
        ToolNames.IntentValidate,
        ToolNames.SystemCaptureGraph,
        ToolNames.IntegrationPreviewSync,
        ToolNames.ScriptList,
        ToolNames.ScriptValidate,
        ToolNames.ScriptComposeSafe,
        ToolNames.ScriptImportManifest,
        ToolNames.ScriptInstallPack,
        ToolNames.SchedulePreviewCreate,
        ToolNames.ScheduleCreateSafe,
        ToolNames.SheetCreateSafe,
        ToolNames.SheetPlaceViewsSafe,
        ToolNames.SheetRenumberSafe,
        ToolNames.ViewCreate3dSafe,
        ToolNames.ViewDuplicateSafe,
        ToolNames.ViewCreateProjectViewSafe,
        ToolNames.ViewSetTemplateSafe,
        ToolNames.DataPreviewImport,
        ToolNames.DataImportSafe,
        ToolNames.DataExportSchedule,
        ToolNames.FamilyLoadSafe,
        ToolNames.FamilyListLibraryRoots,
        ToolNames.AuditNamingConvention,
        ToolNames.AuditComplianceReport,
        ToolNames.AuditTemplateHealth
    };

    private readonly LlmProviderConfiguration _profile;
    private readonly OpenAiCompatibleLlmClient? _primaryClient;
    private readonly OpenAiCompatibleLlmClient? _fallbackClient;

    public LlmPlanningService(LlmProviderConfiguration profile, OpenAiCompatibleLlmClient? primaryClient, OpenAiCompatibleLlmClient? fallbackClient)
    {
        _profile = profile ?? new LlmProviderConfiguration();
        _primaryClient = primaryClient;
        _fallbackClient = fallbackClient;
    }

    public bool IsConfigured => _profile.IsConfigured && _primaryClient != null;

    public LlmProviderConfiguration RuntimeProfile => _profile;

    public LlmPlanValidationResult Plan(LlmPlanningRequest request)
    {
        // Offload to thread-pool to avoid deadlock when called from Revit UI thread.
        // PlanAsync contains HTTP calls that must NOT block the UI message pump.
        return Task.Run(() => PlanAsync(request, CancellationToken.None)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Async planning — runs LLM HTTP calls without blocking the calling thread.
    /// Primary + optional fallback model, validated against allowed intent/tool sets.
    /// </summary>
    public async Task<LlmPlanValidationResult> PlanAsync(LlmPlanningRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured || request == null)
        {
            return new LlmPlanValidationResult
            {
                Accepted = false,
                Reason = "planner_unavailable",
                ConfiguredProvider = _profile.ConfiguredProvider,
                PlannerModel = _profile.PlannerPrimaryModel,
                ResponseModel = _profile.ResponseModel,
                ReasoningMode = WorkerReasoningModes.RuleFirst
            };
        }

        var proposal = await TryPlanWithClientAsync(_primaryClient!, _profile.PlannerPrimaryModel, request, cancellationToken).ConfigureAwait(false);
        if ((!proposal.Parsed || proposal.Confidence < ResolveConfidenceThreshold(request)) && _fallbackClient != null)
        {
            proposal = await TryPlanWithClientAsync(_fallbackClient, _profile.PlannerFallbackModel, request, cancellationToken).ConfigureAwait(false);
        }

        return Validate(request, proposal);
    }

    private async Task<LlmPlanProposal> TryPlanWithClientAsync(
        OpenAiCompatibleLlmClient client, string modelName, LlmPlanningRequest request, CancellationToken cancellationToken)
    {
        var systemPrompt = BuildSystemPrompt(request);
        var userPrompt = BuildUserPrompt(request);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(PlannerTimeoutSeconds));
            var json = await client.CompleteJsonAsync(systemPrompt, userPrompt, cts.Token).ConfigureAwait(false);

            var proposal = ParseProposal(json);
            proposal.ModelUsed = modelName ?? string.Empty;
            proposal.RawJson = json ?? string.Empty;
            return proposal;
        }
        catch (Exception)
        {
            return new LlmPlanProposal
            {
                Parsed = false,
                Status = "error",
                ModelUsed = modelName ?? string.Empty
            };
        }
    }

    private LlmPlanValidationResult Validate(LlmPlanningRequest request, LlmPlanProposal proposal)
    {
        var safeProposal = proposal ?? new LlmPlanProposal();
        var result = new LlmPlanValidationResult
        {
            Proposal = safeProposal,
            ConfiguredProvider = _profile.ConfiguredProvider,
            PlannerModel = string.IsNullOrWhiteSpace(safeProposal.ModelUsed) ? _profile.PlannerPrimaryModel : safeProposal.ModelUsed,
            ResponseModel = _profile.ResponseModel,
            ReasoningMode = WorkerReasoningModes.RuleFirst,
            PlannedTools = request?.RuleDecision?.PlannedTools?.ToList() ?? new List<string>()
        };

        if (request == null)
        {
            result.Reason = "request_missing";
            return result;
        }

        if (!safeProposal.Parsed)
        {
            result.Reason = "proposal_parse_failed";
            return result;
        }

        if (!string.IsNullOrWhiteSpace(safeProposal.Intent) && !AllowedIntents.Contains(safeProposal.Intent))
        {
            result.Reason = "unsupported_intent";
            return result;
        }

        var confidenceThreshold = ResolveConfidenceThreshold(request);
        if (safeProposal.Confidence < confidenceThreshold && !CanShipModeProceed(request, safeProposal))
        {
            result.Reason = "low_confidence";
            return result;
        }

        var allowedUniverse = new HashSet<string>(
            (request.CandidateTools ?? new List<string>())
                .Where(x => AllowedPlannerTools.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        if (allowedUniverse.Count == 0)
        {
            allowedUniverse = new HashSet<string>(AllowedPlannerTools, StringComparer.OrdinalIgnoreCase);
        }

        var filteredTools = (safeProposal.ToolCandidates ?? new List<string>())
            .Where(x => allowedUniverse.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allowedCommandUniverse = new HashSet<string>(
            (request.CandidateCommands ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var preferredCommandId = (safeProposal.CommandCandidates ?? new List<string>())
            .FirstOrDefault(commandId => IsAllowedPlannerCommand(commandId, request.AutonomyMode, allowedCommandUniverse)) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preferredCommandId))
        {
            preferredCommandId = filteredTools.FirstOrDefault(toolId => IsAllowedPlannerCommand(toolId, request.AutonomyMode, allowedCommandUniverse)) ?? string.Empty;
        }

        if ((safeProposal.ToolCandidates?.Count ?? 0) > 0 && filteredTools.Count == 0 && string.IsNullOrWhiteSpace(preferredCommandId))
        {
            result.Reason = "unsupported_tools";
            return result;
        }

        result.Accepted = true;
        result.Reason = safeProposal.RequiresClarification ? "clarify" : (safeProposal.Confidence < confidenceThreshold ? "ship_mode_low_confidence" : "accepted");
        result.ReasoningMode = WorkerReasoningModes.LlmValidated;
        result.PreferredCommandId = preferredCommandId;
        if (filteredTools.Count > 0)
        {
            result.PlannedTools = filteredTools;
        }
        else if (!string.IsNullOrWhiteSpace(preferredCommandId))
        {
            result.PlannedTools = new List<string>
            {
                ToolNames.WorkflowQuickPlan,
                ToolNames.CommandDescribe,
                ToolNames.CommandExecuteSafe
            };
        }
        else
        {
            result.PlannedTools = request.RuleDecision.PlannedTools.ToList();
        }

        return result;
    }

    private static string BuildSystemPrompt(LlmPlanningRequest request)
    {
        var autonomyMode = WorkerAutonomyModes.Normalize(request?.AutonomyMode);
        var autonomyInstruction = string.Equals(autonomyMode, WorkerAutonomyModes.Ship, StringComparison.OrdinalIgnoreCase)
            ? "Operate in SHIP mode: move fast, prefer the shortest useful tool chain, maximize useful work with the provided runtime tools, and only ask clarification when the request is genuinely ambiguous. "
            : "Operate in BOUNDED mode: prefer conservative plans and rely on the rule-first baseline when uncertain. ";
        return "You are the 765T BIM planning model. Return strict JSON only. " +
               autonomyInstruction +
               "Be tool-first, BIM-first, and execution-aware. Never propose arbitrary code execution or invent raw tool names outside the candidate set. " +
               "When the user speaks Vietnamese, all human-readable JSON fields must be concise Vietnamese with full diacritics. " +
               "If scope is ambiguous, set requires_clarification=true and provide one concise clarification_question.";
    }

    private static string BuildUserPrompt(LlmPlanningRequest request)
    {
        return
            "Return one JSON object with fields: " +
            "status, intent, goal, reasoning_summary, plan_summary, requires_clarification, clarification_question, " +
            "tool_candidates, command_candidates, playbook_hints, confidence.\n\n" +
            $"User message: {request.UserMessage}\n" +
            $"Rule intent: {request.Classification.Intent}\n" +
            $"Rule goal: {request.RuleDecision.Goal}\n" +
            $"Rule plan summary: {request.RuleDecision.PlanSummary}\n" +
            $"Rule tools: {string.Join(", ", request.RuleDecision.PlannedTools ?? new List<string>())}\n" +
            $"Autonomy mode: {WorkerAutonomyModes.Normalize(request.AutonomyMode)}\n" +
            $"Candidate tools: {string.Join(", ", request.CandidateTools ?? new List<string>())}\n" +
            $"Candidate commands: {string.Join(", ", request.CandidateCommands ?? new List<string>())}\n" +
            $"Persona: {request.Persona.PersonaId}\n" +
            $"Workspace: {request.WorkspaceId}\n" +
            $"Document: {request.ContextSummary.DocumentTitle}\n" +
            $"View: {request.ContextSummary.ActiveViewName}\n" +
            $"Selection count: {request.ContextSummary.SelectionCount}\n" +
            $"Pending approval: {request.Session.PendingApprovalState?.HasPendingApproval}\n" +
            "Pick from the candidate sets above. Prefer direct product-shipping flows over abstract advice. " +
            "Command candidates can include curated command ids or runtime tool ids when relevant.";
    }

    private static double ResolveConfidenceThreshold(LlmPlanningRequest request)
    {
        return string.Equals(WorkerAutonomyModes.Normalize(request?.AutonomyMode), WorkerAutonomyModes.Ship, StringComparison.OrdinalIgnoreCase)
            ? 0.22d
            : 0.45d;
    }

    private static bool CanShipModeProceed(LlmPlanningRequest request, LlmPlanProposal proposal)
    {
        return string.Equals(WorkerAutonomyModes.Normalize(request?.AutonomyMode), WorkerAutonomyModes.Ship, StringComparison.OrdinalIgnoreCase)
            && proposal != null
            && proposal.Parsed
            && (((proposal.ToolCandidates?.Count ?? 0) > 0) || ((proposal.CommandCandidates?.Count ?? 0) > 0));
    }

    private static bool IsAllowedPlannerCommand(string? commandId, string? autonomyMode, HashSet<string> candidateCommands)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return false;
        }

        if (CommandAtlasService.IsMvpCuratedCommand(commandId))
        {
            return true;
        }

        var normalizedAutonomy = WorkerAutonomyModes.Normalize(autonomyMode);
        if (!string.Equals(normalizedAutonomy, WorkerAutonomyModes.Ship, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var trimmed = commandId ?? string.Empty;
        if (candidateCommands.Contains(trimmed))
        {
            return true;
        }

        return trimmed.IndexOf('.') > 0
            && trimmed.Length <= 96
            && trimmed.All(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-');
    }

    private static LlmPlanProposal ParseProposal(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LlmPlanProposal { Parsed = false, Status = "empty" };
        }

        try
        {
            if (!LightweightJson.TryParse(json, out var root) || !root.IsObject)
            {
                return new LlmPlanProposal { Parsed = false, Status = "invalid_json" };
            }

            return new LlmPlanProposal
            {
                Parsed = true,
                Status = ReadString(root, "status"),
                Intent = ReadString(root, "intent"),
                Goal = ReadString(root, "goal"),
                ReasoningSummary = ReadString(root, "reasoning_summary"),
                PlanSummary = ReadString(root, "plan_summary"),
                RequiresClarification = ReadBool(root, "requires_clarification"),
                ClarificationQuestion = ReadString(root, "clarification_question"),
                ToolCandidates = ReadStringList(root, "tool_candidates"),
                CommandCandidates = ReadStringList(root, "command_candidates"),
                PlaybookHints = ReadStringList(root, "playbook_hints"),
                Confidence = ReadDouble(root, "confidence")
            };
        }
        catch
        {
            return new LlmPlanProposal { Parsed = false, Status = "invalid_json" };
        }
    }

    private static string ReadString(LightweightJsonValue root, string name)
    {
        if (root.TryGetProperty(name, out var value) && value.Kind == LightweightJsonKind.String)
        {
            return value.StringValue;
        }

        return string.Empty;
    }

    private static bool ReadBool(LightweightJsonValue root, string name)
    {
        if (root.TryGetProperty(name, out var value) && value.Kind == LightweightJsonKind.Boolean)
        {
            return value.BooleanValue;
        }

        return false;
    }

    private static double ReadDouble(LightweightJsonValue root, string name)
    {
        if (root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed))
        {
            return parsed;
        }

        return 0d;
    }

    private static List<string> ReadStringList(LightweightJsonValue root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.IsArray)
        {
            return new List<string>();
        }

        return value.Items
            .Where(x => x.Kind == LightweightJsonKind.String)
            .Select(x => x.StringValue)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
