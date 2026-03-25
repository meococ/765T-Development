using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Phase 2: Smart View Template &amp; Sheet Analysis.
/// Read-only analysis tools — scoring, cross-references, intelligent recommendations.
/// Designed for senior BIM Manager workflows: grade, detect issues, suggest actions.
/// </summary>
internal sealed class TemplateSheetAnalysisService
{
    // ══════════════════════════════════════════════════════════
    // 1. audit.template_health
    // ══════════════════════════════════════════════════════════

    internal TemplateHealthResponse AuditTemplateHealth(PlatformServices services, Document doc, TemplateHealthRequest request)
    {
        request ??= new TemplateHealthRequest();
        var result = new TemplateHealthResponse { DocumentKey = services.GetDocumentKey(doc) };
        var review = new ReviewReport { Name = "audit.template_health", DocumentKey = result.DocumentKey };

        // Collect all views (templates + non-templates)
        var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var templates = allViews.Where(v => v.IsTemplate).ToList();
        var nonTemplates = allViews.Where(v => !v.IsTemplate).ToList();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.NameContains))
            templates = templates.Where(v => (v.Name ?? string.Empty).IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (!string.IsNullOrWhiteSpace(request.ViewType))
            templates = templates.Where(v => v.ViewType.ToString().IndexOf(request.ViewType, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        // Build usage map: templateId → count
        var usageMap = new Dictionary<long, int>();
        foreach (var v in nonTemplates)
        {
            var tmplId = v.ViewTemplateId;
            if (tmplId != null && tmplId != ElementId.InvalidElementId)
            {
                var key = tmplId.Value;
                usageMap[key] = usageMap.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        }

        // Analyze each template
        var usedTemplates = new List<View>();
        var unusedTemplates = new List<View>();
        var breakdownMap = new Dictionary<string, TemplateTypeBreakdown>();
        var namingRegex = !string.IsNullOrWhiteSpace(request.NamingPattern)
            ? TryCreateRegex(request.NamingPattern)
            : null;

        foreach (var tmpl in templates)
        {
            var tmplIdVal = tmpl.Id.Value;
            var usage = usageMap.TryGetValue(tmplIdVal, out var u) ? u : 0;
            var viewType = tmpl.ViewType.ToString();

            // ViewType breakdown
            if (!breakdownMap.TryGetValue(viewType, out var bd))
            {
                bd = new TemplateTypeBreakdown { ViewType = viewType };
                breakdownMap[viewType] = bd;
            }
            bd.TotalCount++;

            if (usage > 0)
            {
                bd.UsedCount++;
                usedTemplates.Add(tmpl);
            }
            else
            {
                bd.UnusedCount++;
                unusedTemplates.Add(tmpl);

                // Build unused item
                int filterCount = 0;
                try { filterCount = tmpl.GetFilters().Count; } catch { }

                var suggestedAction = filterCount > 5 ? "review_first" : "safe_to_delete";

                if (result.UnusedTemplatesList.Count < Math.Max(1, request.MaxResults))
                {
                    result.UnusedTemplatesList.Add(new UnusedTemplateItem
                    {
                        TemplateId = checked((int)tmplIdVal),
                        TemplateName = tmpl.Name ?? string.Empty,
                        ViewType = viewType,
                        FilterCount = filterCount,
                        SuggestedAction = suggestedAction
                    });
                }
            }

            // Naming violations
            var name = tmpl.Name ?? string.Empty;
            CheckNamingViolation(name, checked((int)tmplIdVal), namingRegex, result.NamingViolations);
        }

        // Duplicate detection (Levenshtein-based)
        var threshold = Math.Max(0.5, Math.Min(1.0, request.DuplicateSimilarityThreshold));
        DetectDuplicates(templates, usageMap, threshold, result.DuplicateGroups);

        // Summary
        result.TotalTemplates = templates.Count;
        result.UsedTemplates = usedTemplates.Count;
        result.UnusedTemplates = unusedTemplates.Count;
        result.UnusedPercentage = templates.Count > 0
            ? Math.Round(100.0 * unusedTemplates.Count / templates.Count, 1)
            : 0;
        result.SuspectDuplicateGroups = result.DuplicateGroups.Count;
        result.NamingViolationCount = result.NamingViolations.Count;
        result.ByViewType = breakdownMap.Values.OrderByDescending(b => b.TotalCount).ToList();

        // Grade
        result.OverallGrade = GradeTemplateHealth(result.UnusedPercentage, result.SuspectDuplicateGroups, result.NamingViolationCount);

        // ReviewReport issues
        if (result.UnusedPercentage >= 80)
            review.Issues.Add(new ReviewIssue { Code = "TEMPLATE_BLOAT_CRITICAL", Severity = DiagnosticSeverity.Error, Message = $"{result.UnusedPercentage:F0}% of templates are unused — severe bloat." });
        else if (result.UnusedPercentage >= 40)
            review.Issues.Add(new ReviewIssue { Code = "TEMPLATE_BLOAT_WARNING", Severity = DiagnosticSeverity.Warning, Message = $"{result.UnusedPercentage:F0}% of templates are unused." });

        if (result.SuspectDuplicateGroups > 0)
            review.Issues.Add(new ReviewIssue { Code = "TEMPLATE_DUPLICATES", Severity = DiagnosticSeverity.Warning, Message = $"{result.SuspectDuplicateGroups} groups of suspected duplicate templates found." });

        foreach (var nv in result.NamingViolations)
            review.Issues.Add(new ReviewIssue { Code = "TEMPLATE_NAMING_VIOLATION", Severity = DiagnosticSeverity.Warning, Message = $"Template '{nv.TemplateName}': {nv.Violation}", ElementId = nv.TemplateId });

        review.IssueCount = review.Issues.Count;
        result.Review = review;
        return result;
    }

    // ══════════════════════════════════════════════════════════
    // 2. audit.sheet_organization
    // ══════════════════════════════════════════════════════════

    internal SheetOrganizationResponse AuditSheetOrganization(PlatformServices services, Document doc, SheetOrganizationRequest request)
    {
        request ??= new SheetOrganizationRequest();
        var heavyThreshold = Math.Max(1, request.HeavySheetThreshold);
        var result = new SheetOrganizationResponse { DocumentKey = services.GetDocumentKey(doc) };
        var review = new ReviewReport { Name = "audit.sheet_organization", DocumentKey = result.DocumentKey };

        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(s => !s.IsPlaceholder)
            .OrderBy(s => s.SheetNumber ?? string.Empty)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        var groupMap = new Dictionary<string, List<(ViewSheet sheet, int vpCount)>>();
        var maxVP = 0;
        var totalVP = 0;
        Regex? groupRegex = !string.IsNullOrWhiteSpace(request.GroupByPattern) ? TryCreateRegex(request.GroupByPattern) : null;

        foreach (var sheet in sheets)
        {
            var sheetId = checked((int)sheet.Id.Value);
            var sheetNumber = sheet.SheetNumber ?? string.Empty;
            var sheetName = sheet.Name ?? string.Empty;
            var vpCount = sheet.GetAllViewports().Count;
            totalVP += vpCount;
            if (vpCount > maxVP) maxVP = vpCount;

            // Check title block
            bool hasTitleBlock;
            try
            {
                hasTitleBlock = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .GetElementCount() > 0;
            }
            catch { hasTitleBlock = true; /* assume OK if collector fails */ }

            // Detect issues
            if (vpCount == 0)
            {
                result.EmptySheetCount++;
                var severity = sheetName.IndexOf("Unnamed", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Error" : "Warning";
                result.Issues.Add(new SheetOrganizationIssue
                {
                    SheetId = sheetId, SheetNumber = sheetNumber, SheetName = sheetName,
                    IssueCode = "EMPTY_SHEET", Severity = severity,
                    Description = $"Sheet has 0 viewports{(severity == "Error" ? " and is unnamed" : "")}.",
                    ViewportCount = 0
                });
            }

            if (vpCount >= heavyThreshold)
            {
                result.HeavySheetCount++;
                result.Issues.Add(new SheetOrganizationIssue
                {
                    SheetId = sheetId, SheetNumber = sheetNumber, SheetName = sheetName,
                    IssueCode = "HEAVY_SHEET", Severity = vpCount >= 20 ? "Error" : "Warning",
                    Description = $"Sheet has {vpCount} viewports (threshold: {heavyThreshold}). Consider splitting.",
                    ViewportCount = vpCount
                });
            }

            if (!hasTitleBlock)
            {
                result.MissingTitleBlockCount++;
                result.Issues.Add(new SheetOrganizationIssue
                {
                    SheetId = sheetId, SheetNumber = sheetNumber, SheetName = sheetName,
                    IssueCode = "NO_TITLEBLOCK", Severity = "Warning",
                    Description = "Sheet has no title block instance.",
                    ViewportCount = vpCount
                });
            }

            if (sheetName.Equals("Unnamed", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(sheetName))
            {
                result.Issues.Add(new SheetOrganizationIssue
                {
                    SheetId = sheetId, SheetNumber = sheetNumber, SheetName = sheetName,
                    IssueCode = "UNNAMED", Severity = "Warning",
                    Description = "Sheet name is empty or 'Unnamed'.",
                    ViewportCount = vpCount
                });
            }

            // Grouping
            var prefix = groupRegex != null
                ? ExtractGroupByRegex(sheetNumber, groupRegex)
                : DetectPrefix(sheetNumber);

            if (!groupMap.TryGetValue(prefix, out var group))
            {
                group = new List<(ViewSheet, int)>();
                groupMap[prefix] = group;
            }
            group.Add((sheet, vpCount));
        }

        // Build group summaries
        foreach (var kvp in groupMap.OrderByDescending(g => g.Value.Count))
        {
            var grp = kvp.Value;
            result.Groups.Add(new SheetGroupSummaryItem
            {
                GroupPrefix = kvp.Key,
                SheetCount = grp.Count,
                TotalViewports = grp.Sum(g => g.vpCount),
                EmptySheetCount = grp.Count(g => g.vpCount == 0),
                SampleSheetNumbers = grp.Take(5).Select(g => g.sheet.SheetNumber ?? string.Empty).ToList()
            });
        }

        // Summary
        result.TotalSheets = sheets.Count;
        result.MaxViewportsOnSingleSheet = maxVP;
        result.AverageViewportsPerSheet = sheets.Count > 0 ? Math.Round((double)totalVP / sheets.Count, 1) : 0;
        result.OverallGrade = GradeSheetOrganization(result);

        // ReviewReport
        foreach (var issue in result.Issues)
        {
            var severity = issue.Severity == "Error" ? DiagnosticSeverity.Error
                : issue.Severity == "Warning" ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Info;
            review.Issues.Add(new ReviewIssue
            {
                Code = issue.IssueCode,
                Severity = severity,
                Message = $"[{issue.SheetNumber}] {issue.Description}",
                ElementId = issue.SheetId
            });
        }
        review.IssueCount = review.Issues.Count;
        result.Review = review;
        return result;
    }

    // ══════════════════════════════════════════════════════════
    // 3. audit.template_sheet_map
    // ══════════════════════════════════════════════════════════

    internal TemplateSheetMapResponse BuildTemplateSheetMap(PlatformServices services, Document doc, TemplateSheetMapRequest request)
    {
        request ??= new TemplateSheetMapRequest();
        var result = new TemplateSheetMapResponse { DocumentKey = services.GetDocumentKey(doc) };
        var review = new ReviewReport { Name = "audit.template_sheet_map", DocumentKey = result.DocumentKey };

        var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var templates = allViews.Where(v => v.IsTemplate).ToList();
        var nonTemplates = allViews.Where(v => !v.IsTemplate).ToList();

        if (!string.IsNullOrWhiteSpace(request.TemplateNameContains))
            templates = templates.Where(v => (v.Name ?? string.Empty).IndexOf(request.TemplateNameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        // Build viewport→sheet map: viewId → sheetNumber
        var viewToSheetMap = new Dictionary<long, string>();
        var allViewports = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().ToList();
        foreach (var vp in allViewports)
        {
            var viewIdVal = vp.ViewId.Value;
            if (!viewToSheetMap.ContainsKey(viewIdVal))
            {
                var parentSheet = doc.GetElement(vp.SheetId) as ViewSheet;
                viewToSheetMap[viewIdVal] = parentSheet?.SheetNumber ?? string.Empty;
            }
        }

        // Build template→views map
        var templateViewMap = new Dictionary<long, List<View>>();
        foreach (var v in nonTemplates)
        {
            var tmplId = v.ViewTemplateId;
            if (tmplId != null && tmplId != ElementId.InvalidElementId)
            {
                var key = tmplId.Value;
                if (!templateViewMap.TryGetValue(key, out var list))
                {
                    list = new List<View>();
                    templateViewMap[key] = list;
                }
                list.Add(v);
            }
        }

        // Count views on sheets without template
        var viewsOnSheetsNoTemplate = 0;
        foreach (var kvp in viewToSheetMap)
        {
            var viewEl = doc.GetElement(new ElementId(kvp.Key)) as View;
            if (viewEl != null && !viewEl.IsTemplate)
            {
                var tmplId = viewEl.ViewTemplateId;
                if (tmplId == null || tmplId == ElementId.InvalidElementId)
                    viewsOnSheetsNoTemplate++;
            }
        }
        result.ViewsWithNoTemplate = viewsOnSheetsNoTemplate;

        // Analyze each template chain
        foreach (var tmpl in templates.Take(Math.Max(1, request.MaxResults)))
        {
            var tmplIdVal = tmpl.Id.Value;
            var views = templateViewMap.TryGetValue(tmplIdVal, out var vl) ? vl : new List<View>();
            var viewsOnSheet = 0;
            var viewsNotOnSheet = 0;
            var sheetNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in views)
            {
                if (viewToSheetMap.TryGetValue(v.Id.Value, out var sn) && !string.IsNullOrEmpty(sn))
                {
                    viewsOnSheet++;
                    sheetNumbers.Add(sn);
                }
                else
                {
                    viewsNotOnSheet++;
                }
            }

            string chainStatus;
            if (views.Count == 0)
            {
                chainStatus = "Orphan";
                result.OrphanTemplates++;
            }
            else if (viewsNotOnSheet == 0)
            {
                chainStatus = "Complete";
                result.TemplatesWithCompleteChain++;
            }
            else
            {
                chainStatus = "Partial";
                result.TemplatesWithBrokenChain++;
            }

            // Filter if OnlyBrokenChains
            if (request.OnlyBrokenChains && chainStatus == "Complete")
                continue;

            result.Chains.Add(new TemplateChainItem
            {
                TemplateId = checked((int)tmplIdVal),
                TemplateName = tmpl.Name ?? string.Empty,
                ViewType = tmpl.ViewType.ToString(),
                ViewCount = views.Count,
                ViewsOnSheet = viewsOnSheet,
                ViewsNotOnSheet = viewsNotOnSheet,
                ChainStatus = chainStatus,
                SheetNumbers = sheetNumbers.OrderBy(s => s).ToList()
            });
        }

        result.TotalTemplatesAnalyzed = templates.Count;

        // ReviewReport
        if (result.OrphanTemplates > 0)
            review.Issues.Add(new ReviewIssue { Code = "ORPHAN_TEMPLATES", Severity = DiagnosticSeverity.Info, Message = $"{result.OrphanTemplates} templates are not used by any view." });
        if (result.TemplatesWithBrokenChain > 0)
            review.Issues.Add(new ReviewIssue { Code = "BROKEN_CHAINS", Severity = DiagnosticSeverity.Warning, Message = $"{result.TemplatesWithBrokenChain} templates have views not placed on any sheet." });
        if (viewsOnSheetsNoTemplate > 0)
            review.Issues.Add(new ReviewIssue { Code = "VIEWS_NO_TEMPLATE", Severity = DiagnosticSeverity.Warning, Message = $"{viewsOnSheetsNoTemplate} views on sheets have no template assigned." });
        review.IssueCount = review.Issues.Count;
        result.Review = review;
        return result;
    }

    // ══════════════════════════════════════════════════════════
    // 4. view.template_inspect
    // ══════════════════════════════════════════════════════════

    internal TemplateInspectResponse InspectTemplate(PlatformServices services, Document doc, TemplateInspectRequest request)
    {
        request ??= new TemplateInspectRequest();
        var result = new TemplateInspectResponse { DocumentKey = services.GetDocumentKey(doc) };

        // Resolve template
        View? tmpl = null;
        if (request.TemplateId.HasValue && request.TemplateId.Value > 0)
            tmpl = doc.GetElement(new ElementId((long)request.TemplateId.Value)) as View;
        if (tmpl == null && !string.IsNullOrWhiteSpace(request.TemplateName))
        {
            tmpl = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, request.TemplateName, StringComparison.OrdinalIgnoreCase));
        }
        if (tmpl == null || !tmpl.IsTemplate)
            throw new InvalidOperationException($"Template not found: Id={request.TemplateId}, Name='{request.TemplateName}'");

        // Identity
        result.TemplateId = checked((int)tmpl.Id.Value);
        result.TemplateName = tmpl.Name ?? string.Empty;
        result.ViewType = tmpl.ViewType.ToString();

        try
        {
            var disc = tmpl.Discipline;
            result.Discipline = disc.ToString();
        }
        catch { }

        // Detail level & scale
        try { result.DetailLevel = tmpl.DetailLevel.ToString(); } catch { }
        try { result.Scale = tmpl.Scale; } catch { }

        // Controlled parameters
        if (request.IncludeControlledParameters)
        {
            try
            {
                var paramIds = tmpl.GetTemplateParameterIds();
                result.ControlledParameterCount = paramIds.Count;
                foreach (var paramId in paramIds)
                {
                    // Try to get parameter name from the template itself
                    try
                    {
                        var param = tmpl.get_Parameter((BuiltInParameter)(int)paramId.Value);
                        if (param != null)
                        {
                            result.ControlledParameterNames.Add(param.Definition?.Name ?? $"ParamId:{paramId.Value}");
                        }
                        else
                        {
                            result.ControlledParameterNames.Add($"ParamId:{paramId.Value}");
                        }
                    }
                    catch
                    {
                        result.ControlledParameterNames.Add($"ParamId:{paramId.Value}");
                    }
                }
            }
            catch { }
        }

        // Filters
        if (request.IncludeFilterDetails)
        {
            try
            {
                var filterIds = tmpl.GetFilters();
                result.FilterCount = filterIds.Count;
                foreach (var filterId in filterIds)
                {
                    var filterEl = doc.GetElement(filterId);
                    var detail = new TemplateFilterDetail
                    {
                        FilterId = checked((int)filterId.Value),
                        FilterName = filterEl?.Name ?? string.Empty
                    };

                    try { detail.IsVisible = tmpl.GetFilterVisibility(filterId); } catch { detail.IsVisible = true; }
                    detail.IsEnabled = true; // If filter is in the list, it's applied

                    try
                    {
                        var overrides = tmpl.GetFilterOverrides(filterId);
                        detail.OverrideSummary = BuildOverrideSummary(overrides);
                    }
                    catch { detail.OverrideSummary = string.Empty; }

                    result.Filters.Add(detail);
                }
            }
            catch { }
        }

        // Usage — views using this template + sheet placement
        var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v.ViewTemplateId == tmpl.Id)
            .Take(Math.Max(1, request.MaxViewSamples))
            .ToList();

        // Build quick view→sheet map
        var viewToSheet = new Dictionary<long, string>();
        foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
        {
            if (!viewToSheet.ContainsKey(vp.ViewId.Value))
            {
                var parentSheet = doc.GetElement(vp.SheetId) as ViewSheet;
                viewToSheet[vp.ViewId.Value] = parentSheet?.SheetNumber ?? string.Empty;
            }
        }

        result.UsageCount = allViews.Count;
        foreach (var v in allViews)
        {
            result.ViewUsages.Add(new TemplateViewUsageItem
            {
                ViewId = checked((int)v.Id.Value),
                ViewName = v.Name ?? string.Empty,
                PlacedOnSheet = viewToSheet.TryGetValue(v.Id.Value, out var sn) ? sn : string.Empty
            });
        }

        // Explanations
        result.Explanations.Add($"Template '{result.TemplateName}' is a {result.ViewType} template.");
        if (!string.IsNullOrEmpty(result.Discipline))
            result.Explanations.Add($"Discipline: {result.Discipline}.");
        result.Explanations.Add($"Controls {result.ControlledParameterCount} view parameters and has {result.FilterCount} view filters.");
        result.Explanations.Add($"Used by {result.UsageCount} view(s).");
        var onSheet = result.ViewUsages.Count(vu => !string.IsNullOrEmpty(vu.PlacedOnSheet));
        if (onSheet < result.UsageCount)
            result.Explanations.Add($"{result.UsageCount - onSheet} view(s) using this template are NOT placed on any sheet.");

        return result;
    }

    // ══════════════════════════════════════════════════════════
    // 5. sheet.group_summary
    // ══════════════════════════════════════════════════════════

    internal SheetGroupDetailResponse GetSheetGroupDetail(PlatformServices services, Document doc, SheetGroupDetailRequest request)
    {
        request ??= new SheetGroupDetailRequest();
        var result = new SheetGroupDetailResponse { DocumentKey = services.GetDocumentKey(doc) };
        var review = new ReviewReport { Name = "sheet.group_summary", DocumentKey = result.DocumentKey };

        var allSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => !s.IsPlaceholder).ToList();

        // Filter by prefix or pattern
        Regex? pattern = !string.IsNullOrWhiteSpace(request.SheetNumberPattern)
            ? TryCreateRegex(request.SheetNumberPattern)
            : null;

        var matchedSheets = allSheets.Where(s =>
        {
            var num = s.SheetNumber ?? string.Empty;
            if (pattern != null) return pattern.IsMatch(num);
            if (!string.IsNullOrWhiteSpace(request.SheetNumberPrefix))
                return num.StartsWith(request.SheetNumberPrefix, StringComparison.OrdinalIgnoreCase);
            return true;
        }).OrderBy(s => s.SheetNumber ?? string.Empty)
        .Take(Math.Max(1, request.MaxResults))
        .ToList();

        result.GroupIdentifier = !string.IsNullOrWhiteSpace(request.SheetNumberPrefix)
            ? request.SheetNumberPrefix
            : !string.IsNullOrWhiteSpace(request.SheetNumberPattern)
                ? request.SheetNumberPattern
                : "(all)";

        // Build viewport → view → template data
        var viewToTemplate = new Dictionary<long, string>();
        var viewToSheet = new Dictionary<long, long>();
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate))
        {
            var tmplId = v.ViewTemplateId;
            if (tmplId != null && tmplId != ElementId.InvalidElementId)
            {
                var tmplEl = doc.GetElement(tmplId) as View;
                if (tmplEl != null) viewToTemplate[v.Id.Value] = tmplEl.Name ?? string.Empty;
            }
        }
        foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
        {
            viewToSheet[vp.ViewId.Value] = vp.SheetId.Value;
        }

        var templateUsageMap = new Dictionary<string, (int viewCount, HashSet<long> sheetIds)>(StringComparer.OrdinalIgnoreCase);
        var totalVP = 0;
        var viewsWithoutTemplate = 0;
        var matchedSheetIds = new HashSet<long>(matchedSheets.Select(s => s.Id.Value));

        foreach (var sheet in matchedSheets)
        {
            var sheetId = checked((int)sheet.Id.Value);
            var vpIds = sheet.GetAllViewports();
            var vpCount = vpIds.Count;
            totalVP += vpCount;

            // Title block
            string titleBlockName = string.Empty;
            try
            {
                var tbs = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .ToElements();
                titleBlockName = tbs.Count > 0 ? (tbs[0]?.Name ?? string.Empty) : string.Empty;
            }
            catch { }

            var issueFlags = new List<string>();
            if (vpCount == 0) issueFlags.Add("EMPTY");
            if (string.IsNullOrWhiteSpace(titleBlockName)) issueFlags.Add("NO_TITLEBLOCK");
            if (sheet.Name?.Equals("Unnamed", StringComparison.OrdinalIgnoreCase) == true) issueFlags.Add("UNNAMED");

            result.Sheets.Add(new SheetGroupMemberItem
            {
                SheetId = sheetId,
                SheetNumber = sheet.SheetNumber ?? string.Empty,
                SheetName = sheet.Name ?? string.Empty,
                ViewportCount = vpCount,
                TitleBlockName = titleBlockName,
                IssueFlags = issueFlags
            });

            // Track templates used via viewports on this sheet
            foreach (var vpId in vpIds)
            {
                if (doc.GetElement(vpId) is Viewport vp)
                {
                    var viewIdVal = vp.ViewId.Value;
                    if (viewToTemplate.TryGetValue(viewIdVal, out var tmplName))
                    {
                        if (!templateUsageMap.TryGetValue(tmplName, out var usage))
                        {
                            usage = (0, new HashSet<long>());
                            templateUsageMap[tmplName] = usage;
                        }
                        usage.sheetIds.Add(sheet.Id.Value);
                        templateUsageMap[tmplName] = (usage.viewCount + 1, usage.sheetIds);
                    }
                    else
                    {
                        viewsWithoutTemplate++;
                    }
                }
            }
        }

        // Template usages
        if (request.IncludeTemplateUsage)
        {
            foreach (var kvp in templateUsageMap.OrderByDescending(k => k.Value.viewCount))
            {
                result.TemplateUsages.Add(new TemplateUsageInGroup
                {
                    TemplateName = kvp.Key,
                    ViewCount = kvp.Value.viewCount,
                    SheetCount = kvp.Value.sheetIds.Count
                });
            }
        }

        result.SheetCount = matchedSheets.Count;
        result.TotalViewports = totalVP;
        result.UniqueTemplatesUsed = templateUsageMap.Count;
        result.ViewsWithoutTemplate = viewsWithoutTemplate;
        result.AverageViewportsPerSheet = matchedSheets.Count > 0
            ? Math.Round((double)totalVP / matchedSheets.Count, 1) : 0;

        // Review issues
        if (viewsWithoutTemplate > 0)
            review.Issues.Add(new ReviewIssue { Code = "GROUP_VIEWS_NO_TEMPLATE", Severity = DiagnosticSeverity.Warning, Message = $"{viewsWithoutTemplate} views in group have no template assigned." });
        var emptyCount = result.Sheets.Count(s => s.ViewportCount == 0);
        if (emptyCount > 0)
            review.Issues.Add(new ReviewIssue { Code = "GROUP_EMPTY_SHEETS", Severity = DiagnosticSeverity.Warning, Message = $"{emptyCount} empty sheet(s) in group." });
        review.IssueCount = review.Issues.Count;
        result.Review = review;
        return result;
    }

    // ══════════════════════════════════════════════════════════
    // 6. audit.view_template_compliance
    // ══════════════════════════════════════════════════════════

    internal ViewTemplateComplianceResponse AuditViewTemplateCompliance(PlatformServices services, Document doc, ViewTemplateComplianceRequest request)
    {
        request ??= new ViewTemplateComplianceRequest();
        var result = new ViewTemplateComplianceResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        var review = new ReviewReport { Name = "audit.view_template_compliance", DocumentKey = result.DocumentKey };

        var sections = new List<ComplianceSectionResult>();
        var totalScore = 0;
        var totalMaxPoints = 0;
        var totalIssues = 0;
        var criticalIssues = 0;
        var recommendations = new List<(int priority, string text)>();

        // Section 1: Template Health (30 pts)
        if (request.IncludeTemplateHealth)
        {
            var healthReq = new TemplateHealthRequest
            {
                DocumentKey = request.DocumentKey,
                NamingPattern = request.NamingPattern,
                DuplicateSimilarityThreshold = 0.85,
                MaxResults = 500
            };
            var health = AuditTemplateHealth(services, doc, healthReq);

            int pts;
            string status;
            if (health.UnusedPercentage < 20) { pts = 30; status = "Pass"; }
            else if (health.UnusedPercentage < 40) { pts = 20; status = "Warning"; }
            else if (health.UnusedPercentage < 60) { pts = 10; status = "Warning"; }
            else { pts = 0; status = "Fail"; criticalIssues++; }

            sections.Add(new ComplianceSectionResult
            {
                Section = "TemplateHealth", Score = pts, MaxPoints = 30, Status = status,
                IssueCount = health.Review.IssueCount,
                Summary = $"{health.UnusedPercentage:F0}% unused ({health.UnusedTemplates}/{health.TotalTemplates}), {health.SuspectDuplicateGroups} duplicate group(s), Grade {health.OverallGrade}"
            });
            totalScore += pts;
            totalMaxPoints += 30;
            totalIssues += health.Review.IssueCount;

            if (health.UnusedPercentage >= 50)
                recommendations.Add((100, $"Purge {health.UnusedTemplates} unused templates ({health.UnusedPercentage:F0}% bloat)."));
            if (health.SuspectDuplicateGroups > 0)
                recommendations.Add((80, $"Merge {health.SuspectDuplicateGroups} groups of suspected duplicate templates."));
            if (health.NamingViolationCount > 0)
                recommendations.Add((60, $"Fix {health.NamingViolationCount} template naming violations."));
        }

        // Section 2: Sheet Organization (30 pts)
        if (request.IncludeSheetOrganization)
        {
            var sheetReq = new SheetOrganizationRequest { DocumentKey = request.DocumentKey, MaxResults = 500 };
            var org = AuditSheetOrganization(services, doc, sheetReq);

            double emptyPct = org.TotalSheets > 0 ? 100.0 * org.EmptySheetCount / org.TotalSheets : 0;
            double heavyPct = org.TotalSheets > 0 ? 100.0 * org.HeavySheetCount / org.TotalSheets : 0;

            int sheetPts = 0;
            // Empty sheet scoring (15 pts max)
            if (emptyPct < 5) sheetPts += 15;
            else if (emptyPct < 10) sheetPts += 10;
            else sheetPts += 5;
            // Heavy sheet scoring (15 pts max)
            if (heavyPct < 3) sheetPts += 15;
            else if (heavyPct < 10) sheetPts += 10;
            else sheetPts += 5;

            var sheetStatus = sheetPts >= 25 ? "Pass" : sheetPts >= 15 ? "Warning" : "Fail";
            if (sheetStatus == "Fail") criticalIssues++;

            sections.Add(new ComplianceSectionResult
            {
                Section = "SheetOrganization", Score = sheetPts, MaxPoints = 30, Status = sheetStatus,
                IssueCount = org.Review.IssueCount,
                Summary = $"{org.TotalSheets} sheets, {org.EmptySheetCount} empty, {org.HeavySheetCount} heavy (>{sheetReq.HeavySheetThreshold} VP), Grade {org.OverallGrade}"
            });
            totalScore += sheetPts;
            totalMaxPoints += 30;
            totalIssues += org.Review.IssueCount;

            if (org.EmptySheetCount > 0)
                recommendations.Add((50, $"Review {org.EmptySheetCount} empty sheet(s) — remove if not needed."));
            if (org.HeavySheetCount > 0)
                recommendations.Add((70, $"Split {org.HeavySheetCount} heavy sheet(s) with >{sheetReq.HeavySheetThreshold} viewports."));
            if (org.MissingTitleBlockCount > 0)
                recommendations.Add((40, $"Add title blocks to {org.MissingTitleBlockCount} sheet(s)."));
        }

        // Section 3: Chain Integrity (20 pts)
        if (request.IncludeChainAnalysis)
        {
            var mapReq = new TemplateSheetMapRequest { DocumentKey = request.DocumentKey, MaxResults = 500 };
            var map = BuildTemplateSheetMap(services, doc, mapReq);

            var totalChains = map.TotalTemplatesAnalyzed;
            double brokenPct = totalChains > 0 ? 100.0 * map.TemplatesWithBrokenChain / totalChains : 0;

            int chainPts;
            string chainStatus;
            if (brokenPct < 5) { chainPts = 20; chainStatus = "Pass"; }
            else if (brokenPct < 15) { chainPts = 15; chainStatus = "Warning"; }
            else { chainPts = 5; chainStatus = "Fail"; criticalIssues++; }

            sections.Add(new ComplianceSectionResult
            {
                Section = "ChainIntegrity", Score = chainPts, MaxPoints = 20, Status = chainStatus,
                IssueCount = map.Review.IssueCount,
                Summary = $"{map.TemplatesWithCompleteChain} complete, {map.TemplatesWithBrokenChain} broken, {map.OrphanTemplates} orphan, {map.ViewsWithNoTemplate} views without template"
            });
            totalScore += chainPts;
            totalMaxPoints += 20;
            totalIssues += map.Review.IssueCount;

            if (map.TemplatesWithBrokenChain > 0)
                recommendations.Add((65, $"Fix {map.TemplatesWithBrokenChain} broken template-to-sheet chains."));
            if (map.ViewsWithNoTemplate > 0)
                recommendations.Add((45, $"Assign templates to {map.ViewsWithNoTemplate} views on sheets."));
        }

        // Section 4: Naming Convention (20 pts) — uses template_health naming results
        if (request.IncludeTemplateHealth)
        {
            var namingReq = new TemplateHealthRequest
            {
                DocumentKey = request.DocumentKey,
                NamingPattern = request.NamingPattern,
                MaxResults = 500
            };
            var naming = AuditTemplateHealth(services, doc, namingReq);

            double violPct = naming.TotalTemplates > 0 ? 100.0 * naming.NamingViolationCount / naming.TotalTemplates : 0;
            int namingPts;
            string namingStatus;
            if (violPct < 5) { namingPts = 20; namingStatus = "Pass"; }
            else if (violPct < 15) { namingPts = 15; namingStatus = "Warning"; }
            else { namingPts = 5; namingStatus = "Fail"; }

            sections.Add(new ComplianceSectionResult
            {
                Section = "NamingConvention", Score = namingPts, MaxPoints = 20, Status = namingStatus,
                IssueCount = naming.NamingViolationCount,
                Summary = $"{naming.NamingViolationCount} violations out of {naming.TotalTemplates} templates ({violPct:F0}%)"
            });
            totalScore += namingPts;
            totalMaxPoints += 20;
            totalIssues += naming.NamingViolationCount;
        }

        // Overall
        result.OverallScore = totalMaxPoints > 0 ? (int)Math.Round(100.0 * totalScore / totalMaxPoints) : 0;
        result.OverallGrade = result.OverallScore >= 90 ? "A"
            : result.OverallScore >= 75 ? "B"
            : result.OverallScore >= 60 ? "C"
            : result.OverallScore >= 40 ? "D" : "F";
        result.TotalIssues = totalIssues;
        result.CriticalIssues = criticalIssues;
        result.Sections = sections;
        result.TopRecommendations = recommendations
            .OrderByDescending(r => r.priority)
            .Take(5)
            .Select(r => r.text)
            .ToList();

        // ReviewReport
        foreach (var s in sections)
        {
            var severity = s.Status == "Fail" ? DiagnosticSeverity.Error
                : s.Status == "Warning" ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Info;
            review.Issues.Add(new ReviewIssue { Code = $"COMPLIANCE_{s.Section.ToUpperInvariant()}", Severity = severity, Message = $"[{s.Score}/{s.MaxPoints}] {s.Summary}" });
        }
        review.IssueCount = review.Issues.Count;
        result.Review = review;
        return result;
    }

    // ══════════════════════════════════════════════════════════
    // Private Helpers
    // ══════════════════════════════════════════════════════════

    private static string GradeTemplateHealth(double unusedPct, int duplicateGroups, int namingViolations)
    {
        if (unusedPct < 20 && duplicateGroups == 0 && namingViolations == 0) return "A";
        if (unusedPct < 40 && duplicateGroups < 3) return "B";
        if (unusedPct < 60) return "C";
        if (unusedPct < 80) return "D";
        return "F";
    }

    private static string GradeSheetOrganization(SheetOrganizationResponse r)
    {
        if (r.TotalSheets == 0) return "N/A";
        double emptyPct = 100.0 * r.EmptySheetCount / r.TotalSheets;
        double heavyPct = 100.0 * r.HeavySheetCount / r.TotalSheets;
        if (emptyPct < 5 && heavyPct < 3 && r.MissingTitleBlockCount == 0) return "A";
        if (emptyPct < 10 && heavyPct < 5) return "B";
        if (emptyPct < 15) return "C";
        if (emptyPct < 25) return "D";
        return "F";
    }

    private static void CheckNamingViolation(string name, int templateId, Regex? pattern, List<TemplateNamingViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
        {
            violations.Add(new TemplateNamingViolation
            {
                TemplateId = templateId, TemplateName = name,
                Violation = "too_short", SuggestedName = string.Empty
            });
            return;
        }

        if (Regex.IsMatch(name, @"Copy\s*\d*$", RegexOptions.IgnoreCase))
        {
            var suggested = Regex.Replace(name, @"\s*Copy\s*\d*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            violations.Add(new TemplateNamingViolation
            {
                TemplateId = templateId, TemplateName = name,
                Violation = "copy_suffix", SuggestedName = suggested
            });
        }

        if (name.IndexOfAny(new[] { '{', '}', '[', ']', '|', '\\' }) >= 0)
        {
            violations.Add(new TemplateNamingViolation
            {
                TemplateId = templateId, TemplateName = name,
                Violation = "invalid_characters", SuggestedName = string.Empty
            });
        }

        if (pattern != null && !pattern.IsMatch(name))
        {
            violations.Add(new TemplateNamingViolation
            {
                TemplateId = templateId, TemplateName = name,
                Violation = "pattern_mismatch", SuggestedName = string.Empty
            });
        }
    }

    private static void DetectDuplicates(List<View> templates, Dictionary<long, int> usageMap, double threshold, List<TemplateDuplicateGroup> groups)
    {
        var processed = new HashSet<int>();
        for (int i = 0; i < templates.Count; i++)
        {
            if (processed.Contains(i)) continue;
            var nameI = templates[i].Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nameI)) continue;

            var candidates = new List<(int index, double score)>();
            for (int j = i + 1; j < templates.Count; j++)
            {
                if (processed.Contains(j)) continue;
                var nameJ = templates[j].Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(nameJ)) continue;

                // Same view type required for duplicate consideration
                if (templates[i].ViewType != templates[j].ViewType) continue;

                var similarity = ComputeStringSimilarity(nameI, nameJ);
                if (similarity >= threshold)
                    candidates.Add((j, similarity));
            }

            if (candidates.Count > 0)
            {
                var group = new TemplateDuplicateGroup
                {
                    SimilarityScore = Math.Round(candidates.Max(c => c.score), 2)
                };

                group.Templates.Add(new TemplateDuplicateCandidate
                {
                    TemplateId = checked((int)templates[i].Id.Value),
                    TemplateName = nameI,
                    UsageCount = usageMap.TryGetValue(templates[i].Id.Value, out var u1) ? u1 : 0
                });

                foreach (var (idx, _) in candidates.OrderByDescending(c => c.score).Take(5))
                {
                    processed.Add(idx);
                    group.Templates.Add(new TemplateDuplicateCandidate
                    {
                        TemplateId = checked((int)templates[idx].Id.Value),
                        TemplateName = templates[idx].Name ?? string.Empty,
                        UsageCount = usageMap.TryGetValue(templates[idx].Id.Value, out var u2) ? u2 : 0
                    });
                }

                var names = group.Templates.Select(t => t.TemplateName).ToList();
                group.DifferenceDescription = $"Similar names ({group.SimilarityScore:P0}): {string.Join(" ↔ ", names.Take(3))}";

                groups.Add(group);
                processed.Add(i);
            }
        }
    }

    /// <summary>Normalized Levenshtein similarity: 1.0 = identical, 0.0 = completely different.</summary>
    private static double ComputeStringSimilarity(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 1.0;
        var la = a.ToLowerInvariant();
        var lb = b.ToLowerInvariant();
        var maxLen = Math.Max(la.Length, lb.Length);
        if (maxLen == 0) return 1.0;
        var dist = LevenshteinDistance(la, lb);
        return 1.0 - (double)dist / maxLen;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    /// <summary>Auto-detect sheet number prefix from naming convention (split by - and _).</summary>
    private static string DetectPrefix(string sheetNumber)
    {
        if (string.IsNullOrWhiteSpace(sheetNumber)) return "(empty)";
        // Split by separators, take all segments except the last numeric one
        var parts = sheetNumber.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return sheetNumber;

        // Walk backwards to find where numeric-only segments start
        var prefixParts = new List<string>();
        var foundNonNumeric = false;
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (!foundNonNumeric && IsNumericSegment(parts[i]))
                continue; // skip trailing numbers
            foundNonNumeric = true;
            prefixParts.Insert(0, parts[i]);
        }

        return prefixParts.Count > 0 ? string.Join("-", prefixParts) : sheetNumber;
    }

    private static bool IsNumericSegment(string s)
    {
        return !string.IsNullOrEmpty(s) && s.All(c => char.IsDigit(c) || c == '.');
    }

    private static string ExtractGroupByRegex(string sheetNumber, Regex regex)
    {
        var match = regex.Match(sheetNumber);
        if (match.Success && match.Groups.Count > 1)
            return match.Groups[1].Value;
        return match.Success ? match.Value : DetectPrefix(sheetNumber);
    }

    private static string BuildOverrideSummary(OverrideGraphicSettings overrides)
    {
        var parts = new List<string>();

        var projColor = overrides.ProjectionLineColor;
        if (projColor.IsValid && !(projColor.Red == 0 && projColor.Green == 0 && projColor.Blue == 0))
            parts.Add($"Projection: RGB({projColor.Red},{projColor.Green},{projColor.Blue})");

        var cutColor = overrides.CutLineColor;
        if (cutColor.IsValid && !(cutColor.Red == 0 && cutColor.Green == 0 && cutColor.Blue == 0))
            parts.Add($"Cut: RGB({cutColor.Red},{cutColor.Green},{cutColor.Blue})");

        var transparency = overrides.Transparency;
        if (transparency > 0)
            parts.Add($"Transparency: {transparency}%");

        if (overrides.Halftone)
            parts.Add("Halftone");

        return parts.Count > 0 ? string.Join(", ", parts) : "(no overrides)";
    }

    private static Regex? TryCreateRegex(string pattern)
    {
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
        catch { return null; }
    }
}
