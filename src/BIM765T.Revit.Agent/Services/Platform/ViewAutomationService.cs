using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class ViewAutomationService
{
    internal ExecutionResult PreviewCreate3DView(UIApplication uiapp, PlatformServices services, Document doc, Create3DViewRequest request, ToolRequestEnvelope envelope)
    {
        var plan = Build3DViewPlan(uiapp, doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = plan.ExistingView != null && !plan.CreateNew ? new List<int> { checked((int)plan.ExistingView.Id.Value) } : new List<int>(),
            Diagnostics = new List<DiagnosticRecord>(plan.Diagnostics),
            Artifacts = new List<string>
            {
                "viewName=" + plan.TargetViewName,
                "mode=" + (plan.CreateNew ? "create" : "update"),
                "copyOrientation=" + plan.CopyOrientation.ToString(),
                "copySectionBox=" + plan.CopySectionBox.ToString()
            }
        };
    }

    internal ExecutionResult ExecuteCreate3DView(UIApplication uiapp, PlatformServices services, Document doc, Create3DViewRequest request)
    {
        var plan = Build3DViewPlan(uiapp, doc, request);
        if (plan.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(plan.Diagnostics.First(x => x.Severity == DiagnosticSeverity.Error).Message);
        }

        var diagnostics = new List<DiagnosticRecord>(plan.Diagnostics);
        var beforeWarnings = doc.GetWarnings().Count;
        View3D? view = null;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::view.create_3d_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Create 3D view safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        if (plan.CreateNew)
        {
            view = View3D.CreateIsometric(doc, plan.ViewFamilyType.Id);
            view.Name = plan.TargetViewName;
        }
        else
        {
            view = plan.ExistingView!;
        }

        if (plan.TemplateView != null)
        {
            view.ViewTemplateId = plan.TemplateView.Id;
        }

        if (plan.CopyOrientation && plan.Source3DView != null)
        {
            view.SetOrientation(plan.Source3DView.GetOrientation());
        }

        if (plan.CopySectionBox && plan.Source3DView != null)
        {
            try
            {
                view.IsSectionBoxActive = plan.Source3DView.IsSectionBoxActive;
                view.SetSectionBox(plan.Source3DView.GetSectionBox());
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("SECTIONBOX_COPY_FAILED", DiagnosticSeverity.Warning, ex.Message));
            }
        }

        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };
        if (plan.CreateNew)
        {
            diff.CreatedIds.Add(checked((int)view.Id.Value));
        }
        else
        {
            diff.ModifiedIds.Add(checked((int)view.Id.Value));
        }

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        if (request.ActivateViewAfterCreate && view != null && uiapp.ActiveUIDocument?.Document?.Equals(doc) == true)
        {
            try
            {
                uiapp.ActiveUIDocument.ActiveView = view;
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("ACTIVATE_VIEW_FAILED", DiagnosticSeverity.Warning, ex.Message, checked((int)view.Id.Value)));
            }
        }

        var review = services.BuildExecutionReview("view_create_3d_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = record.Code,
                Severity = record.Severity,
                Message = record.Message,
                ElementId = record.SourceId
            });
        }
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "view.create_3d_safe",
            DryRun = false,
            ChangedIds = diff.CreatedIds.Concat(diff.ModifiedIds).Distinct().ToList(),
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "viewName=" + (view?.Name ?? plan.TargetViewName),
                view != null ? "viewId=" + view.Id.Value.ToString(CultureInfo.InvariantCulture) : "viewId=<none>"
            },
            ReviewSummary = review
        };
    }

    internal ListViewFiltersResponse ListViewFilters(Document doc, ListViewFiltersRequest request)
    {
        request ??= new ListViewFiltersRequest();
        var nameFilter = (request.NameContains ?? string.Empty).Trim();
        var max = request.MaxResults > 0 ? request.MaxResults : 500;

        var allFilters = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .Where(f => string.IsNullOrEmpty(nameFilter)
                || f.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();

        var summaries = new List<ViewFilterSummary>(allFilters.Count);
        foreach (var f in allFilters)
        {
            var summary = new ViewFilterSummary
            {
                FilterId = checked((int)f.Id.Value),
                FilterName = f.Name,
                CategoryCount = f.GetCategories().Count,
                RuleCount = 0,
                CategoryNames = new List<string>(),
                RuleSummary = string.Empty
            };

            if (request.IncludeCategoryNames)
            {
                foreach (var catId in f.GetCategories())
                {
                    var cat = Category.GetCategory(doc, catId);
                    if (cat != null)
                    {
                        summary.CategoryNames.Add(cat.Name);
                    }
                }
            }

            if (request.IncludeRuleSummary)
            {
                try
                {
                    var elementFilter = f.GetElementFilter();
                    if (elementFilter is ElementParameterFilter singleParamFilter)
                    {
                        var rules = singleParamFilter.GetRules();
                        summary.RuleCount = rules.Count;
                        summary.RuleSummary = BuildRuleSummary(doc, rules);
                    }
                    else if (elementFilter is LogicalAndFilter andFilter)
                    {
                        var allRules = new List<FilterRule>();
                        foreach (var inner in andFilter.GetFilters())
                        {
                            if (inner is ElementParameterFilter pf)
                            {
                                allRules.AddRange(pf.GetRules());
                            }
                        }
                        summary.RuleCount = allRules.Count;
                        summary.RuleSummary = BuildRuleSummary(doc, allRules);
                    }
                    else
                    {
                        summary.RuleSummary = elementFilter?.GetType().Name ?? "unknown";
                    }
                }
                catch (Exception ex)
                {
                    summary.RuleSummary = "error: " + ex.Message;
                }
            }

            summaries.Add(summary);
        }

        return new ListViewFiltersResponse
        {
            Filters = summaries,
            TotalCount = summaries.Count
        };
    }

    // ── Inspect ──────────────────────────────────────────────────────────────

    internal FilterInspectResult InspectFilter(Document doc, InspectFilterRequest request)
    {
        request ??= new InspectFilterRequest();

        // Dùng FilteredElementCollector (nhất quán với ListViewFilters) thay vì GetElement
        // Materialize thành List trước — FilteredElementCollector NET 4.8 không reusable sau enumerate
        var allFilters = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .ToList();

        ParameterFilterElement? filter = null;
        if (request.FilterId.HasValue)
        {
            filter = allFilters.FirstOrDefault(x => x.Id.Value == (long)request.FilterId.Value);
        }

        if (filter == null && !string.IsNullOrWhiteSpace(request.FilterName))
        {
            filter = allFilters.FirstOrDefault(x => string.Equals(x.Name, request.FilterName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (filter == null)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy filter '{request.FilterName ?? request.FilterId?.ToString(CultureInfo.InvariantCulture)}' trong document.");
        }

        var filterId = filter.Id;

        var result = new FilterInspectResult
        {
            FilterId = checked((int)filterId.Value),
            FilterName = filter.Name,
            CategoryCount = filter.GetCategories().Count
        };

        foreach (var catId in filter.GetCategories())
        {
            var cat = Category.GetCategory(doc, catId);
            if (cat != null)
            {
                result.CategoryNames.Add(cat.Name);
            }
        }

        result.Rules = BuildFilterRuleDetails(doc, filter);

        if (request.IncludeViewUsage)
        {
            result.ViewUsages = ScanFilterUsage(doc, filterId, templatesOnly: false);
            result.TotalViewUsageCount = result.ViewUsages.Count;
        }

        if (request.IncludeTemplateUsage)
        {
            result.TemplateUsages = ScanFilterUsage(doc, filterId, templatesOnly: true);
            result.TotalTemplateUsageCount = result.TemplateUsages.Count;
        }

        return result;
    }

    // ── Remove filter from view ───────────────────────────────────────────────

    internal ExecutionResult PreviewRemoveFilterFromView(UIApplication uiapp, PlatformServices services, Document doc, RemoveFilterFromViewRequest request, ToolRequestEnvelope envelope)
    {
        var plan = BuildRemoveFilterFromViewPlan(uiapp, services, doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = new List<int> { checked((int)plan.View.Id.Value) },
            Diagnostics = new List<DiagnosticRecord>(plan.Diagnostics),
            Artifacts = new List<string>
            {
                "viewName=" + plan.View.Name,
                "filterName=" + plan.Filter.Name,
                "filterCurrentlyApplied=" + plan.FilterApplied.ToString()
            }
        };
    }

    internal ExecutionResult ExecuteRemoveFilterFromView(UIApplication uiapp, PlatformServices services, Document doc, RemoveFilterFromViewRequest request)
    {
        var plan = BuildRemoveFilterFromViewPlan(uiapp, services, doc, request);
        if (plan.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(plan.Diagnostics.First(x => x.Severity == DiagnosticSeverity.Error).Message);
        }

        var diagnostics = new List<DiagnosticRecord>(plan.Diagnostics);
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::view.remove_filter_from_view_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Remove filter from view safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        if (plan.FilterApplied)
        {
            plan.View.RemoveFilter(plan.Filter.Id);
        }

        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            ModifiedIds = new List<int> { checked((int)plan.View.Id.Value) },
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("view_remove_filter_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue { Code = record.Code, Severity = record.Severity, Message = record.Message, ElementId = record.SourceId });
        }
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "view.remove_filter_from_view_safe",
            DryRun = false,
            ChangedIds = diff.ModifiedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string> { "viewName=" + plan.View.Name, "filterName=" + plan.Filter.Name },
            ReviewSummary = review
        };
    }

    // ── Delete filter element ─────────────────────────────────────────────────

    internal ExecutionResult PreviewDeleteFilter(UIApplication uiapp, PlatformServices services, Document doc, DeleteFilterRequest request, ToolRequestEnvelope envelope)
    {
        var plan = BuildDeleteFilterPlan(doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = new List<int> { checked((int)plan.Filter.Id.Value) },
            Diagnostics = new List<DiagnosticRecord>(plan.Diagnostics),
            Artifacts = new List<string>
            {
                "filterName=" + plan.Filter.Name,
                "viewUsageCount=" + plan.ViewUsages.Count.ToString(CultureInfo.InvariantCulture),
                "templateUsageCount=" + plan.TemplateUsages.Count.ToString(CultureInfo.InvariantCulture),
                "forceRemoveFromAllViews=" + request.ForceRemoveFromAllViews.ToString()
            }
        };
    }

    internal ExecutionResult ExecuteDeleteFilter(UIApplication uiapp, PlatformServices services, Document doc, DeleteFilterRequest request)
    {
        var plan = BuildDeleteFilterPlan(doc, request);
        if (plan.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(plan.Diagnostics.First(x => x.Severity == DiagnosticSeverity.Error).Message);
        }

        var diagnostics = new List<DiagnosticRecord>(plan.Diagnostics);
        var beforeWarnings = doc.GetWarnings().Count;
        var filterId = plan.Filter.Id;
        var filterName = plan.Filter.Name;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::view.delete_filter_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Delete filter safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        // Remove từ tất cả views/templates trước khi xoá
        foreach (var usage in plan.ViewUsages.Concat(plan.TemplateUsages))
        {
            try
            {
                var view = doc.GetElement(new ElementId((long)usage.ViewId)) as View;
                if (view != null && view.GetFilters().Contains(filterId))
                {
                    view.RemoveFilter(filterId);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("REMOVE_FILTER_FROM_VIEW_FAILED", DiagnosticSeverity.Warning, ex.Message, usage.ViewId));
            }
        }

        doc.Delete(filterId);
        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            DeletedIds = new List<int> { checked((int)filterId.Value) },
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("view_delete_filter_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue { Code = record.Code, Severity = record.Severity, Message = record.Message, ElementId = record.SourceId });
        }
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "view.delete_filter_safe",
            DryRun = false,
            ChangedIds = diff.DeletedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string> { "filterName=" + filterName, "filterId=" + filterId.Value.ToString(CultureInfo.InvariantCulture) },
            ReviewSummary = review
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static List<FilterUsageEntry> ScanFilterUsage(Document doc, ElementId filterId, bool templatesOnly)
    {
        var result = new List<FilterUsageEntry>();
        foreach (var view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            if (view.IsTemplate != templatesOnly)
            {
                continue;
            }

            try
            {
                if (!view.GetFilters().Contains(filterId))
                {
                    continue;
                }
            }
            catch
            {
                continue; // Một số view type không support GetFilters()
            }

            var entry = new FilterUsageEntry
            {
                ViewId = checked((int)view.Id.Value),
                ViewName = view.Name,
                IsTemplate = view.IsTemplate
            };

            try
            {
                entry.IsVisible = view.GetFilterVisibility(filterId);
                var overrides = view.GetFilterOverrides(filterId);
                entry.IsHalftone = overrides.Halftone;
                entry.Transparency = overrides.Transparency;
                var color = overrides.ProjectionLineColor;
                entry.ProjectionLineColor = color.IsValid
                    ? $"{color.Red},{color.Green},{color.Blue}"
                    : "default";
            }
            catch
            {
                // Visibility/override không query được — để default values
            }

            result.Add(entry);
        }

        return result;
    }

    private static List<FilterRuleDetail> BuildFilterRuleDetails(Document doc, ParameterFilterElement filter)
    {
        var details = new List<FilterRuleDetail>();
        try
        {
            var elementFilter = filter.GetElementFilter();
            var rules = CollectRules(elementFilter);
            foreach (var (rule, isNegated) in rules)
            {
                var paramId = rule.GetRuleParameter();
                var paramElement = doc.GetElement(paramId);
                var detail = new FilterRuleDetail
                {
                    ParameterName = paramElement?.Name ?? string.Empty,
                    ParameterIdRaw = paramId.Value.ToString(CultureInfo.InvariantCulture),
                    StorageType = GetRuleStorageType(rule),
                    Operator = (isNegated ? "NOT(" : string.Empty) + GetRuleOperator(rule) + (isNegated ? ")" : string.Empty),
                    Value = GetRuleValue(rule)
                };
                details.Add(detail);
            }
        }
        catch
        {
            // Không parse được rules — trả list rỗng
        }

        return details;
    }

    private static List<(FilterRule Rule, bool IsNegated)> CollectRules(ElementFilter elementFilter)
    {
        var result = new List<(FilterRule, bool)>();
        if (elementFilter is ElementParameterFilter pf)
        {
            foreach (var r in pf.GetRules())
            {
                result.Add(r is FilterInverseRule inv ? (inv.GetInnerRule(), true) : (r, false));
            }
        }
        else if (elementFilter is LogicalAndFilter andFilter)
        {
            foreach (var inner in andFilter.GetFilters())
            {
                if (inner is ElementParameterFilter innerPf)
                {
                    foreach (var r in innerPf.GetRules())
                    {
                        result.Add(r is FilterInverseRule inv ? (inv.GetInnerRule(), true) : (r, false));
                    }
                }
            }
        }

        return result;
    }

    private static string GetRuleOperator(FilterRule rule)
    {
        switch (rule)
        {
            case FilterStringRule sr: return sr.GetEvaluator().GetType().Name.Replace("FilterString", string.Empty).ToLowerInvariant();
            case FilterIntegerRule ir: return ir.GetEvaluator().GetType().Name.Replace("FilterNumeric", string.Empty).ToLowerInvariant();
            case FilterDoubleRule dr: return dr.GetEvaluator().GetType().Name.Replace("FilterNumeric", string.Empty).ToLowerInvariant();
            case FilterElementIdRule _: return "equals";
            case HasValueFilterRule _: return "has_value";
            case HasNoValueFilterRule _: return "has_no_value";
            default: return rule.GetType().Name;
        }
    }

    private static string GetRuleValue(FilterRule rule)
    {
        switch (rule)
        {
            case FilterStringRule sr: return sr.RuleString ?? string.Empty;
            case FilterIntegerRule ir: return ir.RuleValue.ToString(CultureInfo.InvariantCulture);
            case FilterDoubleRule dr: return dr.RuleValue.ToString("G6", CultureInfo.InvariantCulture);
            case FilterElementIdRule er: return er.RuleValue.Value.ToString(CultureInfo.InvariantCulture);
            default: return string.Empty;
        }
    }

    private static string GetRuleStorageType(FilterRule rule)
    {
        switch (rule)
        {
            case FilterStringRule _: return "String";
            case FilterIntegerRule _: return "Integer";
            case FilterDoubleRule _: return "Double";
            case FilterElementIdRule _: return "ElementId";
            default: return "Unknown";
        }
    }

    private static RemoveFilterFromViewPlan BuildRemoveFilterFromViewPlan(UIApplication uiapp, PlatformServices services, Document doc, RemoveFilterFromViewRequest request)
    {
        request ??= new RemoveFilterFromViewRequest();
        var view = services.ResolveView(uiapp, doc, request.ViewName, request.ViewId);
        var filter = ResolveExistingFilter(doc, request.FilterId, request.FilterName);

        bool applied;
        try { applied = view.GetFilters().Contains(filter.Id); }
        catch { applied = false; }

        var plan = new RemoveFilterFromViewPlan { View = view, Filter = filter, FilterApplied = applied };

        if (!applied)
        {
            plan.Diagnostics.Add(DiagnosticRecord.Create("FILTER_NOT_APPLIED", DiagnosticSeverity.Warning,
                $"Filter '{filter.Name}' chưa được apply trên view '{view.Name}' — không cần remove."));
        }

        return plan;
    }

    private static DeleteFilterPlan BuildDeleteFilterPlan(Document doc, DeleteFilterRequest request)
    {
        request ??= new DeleteFilterRequest();
        var filter = ResolveExistingFilter(doc, request.FilterId, request.FilterName);
        var viewUsages = ScanFilterUsage(doc, filter.Id, templatesOnly: false);
        var templateUsages = ScanFilterUsage(doc, filter.Id, templatesOnly: true);

        var plan = new DeleteFilterPlan { Filter = filter, ViewUsages = viewUsages, TemplateUsages = templateUsages };

        var totalUsage = viewUsages.Count + templateUsages.Count;
        if (totalUsage > 0 && !request.ForceRemoveFromAllViews)
        {
            plan.Diagnostics.Add(DiagnosticRecord.Create("FILTER_IN_USE", DiagnosticSeverity.Error,
                $"Filter '{filter.Name}' đang được dùng ở {viewUsages.Count} view(s) và {templateUsages.Count} template(s). " +
                "Set ForceRemoveFromAllViews=true để tự động remove trước khi xoá.",
                checked((int)filter.Id.Value)));
        }
        else if (totalUsage > 0)
        {
            plan.Diagnostics.Add(DiagnosticRecord.Create("FILTER_FORCE_REMOVE", DiagnosticSeverity.Warning,
                $"Sẽ remove filter khỏi {viewUsages.Count} view(s) và {templateUsages.Count} template(s) trước khi xoá."));
        }

        return plan;
    }

    private static string BuildRuleSummary(Document doc, IList<FilterRule> rules)
    {
        if (rules == null || rules.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(rules.Count);
        foreach (var rule in rules)
        {
            try
            {
                parts.Add(DescribeFilterRule(doc, rule));
            }
            catch
            {
                parts.Add("?");
            }
        }

        return string.Join("; ", parts);
    }

    private static string DescribeFilterRule(Document doc, FilterRule rule)
    {
        // Unwrap NOT — FilterInverseRule bọc ngoài rule thật
        if (rule is FilterInverseRule inverseRule)
        {
            var inner = DescribeFilterRule(doc, inverseRule.GetInnerRule());
            return "NOT(" + inner + ")";
        }

        var paramName = doc.GetElement(rule.GetRuleParameter())?.Name
            ?? rule.GetRuleParameter().Value.ToString(CultureInfo.InvariantCulture);

        switch (rule)
        {
            case FilterStringRule stringRule:
                var evaluatorName = stringRule.GetEvaluator().GetType().Name
                    .Replace("FilterString", string.Empty)
                    .ToLowerInvariant();
                return $"{paramName} {evaluatorName} \"{stringRule.RuleString ?? string.Empty}\"";

            case FilterIntegerRule intRule:
                var intOp = intRule.GetEvaluator().GetType().Name
                    .Replace("FilterNumeric", string.Empty)
                    .ToLowerInvariant();
                return $"{paramName} {intOp} {intRule.RuleValue}";

            case FilterDoubleRule doubleRule:
                var dblOp = doubleRule.GetEvaluator().GetType().Name
                    .Replace("FilterNumeric", string.Empty)
                    .ToLowerInvariant();
                return $"{paramName} {dblOp} {doubleRule.RuleValue.ToString("G6", CultureInfo.InvariantCulture)}";

            case FilterElementIdRule idRule:
                return $"{paramName} eq id:{idRule.RuleValue.Value}";

            case HasValueFilterRule _:
                return $"{paramName} has_value";

            case HasNoValueFilterRule _:
                return $"{paramName} has_no_value";

            default:
                return $"{paramName} {rule.GetType().Name}";
        }
    }

    internal ExecutionResult PreviewCreateOrUpdateViewFilter(UIApplication uiapp, PlatformServices services, Document doc, CreateOrUpdateViewFilterRequest request, ToolRequestEnvelope envelope)
    {
        var plan = BuildFilterPlan(uiapp, doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = plan.ExistingFilter != null ? new List<int> { checked((int)plan.ExistingFilter.Id.Value) } : new List<int>(),
            Diagnostics = new List<DiagnosticRecord>(plan.Diagnostics),
            Artifacts = new List<string>
            {
                "filterName=" + plan.FilterName,
                "mode=" + (plan.ExistingFilter == null ? "create" : "update"),
                "categoryCount=" + plan.CategoryIds.Count.ToString(CultureInfo.InvariantCulture),
                "ruleCount=" + plan.Rules.Count.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    internal ExecutionResult ExecuteCreateOrUpdateViewFilter(UIApplication uiapp, PlatformServices services, Document doc, CreateOrUpdateViewFilterRequest request)
    {
        var plan = BuildFilterPlan(uiapp, doc, request);
        if (plan.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(plan.Diagnostics.First(x => x.Severity == DiagnosticSeverity.Error).Message);
        }

        var diagnostics = new List<DiagnosticRecord>(plan.Diagnostics);
        var beforeWarnings = doc.GetWarnings().Count;
        ParameterFilterElement? filter = null;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::view.create_or_update_filter_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Create or update view filter safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        if (plan.ExistingFilter == null)
        {
            filter = ParameterFilterElement.Create(doc, plan.FilterName, plan.CategoryIds);
        }
        else
        {
            filter = plan.ExistingFilter;
            filter.SetCategories(plan.CategoryIds);
        }

        filter.SetElementFilter(plan.ElementFilter);
        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };
        if (plan.ExistingFilter == null)
        {
            diff.CreatedIds.Add(checked((int)filter.Id.Value));
        }
        else
        {
            diff.ModifiedIds.Add(checked((int)filter.Id.Value));
        }

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("view_filter_create_update_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = record.Code,
                Severity = record.Severity,
                Message = record.Message,
                ElementId = record.SourceId
            });
        }
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "view.create_or_update_filter_safe",
            DryRun = false,
            ChangedIds = diff.CreatedIds.Concat(diff.ModifiedIds).Distinct().ToList(),
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "filterName=" + plan.FilterName,
                filter != null ? "filterId=" + filter.Id.Value.ToString(CultureInfo.InvariantCulture) : "filterId=<none>"
            },
            ReviewSummary = review
        };
    }

    internal ExecutionResult PreviewApplyViewFilter(UIApplication uiapp, PlatformServices services, Document doc, ApplyViewFilterRequest request, ToolRequestEnvelope envelope)
    {
        var plan = BuildApplyFilterPlan(uiapp, services, doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = new List<int> { checked((int)plan.View.Id.Value) },
            Diagnostics = new List<DiagnosticRecord>(plan.Diagnostics),
            Artifacts = new List<string>
            {
                "viewName=" + plan.View.Name,
                "filterName=" + plan.Filter.Name,
                "willAdd=" + (!plan.FilterAlreadyApplied).ToString()
            }
        };
    }

    internal ExecutionResult ExecuteApplyViewFilter(UIApplication uiapp, PlatformServices services, Document doc, ApplyViewFilterRequest request)
    {
        var plan = BuildApplyFilterPlan(uiapp, services, doc, request);
        if (plan.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(plan.Diagnostics.First(x => x.Severity == DiagnosticSeverity.Error).Message);
        }

        var diagnostics = new List<DiagnosticRecord>(plan.Diagnostics);
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::view.apply_filter_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Apply view filter safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        if (!plan.FilterAlreadyApplied)
        {
            plan.View.AddFilter(plan.Filter.Id);
        }

        if (request.Visible.HasValue)
        {
            plan.View.SetFilterVisibility(plan.Filter.Id, request.Visible.Value);
        }

        var overrides = plan.View.GetFilterOverrides(plan.Filter.Id);
        if (request.Halftone.HasValue)
        {
            overrides.SetHalftone(request.Halftone.Value);
        }

        if (request.Transparency.HasValue)
        {
            overrides.SetSurfaceTransparency(Math.Max(0, Math.Min(100, request.Transparency.Value)));
        }

        if (request.ProjectionLineColorRed.HasValue && request.ProjectionLineColorGreen.HasValue && request.ProjectionLineColorBlue.HasValue)
        {
            overrides.SetProjectionLineColor(new Color(
                ToByte(request.ProjectionLineColorRed.Value),
                ToByte(request.ProjectionLineColorGreen.Value),
                ToByte(request.ProjectionLineColorBlue.Value)));
        }

        plan.View.SetFilterOverrides(plan.Filter.Id, overrides);
        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            ModifiedIds = new List<int> { checked((int)plan.View.Id.Value) },
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("view_apply_filter_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = record.Code,
                Severity = record.Severity,
                Message = record.Message,
                ElementId = record.SourceId
            });
        }
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "view.apply_filter_safe",
            DryRun = false,
            ChangedIds = diff.ModifiedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "viewName=" + plan.View.Name,
                "filterName=" + plan.Filter.Name
            },
            ReviewSummary = review
        };
    }

    private static ThreeDViewPlan Build3DViewPlan(UIApplication uiapp, Document doc, Create3DViewRequest request)
    {
        request ??= new Create3DViewRequest();
        if (string.IsNullOrWhiteSpace(request.ViewName))
        {
            throw new InvalidOperationException("Thiếu ViewName cho view.create_3d_safe.");
        }

        var plan = new ThreeDViewPlan
        {
            TargetViewName = request.ViewName.Trim(),
            CopyOrientation = request.UseActive3DOrientationWhenPossible,
            CopySectionBox = request.CopySectionBoxFromActive3D
        };

        plan.ViewFamilyType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional)
            ?? throw new InvalidOperationException("Không tìm thấy ViewFamilyType cho 3D view.");

        plan.ExistingView = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, plan.TargetViewName, StringComparison.OrdinalIgnoreCase));

        if (uiapp.ActiveUIDocument?.Document?.Equals(doc) == true && doc.ActiveView is View3D active3D && !active3D.IsTemplate)
        {
            plan.Source3DView = active3D;
        }

        if (request.ViewTemplateId.HasValue)
        {
            plan.TemplateView = doc.GetElement(new ElementId((long)request.ViewTemplateId.Value)) as View;
        }
        else if (!string.IsNullOrWhiteSpace(request.ViewTemplateName))
        {
            plan.TemplateView = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(x => x.IsTemplate && string.Equals(x.Name, request.ViewTemplateName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (plan.TemplateView != null && !plan.TemplateView.IsTemplate)
        {
            throw new InvalidOperationException("ViewTemplateId/ViewTemplateName phải trỏ tới view template.");
        }

        if (plan.ExistingView != null)
        {
            if (request.DuplicateIfExists)
            {
                plan.CreateNew = true;
                plan.TargetViewName = BuildUniqueViewName(doc, plan.TargetViewName);
                plan.Diagnostics.Add(DiagnosticRecord.Create("VIEW_NAME_EXISTS_DUPLICATE", DiagnosticSeverity.Warning, "Tên view đã tồn tại; sẽ tạo bản mới với tên unique."));
            }
            else if (request.FailIfExists)
            {
                plan.Diagnostics.Add(DiagnosticRecord.Create("VIEW_NAME_ALREADY_EXISTS", DiagnosticSeverity.Error, "Tên view đã tồn tại; dry-run sẽ fail nếu không đổi mode.", checked((int)plan.ExistingView.Id.Value)));
            }
            else
            {
                plan.CreateNew = false;
                plan.Diagnostics.Add(DiagnosticRecord.Create("VIEW_UPDATE_EXISTING", DiagnosticSeverity.Info, "Sẽ update view 3D đang tồn tại.", checked((int)plan.ExistingView.Id.Value)));
            }
        }
        else
        {
            plan.CreateNew = true;
        }

        if (plan.Source3DView == null)
        {
            plan.CopyOrientation = false;
            plan.CopySectionBox = false;
            plan.Diagnostics.Add(DiagnosticRecord.Create("NO_ACTIVE_3D_SOURCE", DiagnosticSeverity.Info, "Không có active 3D view để copy orientation/section box."));
        }

        return plan;
    }

    private static ViewFilterPlan BuildFilterPlan(UIApplication uiapp, Document doc, CreateOrUpdateViewFilterRequest request)
    {
        request ??= new CreateOrUpdateViewFilterRequest();
        if (string.IsNullOrWhiteSpace(request.FilterName))
        {
            throw new InvalidOperationException("Thiếu FilterName cho view.create_or_update_filter_safe.");
        }

        var plan = new ViewFilterPlan
        {
            FilterName = request.FilterName.Trim(),
            ExistingFilter = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(x => string.Equals(x.Name, request.FilterName.Trim(), StringComparison.OrdinalIgnoreCase))
        };

        plan.CategoryIds = ResolveCategoryIds(uiapp, doc, request);
        if (plan.CategoryIds.Count == 0)
        {
            throw new InvalidOperationException("Không resolve được category nào cho filter.");
        }

        plan.Rules = request.Rules ?? new List<ViewFilterRuleRequest>();
        if (plan.Rules.Count == 0)
        {
            throw new InvalidOperationException("Filter tool cần ít nhất 1 rule.");
        }

        if (plan.ExistingFilter != null && !request.OverwriteIfExists)
        {
            plan.Diagnostics.Add(DiagnosticRecord.Create("FILTER_EXISTS_OVERWRITE_DISABLED", DiagnosticSeverity.Error, "Filter đã tồn tại và OverwriteIfExists=false.", checked((int)plan.ExistingFilter.Id.Value)));
            return plan;
        }

        var elementFilters = new List<ElementFilter>();
        foreach (var ruleRequest in plan.Rules)
        {
            var resolved = ResolveFilterRule(doc, plan.CategoryIds, ruleRequest);
            elementFilters.Add(new ElementParameterFilter(resolved.FilterRule));
            plan.Diagnostics.Add(resolved.Diagnostic);
        }

        plan.ElementFilter = elementFilters.Count == 1 ? elementFilters[0] : new LogicalAndFilter(elementFilters);
        return plan;
    }

    private static ApplyViewFilterPlan BuildApplyFilterPlan(UIApplication uiapp, PlatformServices services, Document doc, ApplyViewFilterRequest request)
    {
        request ??= new ApplyViewFilterRequest();
        var view = services.ResolveView(uiapp, doc, request.ViewName, request.ViewId);
        var filter = ResolveExistingFilter(doc, request.FilterId, request.FilterName);
        var alreadyApplied = view.GetFilters().Contains(filter.Id);

        var plan = new ApplyViewFilterPlan
        {
            View = view,
            Filter = filter,
            FilterAlreadyApplied = alreadyApplied
        };

        if (request.ProjectionLineColorRed.HasValue || request.ProjectionLineColorGreen.HasValue || request.ProjectionLineColorBlue.HasValue)
        {
            if (!(request.ProjectionLineColorRed.HasValue && request.ProjectionLineColorGreen.HasValue && request.ProjectionLineColorBlue.HasValue))
            {
                plan.Diagnostics.Add(DiagnosticRecord.Create("FILTER_COLOR_INCOMPLETE", DiagnosticSeverity.Error, "Muốn set ProjectionLineColor thì phải truyền đủ Red/Green/Blue."));
            }
        }

        return plan;
    }

    private static IList<ElementId> ResolveCategoryIds(UIApplication uiapp, Document doc, CreateOrUpdateViewFilterRequest request)
    {
        var resolved = new Dictionary<long, ElementId>();

        foreach (var id in request.CategoryIds ?? new List<int>())
        {
            var category = Category.GetCategory(doc, new ElementId((long)id));
            if (category != null)
            {
                resolved[category.Id.Value] = category.Id;
            }
        }

        foreach (Category category in doc.Settings.Categories)
        {
            if ((request.CategoryNames ?? new List<string>()).Any(x => string.Equals(x, category.Name, StringComparison.OrdinalIgnoreCase)))
            {
                resolved[category.Id.Value] = category.Id;
            }
        }

        if (resolved.Count == 0 && request.InferCategoriesFromSelectionWhenEmpty && uiapp.ActiveUIDocument?.Document?.Equals(doc) == true)
        {
            foreach (var id in uiapp.ActiveUIDocument.Selection.GetElementIds())
            {
                var element = doc.GetElement(id);
                if (element?.Category != null)
                {
                    resolved[element.Category.Id.Value] = element.Category.Id;
                }
            }
        }

        return resolved.Values.ToList();
    }

    private static ResolvedFilterRule ResolveFilterRule(Document doc, IList<ElementId> categoryIds, ViewFilterRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ParameterName))
        {
            throw new InvalidOperationException("Rule đang thiếu ParameterName.");
        }

        Parameter? parameter = null;
        bool fromType = false;
        var multiCategory = new ElementMulticategoryFilter(categoryIds);
        foreach (var sample in new FilteredElementCollector(doc).WherePasses(multiCategory).WhereElementIsNotElementType().ToElements())
        {
            parameter = sample.LookupParameter(request.ParameterName);
            if (parameter == null)
            {
                var type = doc.GetElement(sample.GetTypeId());
                parameter = type?.LookupParameter(request.ParameterName);
                fromType = parameter != null;
            }

            if (parameter != null)
            {
                break;
            }
        }

        if (parameter == null)
        {
            throw new InvalidOperationException("Không resolve được ParameterName `" + request.ParameterName + "` trên categories đã chọn.");
        }

        var operatorName = (request.Operator ?? "equals").Trim().ToLowerInvariant();

        // HasValue / HasNoValue không phụ thuộc StorageType — xử lý trước switch
        if (operatorName is "has_value" or "hasvalue")
        {
            return new ResolvedFilterRule
            {
                FilterRule = ParameterFilterRuleFactory.CreateHasValueParameterRule(parameter.Id),
                Diagnostic = DiagnosticRecord.Create(
                    "FILTER_RULE_RESOLVED",
                    DiagnosticSeverity.Info,
                    $"Resolved rule `{request.ParameterName}` has_value ({parameter.StorageType})" + (fromType ? " from type." : " from instance."))
            };
        }

        if (operatorName is "has_no_value" or "hasnovalue" or "no_value")
        {
            return new ResolvedFilterRule
            {
                FilterRule = ParameterFilterRuleFactory.CreateHasNoValueParameterRule(parameter.Id),
                Diagnostic = DiagnosticRecord.Create(
                    "FILTER_RULE_RESOLVED",
                    DiagnosticSeverity.Info,
                    $"Resolved rule `{request.ParameterName}` has_no_value ({parameter.StorageType})" + (fromType ? " from type." : " from instance."))
            };
        }

        FilterRule rule;
        switch (parameter.StorageType)
        {
            case StorageType.String:
                rule = operatorName switch
                {
                    "equals" or "eq" => ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, request.Value ?? string.Empty),
                    "contains" => ParameterFilterRuleFactory.CreateContainsRule(parameter.Id, request.Value ?? string.Empty),
                    "begins_with" or "starts_with" => ParameterFilterRuleFactory.CreateBeginsWithRule(parameter.Id, request.Value ?? string.Empty),
                    "ends_with" => ParameterFilterRuleFactory.CreateEndsWithRule(parameter.Id, request.Value ?? string.Empty),
                    _ => throw new InvalidOperationException("Operator `" + request.Operator + "` chưa support cho string rule.")
                };
                break;
            case StorageType.Integer:
                var intValue = ParseIntLike(request.Value);
                rule = operatorName switch
                {
                    "equals" or "eq" => ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, intValue),
                    "greater" or "gt" => ParameterFilterRuleFactory.CreateGreaterRule(parameter.Id, intValue),
                    "greater_or_equal" or "gte" => ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameter.Id, intValue),
                    "less" or "lt" => ParameterFilterRuleFactory.CreateLessRule(parameter.Id, intValue),
                    "less_or_equal" or "lte" => ParameterFilterRuleFactory.CreateLessOrEqualRule(parameter.Id, intValue),
                    _ => throw new InvalidOperationException("Operator `" + request.Operator + "` chưa support cho integer rule.")
                };
                break;
            case StorageType.Double:
                var doubleValue = ParseDoubleLike(request.Value);
                rule = operatorName switch
                {
                    "equals" or "eq" => ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, doubleValue, 1e-6),
                    "greater" or "gt" => ParameterFilterRuleFactory.CreateGreaterRule(parameter.Id, doubleValue, 1e-6),
                    "greater_or_equal" or "gte" => ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameter.Id, doubleValue, 1e-6),
                    "less" or "lt" => ParameterFilterRuleFactory.CreateLessRule(parameter.Id, doubleValue, 1e-6),
                    "less_or_equal" or "lte" => ParameterFilterRuleFactory.CreateLessOrEqualRule(parameter.Id, doubleValue, 1e-6),
                    _ => throw new InvalidOperationException("Operator `" + request.Operator + "` chưa support cho double rule.")
                };
                break;
            case StorageType.ElementId:
                var elementIdValue = new ElementId((long)ParseIntLike(request.Value));
                rule = operatorName switch
                {
                    "equals" or "eq" => ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, elementIdValue),
                    _ => throw new InvalidOperationException("Operator `" + request.Operator + "` chưa support cho ElementId rule.")
                };
                break;
            default:
                throw new InvalidOperationException("StorageType `" + parameter.StorageType + "` chưa support cho filter rule.");
        }

        return new ResolvedFilterRule
        {
            FilterRule = rule,
            Diagnostic = DiagnosticRecord.Create(
                "FILTER_RULE_RESOLVED",
                DiagnosticSeverity.Info,
                $"Resolved rule `{request.ParameterName}` ({parameter.StorageType})" + (fromType ? " from type." : " from instance."))
        };
    }

    private static ParameterFilterElement ResolveExistingFilter(Document doc, int? filterId, string filterName)
    {
        // Dùng FilteredElementCollector — GetElement(ElementId) không reliable với ParameterFilterElement trên workshared docs
        var allFilters = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .ToList();

        if (filterId.HasValue)
        {
            var byId = allFilters.FirstOrDefault(x => x.Id.Value == (long)filterId.Value);
            if (byId != null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(filterName))
        {
            var byName = allFilters.FirstOrDefault(x => string.Equals(x.Name, filterName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }

        throw new InvalidOperationException("Không resolve được filter để apply.");
    }

    private static string BuildUniqueViewName(Document doc, string baseName)
    {
        var existing = new HashSet<string>(new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Select(x => x.Name), StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không tạo được unique view name cho `" + baseName + "`.");
    }

    private static int ParseIntLike(string raw)
    {
        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return 0;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException("Không parse được integer value: " + raw);
    }

    private static double ParseDoubleLike(string raw)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException("Không parse được double value: " + raw);
    }

    private static byte ToByte(int value)
    {
        return (byte)Math.Max(0, Math.Min(255, value));
    }

    private sealed class ThreeDViewPlan
    {
        internal string TargetViewName { get; set; } = string.Empty;
        internal bool CreateNew { get; set; } = true;
        internal bool CopyOrientation { get; set; }
        internal bool CopySectionBox { get; set; }
        internal ViewFamilyType ViewFamilyType { get; set; } = null!;
        internal View3D? ExistingView { get; set; }
        internal View3D? Source3DView { get; set; }
        internal View? TemplateView { get; set; }
        internal List<DiagnosticRecord> Diagnostics { get; } = new List<DiagnosticRecord>();
    }

    private sealed class ViewFilterPlan
    {
        internal string FilterName { get; set; } = string.Empty;
        internal ParameterFilterElement? ExistingFilter { get; set; }
        internal IList<ElementId> CategoryIds { get; set; } = new List<ElementId>();
        internal List<ViewFilterRuleRequest> Rules { get; set; } = new List<ViewFilterRuleRequest>();
        internal ElementFilter ElementFilter { get; set; } = null!;
        internal List<DiagnosticRecord> Diagnostics { get; } = new List<DiagnosticRecord>();
    }

    private sealed class ApplyViewFilterPlan
    {
        internal View View { get; set; } = null!;
        internal ParameterFilterElement Filter { get; set; } = null!;
        internal bool FilterAlreadyApplied { get; set; }
        internal List<DiagnosticRecord> Diagnostics { get; } = new List<DiagnosticRecord>();
    }

    private sealed class RemoveFilterFromViewPlan
    {
        internal View View { get; set; } = null!;
        internal ParameterFilterElement Filter { get; set; } = null!;
        internal bool FilterApplied { get; set; }
        internal List<DiagnosticRecord> Diagnostics { get; } = new List<DiagnosticRecord>();
    }

    private sealed class DeleteFilterPlan
    {
        internal ParameterFilterElement Filter { get; set; } = null!;
        internal List<FilterUsageEntry> ViewUsages { get; set; } = new List<FilterUsageEntry>();
        internal List<FilterUsageEntry> TemplateUsages { get; set; } = new List<FilterUsageEntry>();
        internal List<DiagnosticRecord> Diagnostics { get; } = new List<DiagnosticRecord>();
    }

    private sealed class ResolvedFilterRule
    {
        internal FilterRule FilterRule { get; set; } = null!;
        internal DiagnosticRecord Diagnostic { get; set; } = DiagnosticRecord.Create("FILTER_RULE", DiagnosticSeverity.Info, string.Empty);
    }
}
