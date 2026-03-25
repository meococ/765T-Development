using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ToolCapabilitySearchService
{
    public ToolCapabilityLookupResponse Search(IEnumerable<ToolManifest> manifests, ToolCapabilityLookupRequest request)
    {
        request ??= new ToolCapabilityLookupRequest();
        var queryTokens = Tokenize(request.Query);
        var requiredContext = request.RequiredContext ?? new List<string>();
        var riskTags = request.RiskTags ?? new List<string>();
        var capabilityDomain = request.CapabilityDomain ?? string.Empty;
        var discipline = request.Discipline ?? string.Empty;
        var issueKinds = request.IssueKinds ?? new List<string>();

        var matches = new List<ToolCapabilityMatch>();
        foreach (var manifest in manifests ?? Array.Empty<ToolManifest>())
        {
            var score = 0;
            var reasons = new List<string>();

            if (queryTokens.Count == 0)
            {
                score += 1;
            }

            foreach (var token in queryTokens)
            {
                if (manifest.ToolName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 8;
                    reasons.Add($"tool:{token}");
                }

                if (manifest.Description.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 4;
                    reasons.Add($"desc:{token}");
                }

                if (manifest.RiskTags.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 3;
                    reasons.Add($"risk:{token}");
                }

                if (manifest.RulePackTags.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 3;
                    reasons.Add($"rule:{token}");
                }

                if (string.Equals(manifest.CapabilityPack, token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 3;
                    reasons.Add($"pack:{token}");
                }

                if (string.Equals(manifest.SkillGroup, token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 3;
                    reasons.Add($"skill:{token}");
                }

                if (string.Equals(manifest.CapabilityDomain, token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 4;
                    reasons.Add($"capability:{token}");
                }

                if (manifest.IssueKinds.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 3;
                    reasons.Add($"issue:{token}");
                }
            }

            if (requiredContext.Count > 0)
            {
                var satisfied = requiredContext.Count(x => manifest.RequiredContext.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)));
                score += satisfied * 2;
                if (satisfied > 0)
                {
                    reasons.Add($"context:{satisfied}");
                }
            }

            if (riskTags.Count > 0)
            {
                var satisfied = riskTags.Count(x => manifest.RiskTags.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)));
                score += satisfied * 2;
                if (satisfied > 0)
                {
                    reasons.Add($"riskTags:{satisfied}");
                }
            }

            if (!string.IsNullOrWhiteSpace(capabilityDomain)
                && string.Equals(manifest.CapabilityDomain, capabilityDomain, StringComparison.OrdinalIgnoreCase))
            {
                score += 6;
                reasons.Add($"capabilityDomain:{capabilityDomain}");
            }

            if (!string.IsNullOrWhiteSpace(discipline)
                && manifest.SupportedDisciplines.Any(x => string.Equals(x, discipline, StringComparison.OrdinalIgnoreCase)))
            {
                score += 4;
                reasons.Add($"discipline:{discipline}");
            }

            if (issueKinds.Count > 0)
            {
                var satisfied = issueKinds.Count(x => manifest.IssueKinds.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)));
                score += satisfied * 3;
                if (satisfied > 0)
                {
                    reasons.Add($"issueKinds:{satisfied}");
                }
            }

            if (score <= 0)
            {
                continue;
            }

            matches.Add(new ToolCapabilityMatch
            {
                Manifest = manifest,
                Score = score,
                Reason = string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase))
            });
        }

        return new ToolCapabilityLookupResponse
        {
            Query = request.Query,
            Matches = matches
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Manifest.ToolName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, request.MaxResults))
                .ToList()
        };
    }

    private static List<string> Tokenize(string? value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n', ',', ';', ':', '/', '\\', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
