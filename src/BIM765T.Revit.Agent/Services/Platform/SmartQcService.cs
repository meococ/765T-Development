using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Agent.Services.Review;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class SmartQcService
{
    private readonly PlatformServices _platform;
    private readonly ReviewRuleEngineService _reviewRuleEngine;
    private readonly AuditService _audit;
    private readonly SmartQcAggregationService _aggregation;

    internal SmartQcService(
        PlatformServices platform,
        ReviewRuleEngineService reviewRuleEngine,
        AuditService audit,
        SmartQcAggregationService aggregation)
    {
        _platform = platform;
        _reviewRuleEngine = reviewRuleEngine;
        _audit = audit;
        _aggregation = aggregation;
    }

    internal SmartQcResponse Run(UIApplication uiapp, Document doc, SmartQcRequest request)
    {
        request ??= new SmartQcRequest();
        if (string.IsNullOrWhiteSpace(request.DocumentKey))
        {
            request.DocumentKey = _platform.GetDocumentKey(doc);
        }

        var ruleset = _aggregation.LoadRuleset(request.RulesetName, out var resolvedPath);
        var evidence = new SmartQcEvidenceBundle();
        var checkKeys = new HashSet<string>(
            ruleset.Rules.Where(x => x.Enabled).Select(x => (x.CheckKey ?? string.Empty).Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        if (checkKeys.Any(x => x.StartsWith("model.", StringComparison.OrdinalIgnoreCase)))
        {
            evidence.ModelHealth = _platform.ReviewModelHealth(uiapp, doc);
            AddExecuted(evidence, ToolNames.ReviewModelHealth);
        }

        if (checkKeys.Any(x => x.StartsWith("workset.", StringComparison.OrdinalIgnoreCase)))
        {
            evidence.WorksetHealth = _platform.ReviewWorksetHealth(uiapp, doc);
            AddExecuted(evidence, ToolNames.ReviewWorksetHealth);
        }

        if (checkKeys.Contains("naming.violations"))
        {
            var scopes = ruleset.Rules
                .Where(x => x.Enabled && string.Equals(x.CheckKey, "naming.violations", StringComparison.OrdinalIgnoreCase))
                .Select(x => string.IsNullOrWhiteSpace(x.Scope) ? "views" : x.Scope.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var scope in scopes)
            {
                evidence.NamingByScope[scope] = _audit.AuditNaming(_platform, doc, new NamingAuditRequest
                {
                    DocumentKey = request.DocumentKey,
                    Scope = scope,
                    ExpectedPattern = request.NamingPattern,
                    MaxResults = Math.Max(1, request.MaxNamingViolations)
                });
            }

            AddExecuted(evidence, ToolNames.AuditNamingConvention);
        }

        if (checkKeys.Contains("duplicates.groups"))
        {
            evidence.Duplicates = _audit.AuditDuplicates(_platform, doc, new DuplicateElementsRequest
            {
                DocumentKey = request.DocumentKey,
                ToleranceMm = request.DuplicateToleranceMm,
                MaxResults = Math.Max(10, request.MaxFindings)
            });
            AddExecuted(evidence, ToolNames.AuditDuplicateElements);
        }

        if (checkKeys.Contains("standards.failed_rules"))
        {
            evidence.Standards = _audit.AuditModelStandards(_platform, doc, new ModelStandardsRequest
            {
                DocumentKey = request.DocumentKey
            });
            AddExecuted(evidence, ToolNames.AuditModelStandards);
        }

        if (checkKeys.Any(x => x.StartsWith("sheets.", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var sheet in ResolveTargetSheets(doc, request))
            {
                evidence.Sheets.Add(_platform.ReviewSheetSummary(uiapp, doc, new SheetSummaryRequest
                {
                    DocumentKey = request.DocumentKey,
                    SheetId = checked((int)sheet.Id.Value),
                    RequiredParameterNames = request.RequiredParameterNames ?? new List<string>()
                }));
            }

            if (evidence.Sheets.Count > 0)
            {
                AddExecuted(evidence, ToolNames.ReviewSheetSummary);
            }
        }

        if (evidence.ExecutedChecks.Count == 0)
        {
            var review = _reviewRuleEngine.Run(uiapp, _platform, doc, new ReviewRuleSetRunRequest
            {
                DocumentKey = request.DocumentKey,
                RuleSetName = "document_health_v1",
                MaxIssues = Math.Max(10, request.MaxFindings)
            }, string.Empty);
            evidence.ModelHealth = _platform.ReviewModelHealth(uiapp, doc);
            AddExecuted(evidence, review.RuleSetName);
            AddExecuted(evidence, ToolNames.ReviewModelHealth);
        }

        return _aggregation.Build(evidence, request, ruleset, resolvedPath);
    }

    private static IEnumerable<ViewSheet> ResolveTargetSheets(Document doc, SmartQcRequest request)
    {
        var allSheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(x => !x.IsPlaceholder)
            .OrderBy(x => x.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (request.SheetIds != null && request.SheetIds.Count > 0)
        {
            var requested = new HashSet<int>(request.SheetIds);
            return allSheets.Where(x => requested.Contains(checked((int)x.Id.Value))).Take(Math.Max(1, request.MaxSheets)).ToList();
        }

        if (request.SheetNumbers != null && request.SheetNumbers.Count > 0)
        {
            var requested = new HashSet<string>(request.SheetNumbers.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            return allSheets.Where(x => requested.Contains(x.SheetNumber ?? string.Empty)).Take(Math.Max(1, request.MaxSheets)).ToList();
        }

        return allSheets.Take(Math.Max(1, request.MaxSheets)).ToList();
    }

    private static void AddExecuted(SmartQcEvidenceBundle evidence, string check)
    {
        if (string.IsNullOrWhiteSpace(check))
        {
            return;
        }

        if (!evidence.ExecutedChecks.Contains(check, StringComparer.OrdinalIgnoreCase))
        {
            evidence.ExecutedChecks.Add(check);
        }
    }
}
