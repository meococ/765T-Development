using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Platform;
using ContractStatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.WorkerHost.Routing;

namespace BIM765T.Revit.WorkerHost.ExternalAi;

internal sealed class StandaloneConversationService
{
    private static readonly string[] MutationKeywords =
    {
        "create", "tao", "new sheet", "sheet moi", "place", "dat", "rename", "doi ten", "delete", "xoa",
        "apply", "thuc thi", "execute", "run tool", "preview", "approve", "reject", "cancel", "resume",
        "move", "copy", "set template", "load family", "sync", "export"
    };

    private static readonly string[] LiveContextKeywords =
    {
        "selection", "chon", "active view", "view hien tai", "current view", "open model", "model dang mo",
        "trong revit", "uiapp", "document hien tai", "element dang chon"
    };

    private static readonly string[] LocalIntentKeywords =
    {
        "hello", "hi", "xin chao", "chao", "help", "giup", "tong quan", "overview", "project",
        "du an", "context", "family", "model", "qc", "health", "kiem tra model", "phan tich family"
    };

    private readonly IMemorySearchService _memorySearch;

    public StandaloneConversationService(IMemorySearchService memorySearch)
    {
        _memorySearch = memorySearch ?? throw new ArgumentNullException(nameof(memorySearch));
    }

    public bool ShouldHandleLocally(ExternalAiChatRequest request)
    {
        request ??= new ExternalAiChatRequest();
        if (!string.IsNullOrWhiteSpace(request.DocumentKey)
            || !string.IsNullOrWhiteSpace(request.TargetDocument)
            || !string.IsNullOrWhiteSpace(request.TargetView))
        {
            return false;
        }

        var message = (request.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return true;
        }

        if (MutationKeywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (LiveContextKeywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Default to local handling when no mutation/live-context keywords detected.
        // This prevents unknown conversational messages from being routed to the kernel
        // pipe which blocks without Revit running.
        return true;
    }

    public async Task<WorkerResponse> BuildResponseAsync(
        ExternalAiChatRequest request,
        MissionPlan plan,
        RetrievalContext retrieval,
        LlmProviderConfiguration profile,
        CancellationToken cancellationToken)
    {
        request ??= new ExternalAiChatRequest();
        plan ??= new MissionPlan();
        retrieval ??= new RetrievalContext();
        profile ??= new LlmProviderConfiguration();

        var memoryHits = await _memorySearch.SearchAsync(request.Message ?? string.Empty, request.DocumentKey ?? string.Empty, 3, cancellationToken).ConfigureAwait(false);
        var message = BuildAssistantMessage(request.Message, plan, memoryHits);
        var evidenceItems = memoryHits
            .Where(hit => !string.IsNullOrWhiteSpace(hit.SourceRef))
            .Take(3)
            .Select(hit => new WorkerEvidenceItem
            {
                ArtifactRef = hit.SourceRef,
                Title = string.IsNullOrWhiteSpace(hit.Title) ? hit.SourceRef : hit.Title,
                Summary = string.IsNullOrWhiteSpace(hit.Snippet) ? retrieval.Summary : hit.Snippet,
                Status = "grounded",
                SourceToolName = "workerhost.local_conversation",
                VerificationMode = "memory",
                Verified = true
            })
            .ToList();

        var groundingRefs = memoryHits
            .Select(hit => hit.SourceRef)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Concat(plan.EvidenceRefs ?? new List<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorkerResponse
        {
            SessionId = request.SessionId ?? string.Empty,
            MissionId = request.MissionId ?? string.Empty,
            MissionStatus = WorkerMissionStates.Completed,
            Messages = new List<WorkerChatMessage>
            {
                new WorkerChatMessage
                {
                    Role = WorkerMessageRoles.Worker,
                    Content = message,
                    StatusCode = ContractStatusCodes.Ok
                }
            },
            ContextSummary = new WorkerContextSummary
            {
                DocumentKey = request.DocumentKey ?? string.Empty,
                DocumentTitle = string.IsNullOrWhiteSpace(request.TargetDocument) ? "workerhost-standalone" : request.TargetDocument,
                ActiveViewName = request.TargetView ?? string.Empty,
                WorkspaceId = "default",
                Summary = "Standalone WorkerHost conversation path without live Revit context.",
                ProjectSummary = retrieval.Summary,
                GroundingLevel = ResolveGroundingLevel(memoryHits, plan),
                GroundingSummary = memoryHits.Count == 0
                    ? "Answered from WorkerHost standalone path without live Revit context."
                    : $"Grounded by {memoryHits.Count} WorkerHost memory hit(s).",
                GroundingRefs = groundingRefs,
                ProjectTopRefs = groundingRefs
            },
            PlanSummary = string.IsNullOrWhiteSpace(plan.Summary)
                ? "Standalone conversational fast-path in WorkerHost."
                : plan.Summary,
            Stage = WorkerFlowStages.Normalize(plan.FlowState),
            EvidenceItems = evidenceItems,
            SurfaceHint = new WorkerSurfaceHint
            {
                SurfaceId = WorkerSurfaceIds.Assistant,
                Reason = "Standalone conversational reply",
                Emphasis = "local"
            },
            ConfiguredProvider = profile.ConfiguredProvider,
            PlannerModel = profile.PlannerPrimaryModel,
            ResponseModel = profile.ResponseModel,
            ReasoningMode = profile.ReasoningMode,
            AutonomyMode = plan.AutonomyMode,
            PlannerTraceSummary = plan.PlannerTraceSummary,
            ChosenToolSequence = plan.ChosenToolSequence?.ToList() ?? new List<string>()
        };
    }

    private static string BuildAssistantMessage(string? rawMessage, MissionPlan plan, IReadOnlyList<SemanticMemoryHit> memoryHits)
    {
        var message = (rawMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Just describe what you need. I can answer conversational questions in WorkerHost even without Revit open; model operations require Revit running.";
        }

        var prefix = message.Contains("hello", StringComparison.OrdinalIgnoreCase)
            || message.Contains("hi", StringComparison.OrdinalIgnoreCase)
            || message.Contains("chao", StringComparison.OrdinalIgnoreCase)
            || message.Contains("xin chao", StringComparison.OrdinalIgnoreCase)
            ? "Online in WorkerHost standalone mode. "
            : string.Empty;

        var groundedSuffix = memoryHits.Count == 0
            ? "No live Revit context on this path; open Revit and resend if you need model access or tool execution."
            : "Response grounded by WorkerHost memory/runtime context; open Revit to switch to mission path for live model operations.";

        if (message.Contains("tong quan", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overview", StringComparison.OrdinalIgnoreCase)
            || message.Contains("project", StringComparison.OrdinalIgnoreCase)
            || message.Contains("du an", StringComparison.OrdinalIgnoreCase)
            || message.Contains("context", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + FirstNonEmpty(plan.Summary, "I can help with project overview, capability guidance, and reading WorkerHost memory/context.") + " " + groundedSuffix;
        }

        if (message.Contains("family", StringComparison.OrdinalIgnoreCase)
            || message.Contains("model", StringComparison.OrdinalIgnoreCase)
            || message.Contains("qc", StringComparison.OrdinalIgnoreCase)
            || message.Contains("health", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + "I can help with directional analysis, workflow explanation, and next-step guidance at the conversational level. " + groundedSuffix;
        }

        if (message.Contains("help", StringComparison.OrdinalIgnoreCase)
            || message.Contains("giup", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + "I can answer chat, summarize project/runtime context, and guide next steps in WorkerHost. For create/modify/review on the live model, Revit needs to be open for the mission path."
                + (memoryHits.Count > 0 ? " I also found some relevant memory/runtime refs to ground the answer." : string.Empty);
        }

        return prefix + "Received your question on the standalone path. Keeping the response at the conversational/read-only level. " + groundedSuffix;
    }

    private static string ResolveGroundingLevel(IReadOnlyList<SemanticMemoryHit> memoryHits, MissionPlan plan)
    {
        if (memoryHits.Count > 0)
        {
            return WorkerGroundingLevels.DeepScanGrounded;
        }

        return string.IsNullOrWhiteSpace(plan.GroundingLevel)
            ? WorkerGroundingLevels.WorkspaceGrounded
            : plan.GroundingLevel;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
