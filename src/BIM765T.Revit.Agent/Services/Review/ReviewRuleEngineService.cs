using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Review;

internal sealed class ReviewRuleEngineService
{
    internal ReviewRuleSetResponse Run(UIApplication uiapp, PlatformServices platform, Document doc, ReviewRuleSetRunRequest request, string requestedView)
    {
        request ??= new ReviewRuleSetRunRequest();
        var normalized = (request.RuleSetName ?? "document_health_v1").Trim().ToLowerInvariant();
        return normalized switch
        {
            "document_health_v1" => RunDocumentHealth(uiapp, platform, doc),
            "active_view_v1" => RunActiveView(uiapp, platform, doc, request, requestedView),
            "selection_v1" => RunSelection(uiapp, platform, doc, request),
            "parameter_completeness_v1" => RunParameterCompleteness(uiapp, platform, doc, request),
            "workset_hygiene_v1" => RunWorksetHygiene(uiapp, platform, doc),
            "sheet_qc_v1" => RunSheetQc(uiapp, platform, doc, request),
            "snapshot_scope_v1" => RunSnapshotScope(uiapp, platform, doc, request),
            _ => throw new InvalidOperationException("Unknown review rule set: " + request.RuleSetName)
        };
    }

    private static ReviewRuleSetResponse RunDocumentHealth(UIApplication uiapp, PlatformServices platform, Document doc)
    {
        var health = platform.ReviewModelHealth(uiapp, doc);
        return new ReviewRuleSetResponse
        {
            DocumentKey = health.DocumentKey,
            ViewKey = health.ViewKey,
            RuleSetName = "document_health_v1",
            AppliedRuleCount = 3,
            TriggeredRuleCount = health.Review.IssueCount,
            AppliedRules = new List<string> { "DOC_MODIFIED", "DOC_WARNINGS_PRESENT", "LINKS_UNLOADED" },
            Review = health.Review
        };
    }

    private static ReviewRuleSetResponse RunActiveView(UIApplication uiapp, PlatformServices platform, Document doc, ReviewRuleSetRunRequest request, string requestedView)
    {
        var view = platform.ResolveView(uiapp, doc, requestedView, request.ViewId);
        var summary = platform.ReviewActiveViewSummary(uiapp, doc, new ActiveViewSummaryRequest
        {
            ViewId = request.ViewId
        }, requestedView);

        var report = new ReviewReport
        {
            Name = "active_view_v1",
            DocumentKey = platform.GetDocumentKey(doc),
            ViewKey = platform.GetViewKey(view)
        };

        if (view.IsTemplate)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "VIEW_TEMPLATE_BLOCKED",
                Severity = DiagnosticSeverity.Error,
                Message = "Active/target view là view template; review runtime nên chạy trên view thực."
            });
        }

        if (summary.TotalVisibleElements == 0)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "VIEW_EMPTY",
                Severity = DiagnosticSeverity.Warning,
                Message = "View hiện tại không có element visible trong scope collector."
            });
        }

        if (summary.WarningCount > 0)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "VIEW_DOC_WARNINGS_PRESENT",
                Severity = DiagnosticSeverity.Warning,
                Message = $"Document chứa {summary.WarningCount} warning trong khi đang review view."
            });
        }

        if (summary.SelectedCount == 0)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "SELECTION_EMPTY_INFO",
                Severity = DiagnosticSeverity.Info,
                Message = "Không có selection; active_view_v1 đang đánh giá theo toàn view."
            });
        }

        report.IssueCount = report.Issues.Count;
        return new ReviewRuleSetResponse
        {
            DocumentKey = report.DocumentKey,
            ViewKey = report.ViewKey,
            RuleSetName = "active_view_v1",
            AppliedRuleCount = 4,
            TriggeredRuleCount = report.IssueCount,
            AppliedRules = new List<string> { "VIEW_TEMPLATE_BLOCKED", "VIEW_EMPTY", "VIEW_DOC_WARNINGS_PRESENT", "SELECTION_EMPTY_INFO" },
            Review = report
        };
    }

    private static ReviewRuleSetResponse RunSelection(UIApplication uiapp, PlatformServices platform, Document doc, ReviewRuleSetRunRequest request)
    {
        var selectionIds = ResolveScopedElementIds(uiapp, doc, request);
        var report = new ReviewReport
        {
            Name = "selection_v1",
            DocumentKey = platform.GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? platform.GetViewKey(doc.ActiveView) : string.Empty
        };

        if (selectionIds.Count == 0)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "SELECTION_REQUIRED",
                Severity = DiagnosticSeverity.Warning,
                Message = "Selection rule set yêu cầu selection hoặc ElementIds đầu vào."
            });
        }
        else
        {
            foreach (var id in selectionIds)
            {
                if (doc.GetElement(new ElementId((long)id)) == null)
                {
                    report.Issues.Add(new ReviewIssue
                    {
                        Code = "ELEMENT_MISSING",
                        Severity = DiagnosticSeverity.Warning,
                        Message = "Một phần tử trong selection/input không tồn tại.",
                        ElementId = id
                    });
                }
            }
        }

        if ((request.RequiredParameterNames ?? new List<string>()).Count > 0 && selectionIds.Count > 0)
        {
            var completeness = platform.ReviewParameterCompleteness(doc, new ReviewParameterCompletenessRequest
            {
                ElementIds = selectionIds,
                RequiredParameterNames = request.RequiredParameterNames ?? new List<string>()
            });
            report.Issues.AddRange(completeness.Issues);
        }

        TrimIssues(report, request.MaxIssues);
        return new ReviewRuleSetResponse
        {
            DocumentKey = report.DocumentKey,
            ViewKey = report.ViewKey,
            RuleSetName = "selection_v1",
            AppliedRuleCount = (request.RequiredParameterNames ?? new List<string>()).Count > 0 ? 2 : 1,
            TriggeredRuleCount = report.IssueCount,
            AppliedRules = new List<string>
            {
                "SELECTION_REQUIRED",
                (request.RequiredParameterNames ?? new List<string>()).Count > 0 ? "REQUIRED_PARAMETER_COMPLETENESS" : "SELECTION_EXISTS"
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
            Review = report
        };
    }

    private static ReviewRuleSetResponse RunParameterCompleteness(UIApplication uiapp, PlatformServices platform, Document doc, ReviewRuleSetRunRequest request)
    {
        var elementIds = ResolveScopedElementIds(uiapp, doc, request);
        var report = platform.ReviewParameterCompleteness(doc, new ReviewParameterCompletenessRequest
        {
            ElementIds = elementIds,
            RequiredParameterNames = request.RequiredParameterNames ?? new List<string>()
        });

        if (elementIds.Count == 0)
        {
            report.Issues.Insert(0, new ReviewIssue
            {
                Code = "ELEMENT_SCOPE_EMPTY",
                Severity = DiagnosticSeverity.Warning,
                Message = "parameter_completeness_v1 không có ElementIds hoặc selection để kiểm tra."
            });
            report.IssueCount = report.Issues.Count;
        }

        TrimIssues(report, request.MaxIssues);
        return new ReviewRuleSetResponse
        {
            DocumentKey = report.DocumentKey,
            ViewKey = report.ViewKey,
            RuleSetName = "parameter_completeness_v1",
            AppliedRuleCount = 1,
            TriggeredRuleCount = report.IssueCount,
            AppliedRules = new List<string> { "REQUIRED_PARAMETER_COMPLETENESS" },
            Review = report
        };
    }

    private static ReviewRuleSetResponse RunWorksetHygiene(UIApplication uiapp, PlatformServices platform, Document doc)
    {
        var result = platform.ReviewWorksetHealth(uiapp, doc);
        return new ReviewRuleSetResponse
        {
            DocumentKey = result.DocumentKey,
            ViewKey = doc.ActiveView != null ? platform.GetViewKey(doc.ActiveView) : string.Empty,
            RuleSetName = "workset_hygiene_v1",
            AppliedRuleCount = 6,
            TriggeredRuleCount = result.Review.IssueCount,
            AppliedRules = new List<string>
            {
                "DOC_NOT_WORKSHARED",
                "ACTIVE_WORKSET_UNKNOWN",
                "SELECTION_MULTIPLE_WORKSETS",
                "ACTIVE_WORKSET_MISMATCH_SELECTION",
                "ACTIVE_WORKSET_NOT_EDITABLE",
                "SELECTION_WORKSET_NOT_EDITABLE"
            },
            Review = result.Review
        };
    }

    private static ReviewRuleSetResponse RunSheetQc(UIApplication uiapp, PlatformServices platform, Document doc, ReviewRuleSetRunRequest request)
    {
        var summary = platform.ReviewSheetSummary(uiapp, doc, new SheetSummaryRequest
        {
            DocumentKey = request.DocumentKey,
            SheetId = request.SheetId,
            SheetNumber = request.SheetNumber ?? string.Empty,
            SheetName = request.SheetName ?? string.Empty,
            RequiredParameterNames = request.RequiredParameterNames ?? new List<string>()
        });

        TrimIssues(summary.Review, request.MaxIssues);
        return new ReviewRuleSetResponse
        {
            DocumentKey = summary.DocumentKey,
            ViewKey = $"view:{summary.SheetId}",
            RuleSetName = "sheet_qc_v1",
            AppliedRuleCount = 5 + ((request.RequiredParameterNames ?? new List<string>()).Count > 0 ? 1 : 0),
            TriggeredRuleCount = summary.Review.IssueCount,
            AppliedRules = new List<string>
            {
                "SHEET_NUMBER_EMPTY",
                "SHEET_NAME_EMPTY",
                "SHEET_PLACEHOLDER",
                "TITLEBLOCK_MISSING",
                "SHEET_EMPTY_LAYOUT",
                (request.RequiredParameterNames ?? new List<string>()).Count > 0 ? "SHEET_REQUIRED_PARAMETER_COMPLETENESS" : string.Empty
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
            Review = summary.Review
        };
    }

    private static ReviewRuleSetResponse RunSnapshotScope(UIApplication uiapp, PlatformServices platform, Document doc, ReviewRuleSetRunRequest request)
    {
        var snapshot = platform.CaptureSnapshot(uiapp, doc, new CaptureSnapshotRequest
        {
            DocumentKey = request.DocumentKey,
            Scope = request.SheetId.HasValue || !string.IsNullOrWhiteSpace(request.SheetNumber) || !string.IsNullOrWhiteSpace(request.SheetName)
                ? "sheet"
                : (request.ElementIds ?? new List<int>()).Count > 0
                    ? "element_ids"
                    : request.UseCurrentSelectionWhenEmpty
                        ? "selection"
                        : "active_view",
            SheetId = request.SheetId,
            SheetNumber = request.SheetNumber ?? string.Empty,
            SheetName = request.SheetName ?? string.Empty,
            ElementIds = request.ElementIds ?? new List<int>(),
            MaxElements = Math.Max(1, request.MaxIssues)
        });

        var report = snapshot.Review;
        if (snapshot.ElementCount == 0)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "SNAPSHOT_ZERO_ELEMENTS",
                Severity = DiagnosticSeverity.Warning,
                Message = "Snapshot scope không trả về element nào để review."
            });
        }

        TrimIssues(report, request.MaxIssues);
        return new ReviewRuleSetResponse
        {
            DocumentKey = snapshot.DocumentKey,
            ViewKey = snapshot.ViewKey,
            RuleSetName = "snapshot_scope_v1",
            AppliedRuleCount = 2,
            TriggeredRuleCount = report.IssueCount,
            AppliedRules = new List<string> { "SNAPSHOT_SCOPE_EMPTY", "SNAPSHOT_ZERO_ELEMENTS" },
            Review = report
        };
    }

    private static List<int> ResolveScopedElementIds(UIApplication uiapp, Document doc, ReviewRuleSetRunRequest request)
    {
        var explicitIds = request.ElementIds ?? new List<int>();
        if (explicitIds.Count > 0)
        {
            return explicitIds.Distinct().ToList();
        }

        if (!request.UseCurrentSelectionWhenEmpty)
        {
            return new List<int>();
        }

        if (uiapp.ActiveUIDocument?.Document?.Equals(doc) != true)
        {
            return new List<int>();
        }

        return uiapp.ActiveUIDocument.Selection.GetElementIds()
            .Select(x => checked((int)x.Value))
            .Distinct()
            .ToList();
    }

    private static void TrimIssues(ReviewReport report, int maxIssues)
    {
        var safeMax = Math.Max(1, maxIssues);
        if (report.Issues.Count > safeMax)
        {
            report.Issues = report.Issues.Take(safeMax).ToList();
        }

        report.IssueCount = report.Issues.Count;
    }
}
