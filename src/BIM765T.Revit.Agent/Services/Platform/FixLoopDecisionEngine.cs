using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal static class FixLoopDecisionEngine
{
    internal static ParameterRuleCandidate? ResolveParameterRule(string parameterName, string categoryName, string familyName, string elementName, IEnumerable<ParameterRuleCandidate> rules)
    {
        return rules
            .Where(rule => IsParameterRuleMatch(rule, parameterName, categoryName, familyName, elementName))
            .OrderByDescending(rule => ScoreParameterRule(rule))
            .ThenBy(rule => rule.ParameterName == "*" ? 1 : 0)
            .FirstOrDefault();
    }

    internal static ViewTemplateRuleCandidate? ResolveViewTemplateRule(string viewType, string viewName, string currentTemplateName, string sheetNumber, IEnumerable<ViewTemplateRuleCandidate> rules)
    {
        return rules
            .Where(rule => IsViewTemplateRuleMatch(rule, viewType, viewName, currentTemplateName, sheetNumber))
            .OrderByDescending(ScoreViewTemplateRule)
            .FirstOrDefault();
    }

    internal static List<FixLoopCandidateAction> SortActions(IEnumerable<FixLoopCandidateAction> actions)
    {
        return actions
            .OrderByDescending(action => action.IsRecommended)
            .ThenByDescending(action => action.Priority)
            .ThenBy(action => GetRiskRank(action.RiskLevel))
            .ThenByDescending(action => action.Verification.ExpectedIssueDelta)
            .ThenBy(action => action.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static List<string> SelectDefaultActionIds(IEnumerable<FixLoopCandidateAction> actions)
    {
        var ordered = SortActions(actions);
        var recommendedExecutable = ordered.Where(action => action.IsExecutable && action.IsRecommended).Select(action => action.ActionId).ToList();
        if (recommendedExecutable.Count > 0)
        {
            return recommendedExecutable;
        }

        var executable = ordered.Where(action => action.IsExecutable).Select(action => action.ActionId).ToList();
        return executable.Count > 0 ? executable : ordered.Select(action => action.ActionId).ToList();
    }

    internal static int GetExpectedIssueDelta(IEnumerable<FixLoopCandidateAction> actions)
    {
        return actions.Sum(action => Math.Max(0, action.Verification.ExpectedIssueDelta));
    }

    internal static IEnumerable<string> GetProjectOverridePathCandidates(string rootDirectory, string playbookName, string documentTitle, string documentPath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(playbookName))
        {
            yield break;
        }

        foreach (var key in GetProjectKeyCandidates(documentTitle, documentPath))
        {
            yield return Path.Combine(rootDirectory, "projects", key, playbookName + ".json");
        }

        yield return Path.Combine(rootDirectory, playbookName + ".json");
    }

    private static bool IsParameterRuleMatch(ParameterRuleCandidate rule, string parameterName, string categoryName, string familyName, string elementName)
    {
        if (!MatchesToken(rule.ParameterName, parameterName))
        {
            return false;
        }

        if (!MatchesOptionalToken(rule.CategoryName, categoryName))
        {
            return false;
        }

        if (!MatchesOptionalToken(rule.FamilyName, familyName))
        {
            return false;
        }

        if (!MatchesOptionalContains(rule.ElementNameContains, elementName))
        {
            return false;
        }

        return true;
    }

    private static bool IsViewTemplateRuleMatch(ViewTemplateRuleCandidate rule, string viewType, string viewName, string currentTemplateName, string sheetNumber)
    {
        if (!MatchesOptionalToken(rule.ViewType, viewType))
        {
            return false;
        }

        if (!MatchesOptionalContains(rule.ViewNameContains, viewName))
        {
            return false;
        }

        if (!MatchesOptionalContains(rule.CurrentTemplateNameContains, currentTemplateName))
        {
            return false;
        }

        if (!MatchesOptionalPrefix(rule.SheetNumberPrefix, sheetNumber))
        {
            return false;
        }

        return true;
    }

    private static int ScoreParameterRule(ParameterRuleCandidate rule)
    {
        var score = Math.Max(0, rule.Priority) * 100;
        if (!string.IsNullOrWhiteSpace(rule.CategoryName))
        {
            score += 30;
        }

        if (!string.IsNullOrWhiteSpace(rule.FamilyName))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(rule.ElementNameContains))
        {
            score += 10;
        }

        if (!string.Equals(rule.ParameterName, "*", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }

    private static int ScoreViewTemplateRule(ViewTemplateRuleCandidate rule)
    {
        var score = Math.Max(0, rule.Priority) * 100;
        if (!string.IsNullOrWhiteSpace(rule.ViewType))
        {
            score += 30;
        }

        if (!string.IsNullOrWhiteSpace(rule.ViewNameContains))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(rule.CurrentTemplateNameContains))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(rule.SheetNumberPrefix))
        {
            score += 10;
        }

        return score;
    }

    private static IEnumerable<string> GetProjectKeyCandidates(string documentTitle, string documentPath)
    {
        var keys = new List<string>();
        AddProjectKey(keys, documentTitle);
        AddProjectKey(keys, Path.GetFileNameWithoutExtension(documentPath));
        var directoryName = Path.GetDirectoryName(documentPath);
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            AddProjectKey(keys, directoryName);
        }

        return keys.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddProjectKey(ICollection<string> keys, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var buffer = new List<char>(raw.Length);
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Add(char.ToLowerInvariant(ch));
            }
            else if (buffer.Count == 0 || buffer[buffer.Count - 1] != '-')
            {
                buffer.Add('-');
            }
        }

        var normalized = new string(buffer.ToArray()).Trim('-');
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            keys.Add(normalized);
        }
    }

    private static bool MatchesToken(string expected, string actual)
    {
        if (string.Equals(expected, "*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(expected ?? string.Empty, actual ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOptionalToken(string expected, string actual)
    {
        return string.IsNullOrWhiteSpace(expected) || string.Equals(expected, actual ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOptionalContains(string expectedFragment, string actual)
    {
        return string.IsNullOrWhiteSpace(expectedFragment) || (actual ?? string.Empty).IndexOf(expectedFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesOptionalPrefix(string prefix, string actual)
    {
        return string.IsNullOrWhiteSpace(prefix) || (actual ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetRiskRank(string riskLevel)
    {
        if (string.Equals(riskLevel, "low", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(riskLevel, "medium", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(riskLevel, "high", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }
}

internal sealed class ParameterRuleCandidate
{
    internal string ParameterName { get; set; } = string.Empty;
    internal string CategoryName { get; set; } = string.Empty;
    internal string FamilyName { get; set; } = string.Empty;
    internal string ElementNameContains { get; set; } = string.Empty;
    internal string Strategy { get; set; } = string.Empty;
    internal string FillValue { get; set; } = string.Empty;
    internal int SourceElementId { get; set; }
    internal string RiskLevel { get; set; } = string.Empty;
    internal string Recommendation { get; set; } = string.Empty;
    internal int Priority { get; set; }
}

internal sealed class ViewTemplateRuleCandidate
{
    internal string ViewType { get; set; } = string.Empty;
    internal string ViewNameContains { get; set; } = string.Empty;
    internal string CurrentTemplateNameContains { get; set; } = string.Empty;
    internal string SheetNumberPrefix { get; set; } = string.Empty;
    internal string TargetTemplateName { get; set; } = string.Empty;
    internal string TargetTemplateNameContains { get; set; } = string.Empty;
    internal string RiskLevel { get; set; } = string.Empty;
    internal string Recommendation { get; set; } = string.Empty;
    internal int Priority { get; set; }
}
