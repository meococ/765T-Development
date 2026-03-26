using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core.Brain;

/// <summary>
/// Seam between the rule-based engine and the LLM text generation layer.
/// Calls <see cref="ILlmClient"/> to produce natural Vietnamese text for the worker UI,
/// but falls back to the rule-based text if the LLM is unavailable, slow, or returns empty.
///
/// This class does NOT influence tool selection or intent routing — it only enhances
/// the display text that the user sees in chat bubbles and info cards.
/// </summary>
public sealed class LlmResponseEnhancer
{
    private const int ResponseTimeoutSeconds = 10;
    private const int MaxHistoryMessages = 4;

    private const string PreferredSystemPrompt =
        "You are 765T Assistant, a high-quality BIM copilot embedded inside Autodesk Revit. " +
        "Always answer in natural Vietnamese with full diacritics. Address the user as 'anh' and refer to yourself as 'em'. " +
        "Answer the user's actual request directly, not by paraphrasing an internal template. " +
        "Use the current Revit document/view context and any tool evidence when relevant. " +
        "Do not sound like an autoresponder, macro, helpdesk bot, or scripted wizard. " +
        "Do not mention internal terms such as lane, intent, rule-first, mission, orchestration, compile, persona, queue, deep scan, playbook, policy pack, or workspace internals unless the user explicitly asks about the system or the issue blocks the task. " +
        "Be concrete, useful, and concise: usually 2-5 sentences. " +
        "If context is missing or uncertain, state exactly what is missing. " +
        "If the user only greets, greet once and immediately pivot to 1-2 concrete next things you can help with in the current model. " +
        "If the user asks who you are, explain what you are and what you can inspect or do in the current Revit context right now. " +
        "Prefer a direct answer over operational chatter.";

    private readonly ILlmClient _llmClient;
    private readonly bool _isLlmReal;

    public LlmResponseEnhancer(ILlmClient? llmClient)
    {
        _llmClient = llmClient ?? new NullLlmClient();
        _isLlmReal = llmClient != null && !(llmClient is NullLlmClient);
    }

    public bool IsLlmConfigured => _isLlmReal;

    public LlmNarrationResult EnhanceResponse(
        string intent,
        string ruleBasedText,
        IEnumerable<string> toolSummaries,
        WorkerContextSummary? contextSummary,
        WorkerPersonaSummary? persona)
    {
        return EnhanceResponse(
            userMessage: string.Empty,
            intent: intent,
            ruleBasedText: ruleBasedText,
            toolSummaries: toolSummaries,
            contextSummary: contextSummary,
            persona: persona,
            recentMessages: null,
            reasoningSummary: string.Empty,
            planSummary: string.Empty);
    }

    public LlmNarrationResult EnhanceResponse(
        string userMessage,
        string intent,
        string ruleBasedText,
        IEnumerable<string> toolSummaries,
        WorkerContextSummary? contextSummary,
        WorkerPersonaSummary? persona,
        IEnumerable<WorkerChatMessage>? recentMessages,
        string? reasoningSummary,
        string? planSummary)
    {
        if (!_isLlmReal)
        {
            return LlmNarrationResult.RuleOnly(ruleBasedText, "LLM narration client is not configured.");
        }

        var toolResults = string.Join("\n", toolSummaries ?? Enumerable.Empty<string>());
        var userPrompt = BuildResponsePrompt(
            userMessage,
            intent,
            ruleBasedText,
            toolResults,
            contextSummary,
            recentMessages,
            reasoningSummary,
            planSummary);
        var systemPrompt = BuildSystemPromptWithPersona(persona);

        return CallLlmWithResult(systemPrompt, userPrompt, ruleBasedText);
    }

    public string EnhanceResponseText(
        string intent,
        string ruleBasedText,
        IEnumerable<string> toolSummaries,
        WorkerContextSummary? contextSummary,
        WorkerPersonaSummary? persona)
    {
        return EnhanceResponse(
            userMessage: string.Empty,
            intent: intent,
            ruleBasedText: ruleBasedText,
            toolSummaries: toolSummaries,
            contextSummary: contextSummary,
            persona: persona,
            recentMessages: null,
            reasoningSummary: string.Empty,
            planSummary: string.Empty).Text;
    }

    public string EnhanceReasoningSummary(
        string intent,
        string ruleBasedReasoning,
        IReadOnlyList<string> plannedTools,
        WorkerPersonaSummary? persona)
    {
        if (!_isLlmReal)
        {
            return ruleBasedReasoning;
        }

        var toolList = plannedTools != null ? string.Join(", ", plannedTools) : string.Empty;
        var userPrompt =
            $"Intent: {intent}\n" +
            $"Planned tools: {toolList}\n" +
            $"Rule-based reasoning: {ruleBasedReasoning}\n\n" +
            "Rewrite the reasoning summary in concise Vietnamese. Explain the logic, not the internal engine.";

        return CallLlmWithFallback(PreferredSystemPrompt, userPrompt, ruleBasedReasoning);
    }

    public string EnhancePlanSummary(
        string intent,
        string ruleBasedPlan,
        IReadOnlyList<string> plannedTools,
        WorkerContextSummary? contextSummary,
        WorkerPersonaSummary? persona)
    {
        if (!_isLlmReal)
        {
            return ruleBasedPlan;
        }

        var toolList = plannedTools != null ? string.Join(", ", plannedTools) : string.Empty;
        var docTitle = contextSummary?.DocumentTitle ?? "khong ro";
        var viewName = contextSummary?.ActiveViewName ?? "khong ro";

        var userPrompt =
            $"Intent: {intent}\n" +
            $"Document: {docTitle}, View: {viewName}\n" +
            $"Planned tools: {toolList}\n" +
            $"Rule-based plan: {ruleBasedPlan}\n\n" +
            "Rewrite the plan in concise Vietnamese. Tell the user what will happen next and why. Mention preview or approval only when relevant.";

        return CallLlmWithFallback(BuildSystemPromptWithPersona(persona), userPrompt, ruleBasedPlan);
    }

    public string EnhanceWhyThisTool(
        string toolName,
        string ruleBasedWhy,
        string intent)
    {
        if (!_isLlmReal)
        {
            return ruleBasedWhy;
        }

        var userPrompt =
            $"Tool: {toolName}\n" +
            $"Intent: {intent}\n" +
            $"Rule-based rationale: {ruleBasedWhy}\n\n" +
            "Rewrite this as one concise Vietnamese sentence explaining why this tool is relevant now.";

        return CallLlmWithFallback(PreferredSystemPrompt, userPrompt, ruleBasedWhy);
    }

    private static string BuildSystemPromptWithPersona(WorkerPersonaSummary? persona)
    {
        if (persona == null)
        {
            return PreferredSystemPrompt;
        }

        var guardrails = persona.Guardrails != null && persona.Guardrails.Count > 0
            ? string.Join("; ", persona.Guardrails)
            : "Preview before mutation; do not perform destructive work without explicit approval.";

        return PreferredSystemPrompt +
            $"\nPersona: {persona.DisplayName} (tone: {persona.Tone}). " +
            $"Guardrails: {guardrails}";
    }

    private static string BuildResponsePrompt(
        string userMessage,
        string intent,
        string ruleBasedText,
        string toolResults,
        WorkerContextSummary? contextSummary,
        IEnumerable<WorkerChatMessage>? recentMessages,
        string? reasoningSummary,
        string? planSummary)
    {
        if (ShouldUseCompactConversationPrompt(intent))
        {
            return BuildCompactConversationPrompt(
                userMessage,
                intent,
                contextSummary,
                recentMessages,
                toolResults);
        }

        var docTitle = contextSummary?.DocumentTitle ?? "khong ro";
        var viewName = contextSummary?.ActiveViewName ?? "khong ro";
        var selectionCount = contextSummary?.SelectionCount ?? 0;

        var prompt = new StringBuilder(1024);
        prompt.AppendLine("Latest user message:")
            .AppendLine(string.IsNullOrWhiteSpace(userMessage) ? "(empty)" : userMessage.Trim())
            .AppendLine()
            .AppendLine($"Intent: {intent}")
            .AppendLine($"Document: {docTitle}")
            .AppendLine($"View: {viewName}")
            .AppendLine($"Selection count: {selectionCount}");

        if (!string.IsNullOrWhiteSpace(contextSummary?.Summary))
        {
            prompt.AppendLine($"Context summary: {TrimForPrompt(contextSummary?.Summary, 240)}");
        }

        if (ShouldIncludeProjectSummary(intent, userMessage, contextSummary))
        {
            prompt.AppendLine($"Project summary: {TrimForPrompt(contextSummary?.ProjectSummary, 220)}");
        }

        if (!string.IsNullOrWhiteSpace(planSummary))
        {
            prompt.AppendLine($"Current plan summary: {TrimForPrompt(planSummary, 220)}");
        }

        if (!string.IsNullOrWhiteSpace(reasoningSummary))
        {
            prompt.AppendLine($"Current reasoning summary: {TrimForPrompt(reasoningSummary, 220)}");
        }

        var conversationWindow = BuildConversationWindow(recentMessages);
        if (!string.IsNullOrWhiteSpace(conversationWindow))
        {
            prompt.AppendLine()
                .AppendLine("Recent conversation:")
                .AppendLine(conversationWindow);
        }

        if (!string.IsNullOrWhiteSpace(toolResults))
        {
            prompt.AppendLine()
                .AppendLine("Tool evidence:")
                .AppendLine(TrimForPrompt(toolResults, 1000));
        }

        prompt.AppendLine()
            .AppendLine("Fallback safety draft:")
            .AppendLine(ruleBasedText)
            .AppendLine()
            .AppendLine("Write the final assistant reply in Vietnamese.")
            .AppendLine("Use the fallback draft only as a safety reference; do not simply paraphrase it.")
            .AppendLine("Prefer the user's actual request, the conversation history, current Revit context, and tool evidence.")
            .AppendLine("If the request is simple and conversational, answer directly and naturally.")
            .AppendLine("If the request is about current context, mention the actual document and active view.")
            .AppendLine("If there is uncertainty, say what is uncertain instead of giving vague filler.");

        return prompt.ToString();
    }

    private static string BuildConversationWindow(IEnumerable<WorkerChatMessage>? recentMessages)
    {
        if (recentMessages == null)
        {
            return string.Empty;
        }

        var items = recentMessages
            .Where(x => x != null
                && !string.IsNullOrWhiteSpace(x.Content)
                && (string.Equals(x.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Role, WorkerMessageRoles.Worker, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.TimestampUtc)
            .Take(MaxHistoryMessages)
            .OrderBy(x => x.TimestampUtc)
            .Select(x => $"{NormalizeRoleLabel(x.Role)}: {TrimForPrompt(x.Content, 220)}")
            .ToList();

        return items.Count == 0 ? string.Empty : string.Join("\n", items);
    }

    private static bool ShouldUseCompactConversationPrompt(string intent)
    {
        return string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent, "identity_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent, "help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent, "context_query", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludeProjectSummary(string intent, string userMessage, WorkerContextSummary? contextSummary)
    {
        if (contextSummary == null || string.IsNullOrWhiteSpace(contextSummary.ProjectSummary))
        {
            return false;
        }

        if (ShouldUseCompactConversationPrompt(intent))
        {
            var message = (userMessage ?? string.Empty).ToLowerInvariant();
            return message.Contains("deep scan")
                || message.Contains("scan")
                || message.Contains("workspace")
                || message.Contains("project init")
                || message.Contains("project")
                || message.Contains("model health");
        }

        return true;
    }

    private static string BuildCompactConversationPrompt(
        string userMessage,
        string intent,
        WorkerContextSummary? contextSummary,
        IEnumerable<WorkerChatMessage>? recentMessages,
        string toolResults)
    {
        var docTitle = contextSummary?.DocumentTitle ?? "khong ro";
        var viewName = contextSummary?.ActiveViewName ?? "khong ro";
        var selectionCount = contextSummary?.SelectionCount ?? 0;

        var prompt = new StringBuilder(768);
        prompt.AppendLine("User message:")
            .AppendLine(string.IsNullOrWhiteSpace(userMessage) ? "(empty)" : userMessage.Trim())
            .AppendLine()
            .AppendLine("Current Revit context:")
            .AppendLine($"- document: {docTitle}")
            .AppendLine($"- view: {viewName}")
            .AppendLine($"- selection: {selectionCount}");

        var conversationWindow = BuildConversationWindow(recentMessages);
        if (!string.IsNullOrWhiteSpace(conversationWindow))
        {
            prompt.AppendLine()
                .AppendLine("Recent conversation:")
                .AppendLine(conversationWindow);
        }

        if (string.Equals(intent, "context_query", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(toolResults))
        {
            prompt.AppendLine()
                .AppendLine("Verified context evidence:")
                .AppendLine(TrimForPrompt(toolResults, 500));
        }

        prompt.AppendLine()
            .AppendLine("Reply rules:")
            .AppendLine("- Answer directly and naturally.")
            .AppendLine("- Mention the current document and view when useful.")
            .AppendLine("- Do not mention queue, deep scan, playbook, workflow internals, or hidden system state unless the user explicitly asks or it blocks the answer.")
            .AppendLine("- Do not list too many capabilities.");

        var intentGuide = BuildCompactIntentGuide(intent);
        if (!string.IsNullOrWhiteSpace(intentGuide))
        {
            prompt.AppendLine(intentGuide);
        }

        var example = BuildCompactIntentExample(intent);
        if (!string.IsNullOrWhiteSpace(example))
        {
            prompt.AppendLine()
                .AppendLine("Style example:")
                .AppendLine(example);
        }

        return prompt.ToString();
    }

    private static string BuildCompactIntentGuide(string intent)
    {
        if (string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase))
        {
            return "- Greeting intent: greet once, then offer at most 2 concrete things you can help with in this file right now.";
        }

        if (string.Equals(intent, "identity_query", StringComparison.OrdinalIgnoreCase))
        {
            return "- Identity intent: explain who you are and what you can inspect or do now in this Revit file.";
        }

        if (string.Equals(intent, "help", StringComparison.OrdinalIgnoreCase))
        {
            return "- Help intent: suggest 3 concrete tasks max, based on this file, without sounding scripted.";
        }

        if (string.Equals(intent, "context_query", StringComparison.OrdinalIgnoreCase))
        {
            return "- Context intent: summarize the current document, view, and selection first. Mention uncertainty only if a requested detail is unavailable.";
        }

        return string.Empty;
    }

    private static string BuildCompactIntentExample(string intent)
    {
        if (string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase))
        {
            return "User: chao em\nAssistant: Chào anh. Em đang ở file ABC.rvt, view {3D}. Em có thể kiểm tra nhanh context hiện tại hoặc rà QC model trước cho anh.";
        }

        if (string.Equals(intent, "identity_query", StringComparison.OrdinalIgnoreCase))
        {
            return "User: em la gi\nAssistant: Em là 765T Worker, trợ lý BIM chạy trực tiếp trong Revit. Ngay lúc này em có thể đọc context file đang mở, kiểm tra family/sheet/view, hoặc rà QC read-only cho anh.";
        }

        if (string.Equals(intent, "context_query", StringComparison.OrdinalIgnoreCase))
        {
            return "User: kiem tra context hien tai\nAssistant: Hiện em đang ở file ABC.rvt, view {3D}, chưa chọn phần tử nào. Nếu anh cần, em có thể đọc sâu hơn vào warnings, family, hoặc sheet.";
        }

        return string.Empty;
    }

    private static string NormalizeRoleLabel(string role)
    {
        if (string.Equals(role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase))
        {
            return "User";
        }

        return "Assistant";
    }

    private static string TrimForPrompt(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized.Substring(0, maxLength) + "...";
    }

    private string CallLlmWithFallback(string systemPrompt, string userPrompt, string fallbackText)
    {
        return CallLlmWithResult(systemPrompt, userPrompt, fallbackText).Text;
    }

    private LlmNarrationResult CallLlmWithResult(string systemPrompt, string userPrompt, string fallbackText)
    {
        // Offload to thread-pool to avoid deadlock when called from Revit UI thread.
        // CallLlmWithResultAsync contains HTTP calls that must NOT block the UI message pump.
        return Task.Run(() => CallLlmWithResultAsync(systemPrompt, userPrompt, fallbackText, CancellationToken.None))
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Async LLM narration — call from background thread to avoid blocking Revit UI thread.
    /// Sync <see cref="CallLlmWithResult"/> delegates here; prefer this overload for new code paths.
    /// </summary>
    internal async Task<LlmNarrationResult> CallLlmWithResultAsync(
        string systemPrompt, string userPrompt, string fallbackText, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(ResponseTimeoutSeconds));
            var result = await _llmClient.CompleteAsync(systemPrompt, userPrompt, cts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result) || result.Contains("[LLM not configured]"))
            {
                return LlmNarrationResult.Fallback(fallbackText, "LLM narration returned empty output. Using rule fallback.");
            }

            Trace.TraceInformation($"BIM765T LlmResponseEnhancer: LLM enhanced text ({result.Length} chars).");
            return LlmNarrationResult.Enhanced(result, "LLM narration generated the final response text.");
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning("BIM765T LlmResponseEnhancer: LLM call timed out. Using rule-based text.");
            return LlmNarrationResult.Fallback(fallbackText, $"LLM narration timed out after {ResponseTimeoutSeconds} seconds. Using rule fallback.");
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"BIM765T LlmResponseEnhancer: LLM call failed ({ex.GetType().Name}: {ex.Message}). Using rule-based text.");
            return LlmNarrationResult.Fallback(fallbackText, $"LLM narration failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Async overload of <see cref="EnhanceResponse(string,string,string,IEnumerable{string},WorkerContextSummary,WorkerPersonaSummary,IEnumerable{WorkerChatMessage},string,string)"/>.
    /// Runs LLM HTTP call without blocking the calling thread.
    /// </summary>
    public async Task<LlmNarrationResult> EnhanceResponseAsync(
        string userMessage,
        string intent,
        string ruleBasedText,
        IEnumerable<string> toolSummaries,
        WorkerContextSummary? contextSummary,
        WorkerPersonaSummary? persona,
        IEnumerable<WorkerChatMessage>? recentMessages,
        string? reasoningSummary,
        string? planSummary,
        CancellationToken cancellationToken)
    {
        if (!_isLlmReal)
        {
            return LlmNarrationResult.RuleOnly(ruleBasedText, "LLM narration client is not configured.");
        }

        var toolResults = string.Join("\n", toolSummaries ?? Enumerable.Empty<string>());
        var userPrompt = BuildResponsePrompt(
            userMessage, intent, ruleBasedText, toolResults,
            contextSummary, recentMessages, reasoningSummary, planSummary);
        var systemPrompt = BuildSystemPromptWithPersona(persona);

        return await CallLlmWithResultAsync(systemPrompt, userPrompt, ruleBasedText, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class LlmNarrationResult
{
    public string Text { get; private set; } = string.Empty;

    public string Mode { get; private set; } = WorkerNarrationModes.RuleOnly;

    public string Diagnostics { get; private set; } = string.Empty;

    public static LlmNarrationResult RuleOnly(string text, string diagnostics)
    {
        return new LlmNarrationResult
        {
            Text = text ?? string.Empty,
            Mode = WorkerNarrationModes.RuleOnly,
            Diagnostics = diagnostics ?? string.Empty
        };
    }

    public static LlmNarrationResult Enhanced(string text, string diagnostics)
    {
        return new LlmNarrationResult
        {
            Text = text ?? string.Empty,
            Mode = WorkerNarrationModes.LlmEnhanced,
            Diagnostics = diagnostics ?? string.Empty
        };
    }

    public static LlmNarrationResult Fallback(string text, string diagnostics)
    {
        return new LlmNarrationResult
        {
            Text = text ?? string.Empty,
            Mode = WorkerNarrationModes.LlmFallback,
            Diagnostics = diagnostics ?? string.Empty
        };
    }
}
