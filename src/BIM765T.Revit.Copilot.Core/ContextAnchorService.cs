using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ContextAnchorService
{
    public ContextSearchAnchorsResponse SearchAnchors(IEnumerable<TaskRun> runs, IEnumerable<TaskMemoryPromotionRecord> promotions, ContextSearchAnchorsRequest request)
    {
        request ??= new ContextSearchAnchorsRequest();
        var items = BuildAnchors(runs, promotions)
            .Select(x => new
            {
                Item = x,
                Score = ScoreAnchor(x, request.Query, request.Tags)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxResults))
            .Select(x =>
            {
                x.Item.Score = x.Score;
                return x.Item;
            })
            .ToList();

        return new ContextSearchAnchorsResponse
        {
            Query = request.Query,
            Items = items
        };
    }

    public MemoryFindSimilarRunsResponse FindSimilarRuns(IEnumerable<TaskRun> runs, MemoryFindSimilarRunsRequest request)
    {
        request ??= new MemoryFindSimilarRunsRequest();
        var queryTokens = Tokenize(string.Join(" ", new[]
        {
            request.Query,
            request.TaskKind,
            request.TaskName,
            request.DocumentKey
        }));

        var items = new List<SimilarTaskRunItem>();
        foreach (var run in runs ?? Array.Empty<TaskRun>())
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(request.RunId) && string.Equals(run.RunId, request.RunId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(request.TaskKind) && string.Equals(run.TaskKind, request.TaskKind, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (!string.IsNullOrWhiteSpace(request.TaskName) && string.Equals(run.TaskName, request.TaskName, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (!string.IsNullOrWhiteSpace(request.DocumentKey) && string.Equals(run.DocumentKey, request.DocumentKey, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            foreach (var token in queryTokens)
            {
                if (run.IntentSummary.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                    || run.PlanSummary.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                    || run.TaskName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                    || run.TaskKind.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 2;
                }
            }

            if (score <= 0)
            {
                continue;
            }

            items.Add(new SimilarTaskRunItem
            {
                RunId = run.RunId,
                TaskKind = run.TaskKind,
                TaskName = run.TaskName,
                DocumentKey = run.DocumentKey,
                Status = run.Status,
                Summary = string.IsNullOrWhiteSpace(run.PlanSummary) ? run.IntentSummary : run.PlanSummary,
                Score = score
            });
        }

        return new MemoryFindSimilarRunsResponse
        {
            Runs = items
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.RunId, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, request.MaxResults))
                .ToList()
        };
    }

    public ContextResolveBundleResponse ResolveBundle(
        IEnumerable<ContextBundleItem> hotItems,
        IEnumerable<TaskRun> runs,
        IEnumerable<TaskMemoryPromotionRecord> promotions,
        ContextResolveBundleRequest request)
    {
        request ??= new ContextResolveBundleRequest();
        var bundle = new List<ContextBundleItem>();
        if (request.IncludeHot)
        {
            bundle.AddRange((hotItems ?? Array.Empty<ContextBundleItem>())
                .Select(CloneItem)
                .Take(Math.Max(1, request.MaxAnchors / 2)));
        }

        if (request.IncludeWarm || request.IncludeCold)
        {
            var search = SearchAnchors(runs, promotions, new ContextSearchAnchorsRequest
            {
                Query = request.Query,
                Tags = request.Tags,
                MaxResults = Math.Max(1, request.MaxAnchors)
            });

            foreach (var item in search.Items)
            {
                if (request.IncludeWarm && string.Equals(item.Tier, "warm", StringComparison.OrdinalIgnoreCase))
                {
                    bundle.Add(CloneItem(item));
                }
                else if (request.IncludeCold && string.Equals(item.Tier, "cold", StringComparison.OrdinalIgnoreCase))
                {
                    bundle.Add(CloneItem(item));
                }
            }
        }

        return new ContextResolveBundleResponse
        {
            Query = request.Query,
            Items = bundle
                .GroupBy(x => x.AnchorId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderByDescending(y => y.Score).First())
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, request.MaxAnchors))
                .ToList()
        };
    }

    public List<ContextBundleItem> BuildAnchors(IEnumerable<TaskRun> runs, IEnumerable<TaskMemoryPromotionRecord> promotions)
    {
        var items = new List<ContextBundleItem>();
        foreach (var run in runs ?? Array.Empty<TaskRun>())
        {
            items.Add(new ContextBundleItem
            {
                AnchorId = "task:" + run.RunId,
                Tier = "warm",
                Title = $"{run.TaskKind} - {run.TaskName}",
                Summary = string.IsNullOrWhiteSpace(run.PlanSummary) ? run.IntentSummary : run.PlanSummary,
                SourceKind = "task_run",
                SourcePath = run.RunId,
                Tags = new List<string>(new[] { run.TaskKind, run.TaskName, run.Status }.Concat(run.Tags).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
                RetrievalHint = "Use task.get_run for full durable state."
            });
        }

        foreach (var promotion in promotions ?? Array.Empty<TaskMemoryPromotionRecord>())
        {
            items.Add(new ContextBundleItem
            {
                AnchorId = "memory:" + promotion.PromotionId,
                Tier = "warm",
                Title = $"{promotion.PromotionKind} - {promotion.TaskName}",
                Summary = promotion.Summary,
                SourceKind = "memory_promotion",
                SourcePath = promotion.PromotionId,
                Tags = new List<string>(new[] { promotion.PromotionKind, promotion.TaskKind, promotion.TaskName }.Concat(promotion.Tags).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
                RetrievalHint = "Use task.promote_memory_safe output path or state store to inspect full record."
            });
        }

        return items;
    }

    private static int ScoreAnchor(ContextBundleItem item, string query, IEnumerable<string> tags)
    {
        var score = 0;
        foreach (var token in Tokenize(query))
        {
            if (item.Title.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 5;
            }

            if (item.Summary.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 3;
            }

            if (item.Tags.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
        }

        foreach (var tag in tags ?? Array.Empty<string>())
        {
            if (item.Tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
        }

        return score;
    }

    private static ContextBundleItem CloneItem(ContextBundleItem item)
    {
        return new ContextBundleItem
        {
            AnchorId = item.AnchorId,
            Tier = item.Tier,
            Title = item.Title,
            Summary = item.Summary,
            SourceKind = item.SourceKind,
            SourcePath = item.SourcePath,
            Tags = new List<string>(item.Tags),
            RetrievalHint = item.RetrievalHint,
            Score = item.Score
        };
    }

    private static List<string> Tokenize(string? query)
    {
        return (query ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n', ',', ';', ':', '/', '\\', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

