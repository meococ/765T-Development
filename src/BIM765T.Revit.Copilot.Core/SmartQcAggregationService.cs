using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public sealed class SmartQcEvidenceBundle
{
    public ModelHealthResponse? ModelHealth { get; set; }

    public WorksetHealthResponse? WorksetHealth { get; set; }

    public DuplicateElementsResponse? Duplicates { get; set; }

    public ModelStandardsResponse? Standards { get; set; }

    public Dictionary<string, NamingAuditResponse> NamingByScope { get; set; } = new Dictionary<string, NamingAuditResponse>(StringComparer.OrdinalIgnoreCase);

    public List<SheetSummaryResponse> Sheets { get; set; } = new List<SheetSummaryResponse>();

    public List<string> ExecutedChecks { get; set; } = new List<string>();
}

[DataContract]
public sealed class SmartQcRulesetDefinition
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<SmartQcRuleDefinition> Rules { get; set; } = new List<SmartQcRuleDefinition>();
}

[DataContract]
public sealed class SmartQcRuleDefinition
{
    [DataMember(Order = 1)]
    public string RuleId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CheckKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Severity { get; set; } = "warning";

    [DataMember(Order = 6)]
    public string StandardRef { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int Threshold { get; set; }

    [DataMember(Order = 8)]
    public int MaxItems { get; set; } = 10;

    [DataMember(Order = 9)]
    public bool Enabled { get; set; } = true;

    [DataMember(Order = 10)]
    public string Scope { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string SuggestedAction { get; set; } = string.Empty;
}

public sealed class SmartQcAggregationService
{
    public SmartQcRulesetDefinition LoadRuleset(string rulesetName, out string resolvedPath)
    {
        var normalized = NormalizeRulesetName(rulesetName);
        foreach (var candidate in ResolveCandidatePaths(AppContext.BaseDirectory, normalized))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var definition = JsonUtil.DeserializeRequired<SmartQcRulesetDefinition>(File.ReadAllText(candidate));
                if (definition.Rules.Count > 0)
                {
                    resolvedPath = candidate;
                    return Normalize(definition, normalized);
                }
            }
            catch (InvalidDataException ex)
            {
                Trace.WriteLine($"SmartQc ruleset parse failed for '{candidate}': {ex.Message}");
            }
            catch (IOException ex)
            {
                Trace.WriteLine($"SmartQc ruleset read failed for '{candidate}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Trace.WriteLine($"SmartQc ruleset access denied for '{candidate}': {ex.Message}");
            }
        }

        resolvedPath = "builtin:base-rules";
        return CreateBuiltInBaseRuleset();
    }

    public SmartQcResponse Build(SmartQcEvidenceBundle evidence, SmartQcRequest request, SmartQcRulesetDefinition ruleset, string resolvedPath)
    {
        evidence ??= new SmartQcEvidenceBundle();
        request ??= new SmartQcRequest();
        ruleset ??= CreateBuiltInBaseRuleset();

        var findings = new List<SmartQcFinding>();
        var evaluated = new List<string>();

        foreach (var rule in ruleset.Rules.Where(x => x.Enabled))
        {
            evaluated.Add(rule.RuleId);
            switch ((rule.CheckKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "model.missing_path":
                    AppendModelMissingPath(findings, evidence, rule);
                    break;
                case "model.warnings_threshold":
                    AppendModelWarnings(findings, evidence, rule);
                    break;
                case "model.unloaded_links":
                    AppendUnloadedLinks(findings, evidence, rule);
                    break;
                case "workset.selection_non_editable":
                    AppendWorksetSelectionNonEditable(findings, evidence, rule);
                    break;
                case "naming.violations":
                    AppendNamingViolations(findings, evidence, rule);
                    break;
                case "duplicates.groups":
                    AppendDuplicateGroups(findings, evidence, rule);
                    break;
                case "standards.failed_rules":
                    AppendStandardsFailures(findings, evidence, rule);
                    break;
                case "sheets.missing_titleblock":
                    AppendSheetIssues(findings, evidence, rule, "TITLEBLOCK_MISSING");
                    break;
                case "sheets.empty_layout":
                    AppendSheetIssues(findings, evidence, rule, "SHEET_EMPTY_LAYOUT");
                    break;
                case "sheets.parameter_completeness":
                    AppendSheetIssues(findings, evidence, rule, "SHEET_PARAMETER_MISSING_OR_EMPTY");
                    break;
            }
        }

        var limited = findings
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenBy(x => x.RuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.EvidenceRef, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxFindings))
            .ToList();

        return new SmartQcResponse
        {
            DocumentKey = request.DocumentKey ?? string.Empty,
            RulesetName = ruleset.Name,
            RulesetDescription = ruleset.Description,
            RulesetPath = resolvedPath,
            ExecutedCheckCount = evidence.ExecutedChecks.Count,
            FindingCount = limited.Count,
            Summary = BuildSummary(ruleset, evidence, limited),
            ExecutedChecks = evidence.ExecutedChecks.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RulesEvaluated = evaluated,
            Findings = limited
        };
    }

    public static IReadOnlyList<string> ResolveCandidatePaths(string? baseDirectory, string rulesetName)
    {
        var fileName = NormalizeRulesetName(rulesetName) + ".json";
        var results = new List<string>
        {
            Path.Combine(baseDirectory ?? string.Empty, "rulesets", fileName)
        };

        var repoRoot = FindRepoRoot(baseDirectory);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            results.Add(Path.Combine(repoRoot, "rulesets", fileName));
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AppendModelMissingPath(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule)
    {
        if (evidence.ModelHealth == null || evidence.ModelHealth.HasPath)
        {
            return;
        }

        findings.Add(CreateFinding(rule, ToolNames.ReviewModelHealth, "document", "Document chưa có PathName; save/sync/export sẽ bị giới hạn.", "document"));
    }

    private static void AppendModelWarnings(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule)
    {
        if (evidence.ModelHealth == null || evidence.ModelHealth.TotalWarnings <= Math.Max(0, rule.Threshold))
        {
            return;
        }

        findings.Add(CreateFinding(
            rule,
            ToolNames.ReviewModelHealth,
            "document",
            string.Format(CultureInfo.InvariantCulture, "Model có {0} warnings, vượt ngưỡng {1}.", evidence.ModelHealth.TotalWarnings, Math.Max(0, rule.Threshold)),
            "document"));
    }

    private static void AppendUnloadedLinks(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule)
    {
        if (evidence.ModelHealth == null || evidence.ModelHealth.TotalLinks <= evidence.ModelHealth.LoadedLinks)
        {
            return;
        }

        var unloaded = evidence.ModelHealth.TotalLinks - evidence.ModelHealth.LoadedLinks;
        findings.Add(CreateFinding(rule, ToolNames.ReviewLinksStatus, "coordination", string.Format(CultureInfo.InvariantCulture, "Có {0} Revit links chưa load.", unloaded), "links"));
    }

    private static void AppendWorksetSelectionNonEditable(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule)
    {
        if (evidence.WorksetHealth?.Review?.Issues == null)
        {
            return;
        }

        foreach (var issue in evidence.WorksetHealth.Review.Issues.Where(x => string.Equals(x.Code, "SELECTION_WORKSET_NOT_EDITABLE", StringComparison.OrdinalIgnoreCase)).Take(Math.Max(1, rule.MaxItems)))
        {
            findings.Add(CreateFinding(rule, ToolNames.ReviewWorksetHealth, "worksharing", issue.Message, issue.Code));
        }
    }

    private static void AppendNamingViolations(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule)
    {
        var scope = string.IsNullOrWhiteSpace(rule.Scope) ? "views" : rule.Scope.Trim().ToLowerInvariant();
        if (!evidence.NamingByScope.TryGetValue(scope, out var response) || response.Violations.Count == 0)
        {
            return;
        }

        foreach (var item in response.Violations.Take(Math.Max(1, rule.MaxItems)))
        {
            findings.Add(CreateFinding(rule, ToolNames.AuditNamingConvention, scope, item.Violation, scope + ":" + item.ElementId, item.ElementId, null));
        }
    }

    private static void AppendDuplicateGroups(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule)
    {
        if (evidence.Duplicates == null || evidence.Duplicates.DuplicateGroups.Count == 0)
        {
            return;
        }

        foreach (var group in evidence.Duplicates.DuplicateGroups.Take(Math.Max(1, rule.MaxItems)))
        {
            var evidenceRef = group.ElementIds.Count > 0 ? "element:" + group.ElementIds[0].ToString(CultureInfo.InvariantCulture) : "duplicates";
            var message = string.Format(CultureInfo.InvariantCulture, "Nhóm duplicate {0} ({1}) tại {2}.", group.TypeName, group.Category, group.Location);
            findings.Add(CreateFinding(rule, ToolNames.AuditDuplicateElements, "coordination", message, evidenceRef, group.ElementIds.FirstOrDefault(), null));
        }
    }

    private static void AppendStandardsFailures(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule)
    {
        if (evidence.Standards == null || evidence.Standards.Results.Count == 0)
        {
            return;
        }

        foreach (var item in evidence.Standards.Results.Where(x => !string.Equals(x.Status, "Pass", StringComparison.OrdinalIgnoreCase)).Take(Math.Max(1, rule.MaxItems)))
        {
            findings.Add(CreateFinding(rule, ToolNames.AuditModelStandards, "standards", item.Description, item.RuleName));
        }
    }

    private static void AppendSheetIssues(ICollection<SmartQcFinding> findings, SmartQcEvidenceBundle evidence, SmartQcRuleDefinition rule, string issueCode)
    {
        foreach (var sheet in evidence.Sheets)
        {
            var matching = (sheet.Review?.Issues ?? new List<ReviewIssue>())
                .Where(x => string.Equals(x.Code, issueCode, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Max(1, rule.MaxItems))
                .ToList();

            foreach (var issue in matching)
            {
                findings.Add(CreateFinding(
                    rule,
                    ToolNames.ReviewSheetSummary,
                    "documentation",
                    issue.Message,
                    string.Format(CultureInfo.InvariantCulture, "sheet:{0}", sheet.SheetId),
                    issue.ElementId,
                    sheet.SheetId));
            }
        }
    }

    private static SmartQcFinding CreateFinding(
        SmartQcRuleDefinition rule,
        string sourceTool,
        string categoryFallback,
        string message,
        string evidenceRef,
        int? elementId = null,
        int? sheetId = null)
    {
        return new SmartQcFinding
        {
            RuleId = rule.RuleId,
            Title = string.IsNullOrWhiteSpace(rule.Title) ? rule.RuleId : rule.Title,
            Category = string.IsNullOrWhiteSpace(rule.Category) ? categoryFallback : rule.Category,
            Severity = ParseSeverity(rule.Severity),
            Message = message ?? string.Empty,
            SourceTool = sourceTool,
            EvidenceRef = evidenceRef ?? string.Empty,
            SuggestedAction = rule.SuggestedAction ?? string.Empty,
            StandardRef = rule.StandardRef ?? string.Empty,
            ElementId = elementId,
            SheetId = sheetId
        };
    }

    private static DiagnosticSeverity ParseSeverity(string severity)
    {
        switch ((severity ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "error":
                return DiagnosticSeverity.Error;
            case "info":
                return DiagnosticSeverity.Info;
            case "warning":
            default:
                return DiagnosticSeverity.Warning;
        }
    }

    private static int SeverityRank(DiagnosticSeverity severity)
    {
        switch (severity)
        {
            case DiagnosticSeverity.Error:
                return 3;
            case DiagnosticSeverity.Warning:
                return 2;
            default:
                return 1;
        }
    }

    private static string BuildSummary(SmartQcRulesetDefinition ruleset, SmartQcEvidenceBundle evidence, IReadOnlyCollection<SmartQcFinding> findings)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Smart QC `{0}` dùng {1} check và tạo {2} finding.",
            ruleset.Name,
            evidence.ExecutedChecks.Count,
            findings.Count);
    }

    private static SmartQcRulesetDefinition Normalize(SmartQcRulesetDefinition definition, string normalizedName)
    {
        definition.Name = string.IsNullOrWhiteSpace(definition.Name) ? normalizedName : definition.Name.Trim();
        definition.Description = definition.Description ?? string.Empty;
        definition.Rules = definition.Rules ?? new List<SmartQcRuleDefinition>();
        foreach (var rule in definition.Rules)
        {
            rule.RuleId = (rule.RuleId ?? string.Empty).Trim();
            rule.CheckKey = (rule.CheckKey ?? string.Empty).Trim();
            rule.Title = rule.Title ?? string.Empty;
            rule.Category = rule.Category ?? string.Empty;
            rule.Severity = string.IsNullOrWhiteSpace(rule.Severity) ? "warning" : rule.Severity.Trim();
            rule.StandardRef = rule.StandardRef ?? string.Empty;
            rule.Scope = rule.Scope ?? string.Empty;
            rule.SuggestedAction = rule.SuggestedAction ?? string.Empty;
        }

        return definition;
    }

    private static string NormalizeRulesetName(string rulesetName)
    {
        if (string.IsNullOrWhiteSpace(rulesetName))
        {
            return "base-rules";
        }

        var value = rulesetName.Trim();
        if (value.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(0, value.Length - 5);
        }

        return value;
    }

    private static string FindRepoRoot(string? baseDirectory)
    {
        var current = new DirectoryInfo(baseDirectory ?? string.Empty);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BIM765T.Revit.Agent.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    private static SmartQcRulesetDefinition CreateBuiltInBaseRuleset()
    {
        return new SmartQcRulesetDefinition
        {
            Name = "base-rules",
            Description = "Ruleset QC nền cho document health, naming, standards, duplicates, và sheet hygiene cơ bản.",
            Rules = new List<SmartQcRuleDefinition>
            {
                new SmartQcRuleDefinition { RuleId = "DOC_NO_PATH", CheckKey = "model.missing_path", Title = "Document should have a saved path", Category = "document", Severity = "warning", SuggestedAction = "Save the model to establish a stable document path." },
                new SmartQcRuleDefinition { RuleId = "DOC_WARNINGS_LIMIT", CheckKey = "model.warnings_threshold", Title = "Model warnings should stay under threshold", Category = "document", Severity = "warning", Threshold = 50, SuggestedAction = "Review warnings and resolve high-frequency warning types first." },
                new SmartQcRuleDefinition { RuleId = "LINKS_UNLOADED", CheckKey = "model.unloaded_links", Title = "Linked models should be loaded", Category = "coordination", Severity = "warning", SuggestedAction = "Load missing links or confirm they are intentionally unloaded." },
                new SmartQcRuleDefinition { RuleId = "WORKSET_SELECTION_NOT_EDITABLE", CheckKey = "workset.selection_non_editable", Title = "Selection should not sit on non-editable worksets", Category = "worksharing", Severity = "warning", SuggestedAction = "Borrow/open the relevant worksets before mutation workflows." },
                new SmartQcRuleDefinition { RuleId = "VIEW_NAMING", CheckKey = "naming.violations", Title = "View names should follow naming hygiene", Category = "naming", Severity = "warning", Scope = "views", MaxItems = 10, SuggestedAction = "Rename copied or malformed views before issuing sheets." },
                new SmartQcRuleDefinition { RuleId = "DUPLICATE_ELEMENTS", CheckKey = "duplicates.groups", Title = "Model should avoid duplicate elements", Category = "coordination", Severity = "warning", MaxItems = 10, SuggestedAction = "Inspect duplicate groups and remove redundant instances." },
                new SmartQcRuleDefinition { RuleId = "STANDARDS_FAILED", CheckKey = "standards.failed_rules", Title = "Model standards checks should pass", Category = "standards", Severity = "warning", MaxItems = 10, SuggestedAction = "Review the failing standards checks and address the most impactful ones first." },
                new SmartQcRuleDefinition { RuleId = "SHEET_TITLEBLOCK", CheckKey = "sheets.missing_titleblock", Title = "Sheets should contain title blocks", Category = "documentation", Severity = "warning", MaxItems = 10, SuggestedAction = "Place a valid title block on every issued sheet." },
                new SmartQcRuleDefinition { RuleId = "SHEET_EMPTY_LAYOUT", CheckKey = "sheets.empty_layout", Title = "Sheets should not be empty", Category = "documentation", Severity = "warning", MaxItems = 10, SuggestedAction = "Place views or schedules before sheet issue review." },
                new SmartQcRuleDefinition { RuleId = "SHEET_REQUIRED_PARAMETERS", CheckKey = "sheets.parameter_completeness", Title = "Required sheet parameters should be filled", Category = "documentation", Severity = "warning", MaxItems = 10, SuggestedAction = "Fill missing sheet metadata before issue/export." }
            }
        };
    }
}
