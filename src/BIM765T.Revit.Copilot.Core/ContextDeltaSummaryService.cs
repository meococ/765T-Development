using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ContextDeltaInsight
{
    public int AddedElementEstimate { get; set; }
    public int RemovedElementEstimate { get; set; }
    public int ModifiedElementEstimate { get; set; }
    public List<CountByNameDto> TopCategories { get; set; } = new List<CountByNameDto>();
    public List<CountByNameDto> DisciplineHints { get; set; } = new List<CountByNameDto>();
    public List<string> RecentMutationKinds { get; set; } = new List<string>();
}

public sealed class ContextDeltaSummaryService
{
    public ContextDeltaSummaryResponse Build(
        TaskContextResponse context,
        QueueStateResponse queueState,
        IEnumerable<ToolManifest> manifests,
        ContextDeltaSummaryRequest request,
        ContextDeltaInsight? insight = null)
    {
        context ??= new TaskContextResponse();
        queueState ??= new QueueStateResponse();
        request ??= new ContextDeltaSummaryRequest();
        insight ??= new ContextDeltaInsight();

        var catalog = (manifests ?? context.Tools ?? new List<ToolManifest>())
            .ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
        var recentOperations = (context.RecentOperations ?? new List<OperationJournalEntry>())
            .OrderByDescending(x => x.StartedUtc)
            .Take(Math.Max(1, request.MaxRecentOperations))
            .ToList();
        var recentEvents = (context.RecentEvents ?? new List<EventRecord>())
            .OrderByDescending(x => x.TimestampUtc)
            .Take(Math.Max(1, request.MaxRecentEvents))
            .ToList();

        var lastMutation = recentOperations.FirstOrDefault(x => IsMutation(x.ToolName, catalog));
        var lastFailure = recentOperations.FirstOrDefault(x => !x.Succeeded);
        var changedCount = recentOperations.SelectMany(x => x.ChangedIds ?? new List<int>()).Distinct().Count();
        var suggested = BuildSuggestedTools(queueState, lastMutation, lastFailure, changedCount, catalog);
        var fallbackInsight = BuildFallbackInsight(recentOperations, changedCount);
        var effectiveInsight = MergeInsight(fallbackInsight, insight);

        return new ContextDeltaSummaryResponse
        {
            DocumentKey = !string.IsNullOrWhiteSpace(request.DocumentKey) ? request.DocumentKey : context.Document?.DocumentKey ?? string.Empty,
            RecentOperationCount = recentOperations.Count,
            RecentEventCount = recentEvents.Count,
            RecentChangedElementCount = changedCount,
            LastMutationTool = lastMutation?.ToolName ?? string.Empty,
            LastFailureCode = lastFailure?.StatusCode ?? string.Empty,
            Summary = BuildSummary(queueState, recentOperations, recentEvents, changedCount, lastMutation, lastFailure, effectiveInsight),
            SuggestedNextTools = suggested.Take(Math.Max(1, request.MaxRecommendations)).ToList(),
            AddedElementEstimate = effectiveInsight.AddedElementEstimate,
            RemovedElementEstimate = effectiveInsight.RemovedElementEstimate,
            ModifiedElementEstimate = effectiveInsight.ModifiedElementEstimate,
            TopCategories = effectiveInsight.TopCategories.Take(5).ToList(),
            DisciplineHints = effectiveInsight.DisciplineHints.Take(5).ToList(),
            RecentMutationKinds = effectiveInsight.RecentMutationKinds.Take(5).ToList()
        };
    }

    private static List<string> BuildSuggestedTools(
        QueueStateResponse queueState,
        OperationJournalEntry? lastMutation,
        OperationJournalEntry? lastFailure,
        int changedCount,
        IReadOnlyDictionary<string, ToolManifest> catalog)
    {
        var suggested = new List<string>();
        if (queueState.HasActiveInvocation || queueState.PendingCount > 0)
        {
            AddIfKnown(catalog, suggested, ToolNames.SessionGetQueueState);
        }

        if (string.Equals(lastFailure?.StatusCode, StatusCodes.ContextMismatch, StringComparison.OrdinalIgnoreCase))
        {
            AddIfKnown(catalog, suggested, ToolNames.DocumentGetContextFingerprint);
            AddIfKnown(catalog, suggested, ToolNames.ContextGetHotState);
        }

        var lastFailureCode = lastFailure?.StatusCode ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(lastFailureCode)
            && (string.Equals(lastFailureCode, StatusCodes.ApprovalInvalid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lastFailureCode, StatusCodes.ApprovalExpired, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lastFailureCode, StatusCodes.ApprovalMismatch, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lastFailureCode, StatusCodes.PreviewRunRequired, StringComparison.OrdinalIgnoreCase)))
        {
            AddIfKnown(catalog, suggested, ToolNames.ToolGetGuidance);
            AddIfKnown(catalog, suggested, ToolNames.SessionGetTaskContext);
        }

        if (lastMutation != null)
        {
            AddIfKnown(catalog, suggested, ToolNames.SessionGetRecentOperations);
            AddIfKnown(catalog, suggested, ToolNames.ReviewCaptureSnapshot);
        }

        if (changedCount > 0)
        {
            AddIfKnown(catalog, suggested, ToolNames.ContextGetHotState);
            AddIfKnown(catalog, suggested, ToolNames.ToolGetGuidance);
        }

        if (suggested.Count == 0)
        {
            AddIfKnown(catalog, suggested, ToolNames.SessionGetTaskContext);
        }

        return suggested;
    }

    private static bool IsMutation(string toolName, IReadOnlyDictionary<string, ToolManifest> catalog)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (!catalog.TryGetValue(toolName, out var manifest))
        {
            return false;
        }

        return manifest.MutatesModel || manifest.PermissionLevel == PermissionLevel.FileLifecycle;
    }

    private static ContextDeltaInsight BuildFallbackInsight(IReadOnlyList<OperationJournalEntry> recentOperations, int changedCount)
    {
        var insight = new ContextDeltaInsight();
        if (changedCount <= 0)
        {
            return insight;
        }

        var addEstimate = 0;
        var removeEstimate = 0;
        var modifyEstimate = 0;
        var mutationKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in recentOperations)
        {
            var ids = operation.ChangedIds?.Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0)
            {
                continue;
            }

            var mutationKind = ClassifyMutationKind(operation.ToolName);
            mutationKinds.Add(mutationKind);

            switch (mutationKind)
            {
                case "create":
                    addEstimate += ids.Count;
                    break;
                case "delete":
                    removeEstimate += ids.Count;
                    break;
                default:
                    modifyEstimate += ids.Count;
                    break;
            }
        }

        if (addEstimate == 0 && removeEstimate == 0 && modifyEstimate == 0)
        {
            modifyEstimate = changedCount;
        }

        insight.AddedElementEstimate = addEstimate;
        insight.RemovedElementEstimate = removeEstimate;
        insight.ModifiedElementEstimate = Math.Max(modifyEstimate, Math.Max(0, changedCount - addEstimate - removeEstimate));
        insight.RecentMutationKinds = mutationKinds.ToList();
        return insight;
    }

    private static ContextDeltaInsight MergeInsight(ContextDeltaInsight fallbackInsight, ContextDeltaInsight? suppliedInsight)
    {
        if (suppliedInsight == null)
        {
            return fallbackInsight;
        }

        return new ContextDeltaInsight
        {
            AddedElementEstimate = suppliedInsight.AddedElementEstimate > 0 ? suppliedInsight.AddedElementEstimate : fallbackInsight.AddedElementEstimate,
            RemovedElementEstimate = suppliedInsight.RemovedElementEstimate > 0 ? suppliedInsight.RemovedElementEstimate : fallbackInsight.RemovedElementEstimate,
            ModifiedElementEstimate = suppliedInsight.ModifiedElementEstimate > 0 ? suppliedInsight.ModifiedElementEstimate : fallbackInsight.ModifiedElementEstimate,
            TopCategories = suppliedInsight.TopCategories.Count > 0 ? suppliedInsight.TopCategories : fallbackInsight.TopCategories,
            DisciplineHints = suppliedInsight.DisciplineHints.Count > 0 ? suppliedInsight.DisciplineHints : fallbackInsight.DisciplineHints,
            RecentMutationKinds = suppliedInsight.RecentMutationKinds.Count > 0 ? suppliedInsight.RecentMutationKinds : fallbackInsight.RecentMutationKinds
        };
    }

    private static string BuildSummary(
        QueueStateResponse queueState,
        IReadOnlyList<OperationJournalEntry> recentOperations,
        IReadOnlyList<EventRecord> recentEvents,
        int changedCount,
        OperationJournalEntry? lastMutation,
        OperationJournalEntry? lastFailure,
        ContextDeltaInsight insight)
    {
        var parts = new List<string>();

        if (queueState.HasActiveInvocation)
        {
            parts.Add("Queue is actively running " + queueState.ActiveToolName + " at stage " + (string.IsNullOrWhiteSpace(queueState.ActiveStage) ? "execution" : queueState.ActiveStage) + ".");
            if (queueState.ActiveElapsedMs > 0)
            {
                parts.Add("Active elapsed " + queueState.ActiveElapsedMs + "ms.");
            }
        }
        else if (queueState.PendingCount > 0)
        {
            parts.Add("Queue still has " + queueState.PendingCount + " pending request(s) (high/normal/low="
                      + queueState.PendingHighPriorityCount
                      + "/"
                      + queueState.PendingNormalPriorityCount
                      + "/"
                      + queueState.PendingLowPriorityCount
                      + ").");
        }

        if (lastMutation != null)
        {
            parts.Add("Last mutation tool: " + lastMutation.ToolName + ".");
        }

        if (lastFailure != null)
        {
            parts.Add("Last failure: " + lastFailure.StatusCode + ".");
        }

        if (changedCount > 0)
        {
            parts.Add(changedCount + " changed element(s) were seen in the hot window.");
        }

        if (insight.AddedElementEstimate > 0 || insight.RemovedElementEstimate > 0 || insight.ModifiedElementEstimate > 0)
        {
            parts.Add("Estimated delta add/remove/modify = "
                      + insight.AddedElementEstimate
                      + "/"
                      + insight.RemovedElementEstimate
                      + "/"
                      + insight.ModifiedElementEstimate
                      + ".");
        }

        if (insight.TopCategories.Count > 0)
        {
            parts.Add("Top categories: " + string.Join(", ", insight.TopCategories.Select(x => x.Name + "=" + x.Count)) + ".");
        }

        if (insight.DisciplineHints.Count > 0)
        {
            parts.Add("Discipline hints: " + string.Join(", ", insight.DisciplineHints.Select(x => x.Name + "=" + x.Count)) + ".");
        }

        if (insight.RecentMutationKinds.Count > 0)
        {
            parts.Add("Recent mutation kinds: " + string.Join(", ", insight.RecentMutationKinds) + ".");
        }

        if (parts.Count == 0)
        {
            parts.Add("No meaningful hot delta was found in recent operations/events.");
        }

        parts.Add("Recent ops=" + recentOperations.Count + ", recent events=" + recentEvents.Count + ".");
        return string.Join(" ", parts);
    }

    private static string ClassifyMutationKind(string toolName)
    {
        var value = toolName ?? string.Empty;
        if (value.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("remove", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("cleanup", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "delete";
        }

        if (value.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("place", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "create";
        }

        return "modify";
    }

    private static void AddDistinct(ICollection<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static void AddIfKnown(IReadOnlyDictionary<string, ToolManifest> catalog, ICollection<string> values, string toolName)
    {
        if (catalog.ContainsKey(toolName))
        {
            AddDistinct(values, toolName);
        }
    }
}
