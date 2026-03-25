using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class TaskMetricsService
{
    public TaskMetricsResponse Build(IEnumerable<TaskRun> runs, TaskMetricsRequest request)
    {
        request ??= new TaskMetricsRequest();
        var filtered = ApplyFilters(runs ?? Array.Empty<TaskRun>(), request);

        var rows = filtered
            .GroupBy(x => new
            {
                x.TaskKind,
                x.TaskName,
                x.DocumentKey
            })
            .Select(group =>
            {
                var materialized = group.ToList();
                return new TaskMetricRow
                {
                    TaskKind = group.Key.TaskKind,
                    TaskName = group.Key.TaskName,
                    DocumentKey = group.Key.DocumentKey,
                    TotalRuns = materialized.Count,
                    CompletedRuns = materialized.Count(x => string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Status, "verified", StringComparison.OrdinalIgnoreCase)),
                    VerifiedPassRuns = materialized.Count(x => string.Equals(x.VerificationStatus, "pass", StringComparison.OrdinalIgnoreCase)),
                    AverageDurationMs = materialized.Count == 0 ? 0d : materialized.Average(x => (double)x.DurationMs),
                    AverageChangedCount = materialized.Count == 0 ? 0d : materialized.Average(x => (double)x.ChangedIds.Count),
                    AverageResidualCount = materialized.Count == 0 ? 0d : materialized.Average(x => EstimateResidualCount(x))
                };
            })
            .OrderByDescending(x => x.TotalRuns)
            .ThenBy(x => x.TaskKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TaskName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        return new TaskMetricsResponse
        {
            Metrics = rows
        };
    }

    private static IEnumerable<TaskRun> ApplyFilters(IEnumerable<TaskRun> runs, TaskMetricsRequest request)
    {
        var filtered = runs;
        if (!string.IsNullOrWhiteSpace(request.TaskKind))
        {
            filtered = filtered.Where(x => string.Equals(x.TaskKind, request.TaskKind, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.TaskName))
        {
            filtered = filtered.Where(x => string.Equals(x.TaskName, request.TaskName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentKey))
        {
            filtered = filtered.Where(x => string.Equals(x.DocumentKey, request.DocumentKey, StringComparison.OrdinalIgnoreCase));
        }

        return filtered;
    }

    private static int EstimateResidualCount(TaskRun run)
    {
        if (string.IsNullOrWhiteSpace(run.ResidualSummary))
        {
            return 0;
        }

        return int.TryParse(run.ResidualSummary, out var residuals)
            ? residuals
            : 1;
    }
}
