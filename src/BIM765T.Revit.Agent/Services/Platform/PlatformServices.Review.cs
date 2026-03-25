using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Review operations: warnings, parameter completeness, view summary,
/// links status, workset health, model health, sheet summary, snapshots.
/// </summary>
internal sealed partial class PlatformServices
{
    internal ReviewReport ReviewWarnings(Document doc)
    {
        var report = new ReviewReport
        {
            Name = "model_warnings",
            DocumentKey = GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? GetViewKey(doc.ActiveView) : string.Empty
        };

        foreach (var warning in doc.GetWarnings())
        {
            var firstFailing = warning.GetFailingElements().FirstOrDefault();
            report.Issues.Add(new ReviewIssue
            {
                Code = "REVIT_WARNING",
                Severity = DiagnosticSeverity.Warning,
                Message = warning.GetDescriptionText(),
                ElementId = firstFailing != null ? checked((int)firstFailing.Value) : (int?)null
            });
        }

        report.IssueCount = report.Issues.Count;
        return report;
    }

    internal ReviewReport ReviewParameterCompleteness(Document doc, ReviewParameterCompletenessRequest request)
    {
        var elementIds = request.ElementIds ?? new List<int>();
        var requiredParameterNames = request.RequiredParameterNames ?? new List<string>();
        var report = new ReviewReport
        {
            Name = "parameter_completeness",
            DocumentKey = GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? GetViewKey(doc.ActiveView) : string.Empty
        };

        foreach (var id in elementIds)
        {
            var element = doc.GetElement(new ElementId((long)id));
            if (element == null)
            {
                report.Issues.Add(new ReviewIssue
                {
                    Code = "ELEMENT_MISSING",
                    Severity = DiagnosticSeverity.Warning,
                    Message = "Element không tồn tại.",
                    ElementId = id
                });
                continue;
            }

            foreach (var parameterName in requiredParameterNames)
            {
                var parameter = element.LookupParameter(parameterName);
                var value = parameter?.AsString() ?? parameter?.AsValueString();
                if (parameter == null || string.IsNullOrWhiteSpace(value))
                {
                    report.Issues.Add(new ReviewIssue
                    {
                        Code = "PARAMETER_MISSING_OR_EMPTY",
                        Severity = DiagnosticSeverity.Warning,
                        Message = $"Parameter `{parameterName}` bị thiếu hoặc rỗng.",
                        ElementId = id
                    });
                }
            }
        }

        report.IssueCount = report.Issues.Count;
        return report;
    }

    internal ActiveViewSummaryResponse ReviewActiveViewSummary(UIApplication uiapp, Document doc, ActiveViewSummaryRequest request, string requestedView)
    {
        var view = ResolveView(uiapp, doc, requestedView, request.ViewId);
        var response = new ActiveViewSummaryResponse
        {
            DocumentKey = GetDocumentKey(doc),
            ViewKey = GetViewKey(view),
            ViewId = checked((int)view.Id.Value),
            ViewName = view.Name,
            ViewType = view.ViewType.ToString(),
            LevelId = view.GenLevel != null ? checked((int)view.GenLevel.Id.Value) : (int?)null,
            LevelName = view.GenLevel?.Name ?? string.Empty,
            WarningCount = doc.GetWarnings().Count,
            SelectedCount = uiapp.ActiveUIDocument?.Document?.Equals(doc) == true ? uiapp.ActiveUIDocument.Selection.GetElementIds().Count : 0
        };

        var collector = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .ToElements();

        response.TotalVisibleElements = collector.Count;
        response.CategoryCounts = collector
            .GroupBy(x => x.Category?.Name ?? "<No Category>")
            .OrderByDescending(g => g.Count())
            .Take(Math.Max(1, request.MaxCategoryCount))
            .Select(g => new CountByNameDto { Name = g.Key, Count = g.Count() })
            .ToList();
        response.ClassCounts = collector
            .GroupBy(x => x.GetType().Name)
            .OrderByDescending(g => g.Count())
            .Take(Math.Max(1, request.MaxClassCount))
            .Select(g => new CountByNameDto { Name = g.Key, Count = g.Count() })
            .ToList();

        return response;
    }

    internal LinksStatusResponse ReviewLinksStatus(Document doc)
    {
        var response = new LinksStatusResponse
        {
            DocumentKey = GetDocumentKey(doc)
        };

        foreach (RevitLinkInstance link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            var linkDocument = link.GetLinkDocument();
            var linkType = doc.GetElement(link.GetTypeId()) as RevitLinkType;
            response.Links.Add(new LinkStatusDto
            {
                ElementId = checked((int)link.Id.Value),
                Name = link.Name,
                DocumentKey = GetDocumentKey(doc),
                IsLoaded = linkDocument != null,
                LinkedDocumentTitle = linkDocument?.Title ?? string.Empty,
                LinkedDocumentPath = linkDocument?.PathName ?? string.Empty,
                AttachmentType = linkType?.AttachmentType.ToString() ?? string.Empty,
                InstanceName = link.Name
            });
        }

        response.TotalLinks = response.Links.Count;
        response.LoadedLinks = response.Links.Count(x => x.IsLoaded);
        return response;
    }

    internal WorksetHealthResponse ReviewWorksetHealth(UIApplication uiapp, Document doc)
    {
        var response = new WorksetHealthResponse
        {
            DocumentKey = GetDocumentKey(doc),
            IsWorkshared = doc.IsWorkshared
        };

        var report = new ReviewReport
        {
            Name = "workset_health",
            DocumentKey = response.DocumentKey,
            ViewKey = doc.ActiveView != null ? GetViewKey(doc.ActiveView) : string.Empty
        };

        if (!doc.IsWorkshared)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "DOC_NOT_WORKSHARED",
                Severity = DiagnosticSeverity.Info,
                Message = "Document hiện tại không phải workshared model."
            });
            report.IssueCount = report.Issues.Count;
            response.Review = report;
            return response;
        }

        var table = doc.GetWorksetTable();
        var activeWorksetId = table.GetActiveWorksetId();
        response.ActiveWorksetId = checked((int)activeWorksetId.IntegerValue);
        response.ActiveWorksetName = table.GetWorkset(activeWorksetId)?.Name ?? string.Empty;

        var selectionWorksetCounts = new Dictionary<int, int>();
        if (uiapp.ActiveUIDocument?.Document?.Equals(doc) == true)
        {
            foreach (var selectedId in uiapp.ActiveUIDocument.Selection.GetElementIds())
            {
                var element = doc.GetElement(selectedId);
                if (element == null)
                {
                    continue;
                }

                var worksetId = checked((int)element.WorksetId.IntegerValue);
                selectionWorksetCounts[worksetId] = selectionWorksetCounts.TryGetValue(worksetId, out var count) ? count + 1 : 1;
            }
        }

        var worksets = new FilteredWorksetCollector(doc).ToWorksets();
        foreach (var workset in worksets.OrderBy(x => x.Kind.ToString()).ThenBy(x => x.Name))
        {
            var dto = new WorksetSummaryDto
            {
                WorksetId = checked((int)workset.Id.IntegerValue),
                Name = workset.Name,
                Kind = workset.Kind.ToString(),
                IsOpen = workset.IsOpen,
                IsEditable = workset.IsEditable,
                SelectionCount = selectionWorksetCounts.TryGetValue(checked((int)workset.Id.IntegerValue), out var cnt) ? cnt : 0
            };
            response.Worksets.Add(dto);
        }

        response.TotalWorksets = response.Worksets.Count;
        response.OpenWorksets = response.Worksets.Count(x => x.IsOpen);

        if (string.IsNullOrWhiteSpace(response.ActiveWorksetName))
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "ACTIVE_WORKSET_UNKNOWN",
                Severity = DiagnosticSeverity.Warning,
                Message = "Không resolve được active workset."
            });
        }

        if (selectionWorksetCounts.Count > 1)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "SELECTION_MULTIPLE_WORKSETS",
                Severity = DiagnosticSeverity.Warning,
                Message = "Selection hiện tại đang trải trên nhiều workset; cần kiểm tra scope trước khi edit."
            });
        }

        if (selectionWorksetCounts.Count == 1 && response.ActiveWorksetId.HasValue && selectionWorksetCounts.Keys.First() != response.ActiveWorksetId.Value)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "ACTIVE_WORKSET_MISMATCH_SELECTION",
                Severity = DiagnosticSeverity.Info,
                Message = "Active workset khác workset của selection hiện tại."
            });
        }

        var activeWorkset = response.ActiveWorksetId.HasValue
            ? response.Worksets.FirstOrDefault(x => x.WorksetId == response.ActiveWorksetId.Value)
            : null;
        if (activeWorkset != null && !activeWorkset.IsEditable)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "ACTIVE_WORKSET_NOT_EDITABLE",
                Severity = DiagnosticSeverity.Warning,
                Message = "Active workset is not editable; write tools may fail."
            });
        }

        var nonEditableSelectionWorksets = response.Worksets.Where(x => x.SelectionCount > 0 && !x.IsEditable).ToList();
        if (nonEditableSelectionWorksets.Count > 0)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "SELECTION_WORKSET_NOT_EDITABLE",
                Severity = DiagnosticSeverity.Warning,
                Message = "Selection contains elements on non-editable worksets: " + string.Join(", ", nonEditableSelectionWorksets.Select(x => x.Name))
            });
        }

        report.IssueCount = report.Issues.Count;
        response.Review = report;
        return response;
    }

    internal SheetSummaryResponse ReviewSheetSummary(UIApplication uiapp, Document doc, SheetSummaryRequest request)
    {
        request ??= new SheetSummaryRequest();
        var sheet = ResolveSheet(doc, request);
        var response = new SheetSummaryResponse
        {
            DocumentKey = GetDocumentKey(doc),
            SheetId = checked((int)sheet.Id.Value),
            SheetNumber = sheet.SheetNumber ?? string.Empty,
            SheetName = sheet.Name ?? string.Empty,
            IsPlaceholder = sheet.IsPlaceholder
        };

        var report = new ReviewReport
        {
            Name = "sheet_summary",
            DocumentKey = response.DocumentKey,
            ViewKey = GetViewKey(sheet)
        };

        var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .ToElements();
        response.TitleBlockCount = titleBlocks.Count;

        var viewports = new FilteredElementCollector(doc)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Where(x => x.SheetId == sheet.Id)
            .ToList();
        response.ViewportCount = viewports.Count;

        var schedules = new FilteredElementCollector(doc)
            .OfClass(typeof(ScheduleSheetInstance))
            .Cast<ScheduleSheetInstance>()
            .Where(x => x.OwnerViewId == sheet.Id)
            .ToList();
        response.ScheduleInstanceCount = schedules.Count;

        foreach (var viewId in sheet.GetAllPlacedViews().Take(Math.Max(1, request.MaxPlacedViews)))
        {
            var view = doc.GetElement(viewId) as View;
            var viewport = viewports.FirstOrDefault(x => x.ViewId == viewId);
            response.PlacedViews.Add(new PlacedViewInfoDto
            {
                ViewId = checked((int)viewId.Value),
                ViewName = view?.Name ?? string.Empty,
                ViewType = view?.ViewType.ToString() ?? string.Empty,
                ViewportId = viewport != null ? checked((int)viewport.Id.Value) : (int?)null
            });
        }

        if (string.IsNullOrWhiteSpace(response.SheetNumber))
        {
            report.Issues.Add(new ReviewIssue { Code = "SHEET_NUMBER_EMPTY", Severity = DiagnosticSeverity.Warning, Message = "Sheet number đang rỗng." });
        }

        if (string.IsNullOrWhiteSpace(response.SheetName))
        {
            report.Issues.Add(new ReviewIssue { Code = "SHEET_NAME_EMPTY", Severity = DiagnosticSeverity.Warning, Message = "Sheet name đang rỗng." });
        }

        if (response.IsPlaceholder)
        {
            report.Issues.Add(new ReviewIssue { Code = "SHEET_PLACEHOLDER", Severity = DiagnosticSeverity.Info, Message = "Sheet hiện tại là placeholder." });
        }

        if (response.TitleBlockCount == 0)
        {
            report.Issues.Add(new ReviewIssue { Code = "TITLEBLOCK_MISSING", Severity = DiagnosticSeverity.Warning, Message = "Sheet không có title block." });
        }

        if (response.TitleBlockCount > 1)
        {
            report.Issues.Add(new ReviewIssue { Code = "TITLEBLOCK_MULTIPLE", Severity = DiagnosticSeverity.Info, Message = $"Sheet has {response.TitleBlockCount} title block instances; please verify this is intentional." });
        }

        if (response.ViewportCount == 0 && response.ScheduleInstanceCount == 0)
        {
            report.Issues.Add(new ReviewIssue { Code = "SHEET_EMPTY_LAYOUT", Severity = DiagnosticSeverity.Warning, Message = "Sheet không có viewport hoặc schedule instance nào." });
        }

        foreach (var parameterName in request.RequiredParameterNames ?? new List<string>())
        {
            var parameter = sheet.LookupParameter(parameterName);
            var value = parameter?.AsString() ?? parameter?.AsValueString();
            if (parameter == null || string.IsNullOrWhiteSpace(value))
            {
                report.Issues.Add(new ReviewIssue
                {
                    Code = "SHEET_PARAMETER_MISSING_OR_EMPTY",
                    Severity = DiagnosticSeverity.Warning,
                    Message = $"Sheet parameter `{parameterName}` bị thiếu hoặc rỗng.",
                    ElementId = response.SheetId
                });
            }
        }

        report.IssueCount = report.Issues.Count;
        response.Review = report;
        return response;
    }

    internal SnapshotCaptureResponse CaptureSnapshot(UIApplication uiapp, Document doc, CaptureSnapshotRequest request)
    {
        request ??= new CaptureSnapshotRequest();
        var scope = (request.Scope ?? "active_view").Trim().ToLowerInvariant();
        var elementIds = new List<int>();
        string viewKey = doc.ActiveView != null ? GetViewKey(doc.ActiveView) : string.Empty;
        string summaryLabel;
        IEnumerable<string> parameterNames = request.ParameterNames ?? new List<string>();
        View? exportView = null;
        ViewSheet? exportSheet = null;

        switch (scope)
        {
            case "selection":
                if (uiapp.ActiveUIDocument?.Document?.Equals(doc) == true)
                {
                    elementIds = uiapp.ActiveUIDocument.Selection.GetElementIds().Select(x => checked((int)x.Value)).ToList();
                }
                summaryLabel = "selection";
                exportView = doc.ActiveView;
                break;
            case "element_ids":
                elementIds = (request.ElementIds ?? new List<int>()).Distinct().ToList();
                summaryLabel = "element_ids";
                exportView = doc.ActiveView;
                break;
            case "sheet":
                var sheet = ResolveSheet(doc, new SheetSummaryRequest { SheetId = request.SheetId, SheetNumber = request.SheetNumber, SheetName = request.SheetName });
                viewKey = GetViewKey(sheet);
                exportSheet = sheet;
                elementIds.AddRange(new FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType().ToElements().Select(x => checked((int)x.Id.Value)));
                elementIds.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Where(x => x.SheetId == sheet.Id).Select(x => checked((int)x.Id.Value)));
                elementIds = elementIds.Distinct().Take(Math.Max(1, request.MaxElements)).ToList();
                summaryLabel = $"sheet:{sheet.SheetNumber} - {sheet.Name}";
                break;
            case "active_view":
            default:
                var view = ResolveView(uiapp, doc, string.Empty, request.ViewId);
                viewKey = GetViewKey(view);
                exportView = view;
                elementIds = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Select(x => checked((int)x.Id.Value))
                    .Take(Math.Max(1, request.MaxElements))
                    .ToList();
                summaryLabel = $"view:{view.Name}";
                break;
        }

        var snapshot = Snapshot.Take(doc, elementIds, parameterNames);
        snapshot.DocumentKey = GetDocumentKey(doc);
        var elementResponse = QueryElements(uiapp, doc, new ElementQueryRequest
        {
            ElementIds = elementIds,
            IncludeParameters = request.IncludeParameters,
            MaxResults = Math.Max(1, request.MaxElements)
        });

        var review = new ReviewReport
        {
            Name = "snapshot_capture",
            DocumentKey = snapshot.DocumentKey,
            ViewKey = viewKey
        };
        if (elementIds.Count == 0)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "SNAPSHOT_SCOPE_EMPTY",
                Severity = DiagnosticSeverity.Warning,
                Message = "Snapshot scope hiện tại không resolve được element nào."
            });
        }
        review.IssueCount = review.Issues.Count;
        var artifactPaths = new List<string>();
        if (request.ExportImage)
        {
            try
            {
                artifactPaths.AddRange(ExportSnapshotImages(doc, exportView, exportSheet, request, summaryLabel));
            }
            catch (Exception ex)
            {
                review.Issues.Add(new ReviewIssue
                {
                    Code = "SNAPSHOT_IMAGE_EXPORT_FAILED",
                    Severity = DiagnosticSeverity.Warning,
                    Message = "Snapshot image export failed: " + ex.Message
                });
                review.IssueCount = review.Issues.Count;
            }
        }

        return new SnapshotCaptureResponse
        {
            DocumentKey = snapshot.DocumentKey,
            Scope = scope,
            ViewKey = viewKey,
            SummaryLabel = summaryLabel,
            ElementCount = elementResponse.Count,
            Snapshot = snapshot,
            Elements = elementResponse.Items,
            Review = review,
            ArtifactPaths = artifactPaths
        };
    }

    internal ModelHealthResponse ReviewModelHealth(UIApplication uiapp, Document doc)
    {
        var report = new ReviewReport
        {
            Name = "model_health",
            DocumentKey = GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? GetViewKey(doc.ActiveView) : string.Empty
        };

        if (string.IsNullOrWhiteSpace(doc.PathName))
        {
            report.Issues.Add(new ReviewIssue { Code = "DOC_NO_PATH", Severity = DiagnosticSeverity.Warning, Message = "Document chưa có PathName; save/sync workflow sẽ bị giới hạn." });
        }

        if (doc.IsModified)
        {
            report.Issues.Add(new ReviewIssue { Code = "DOC_MODIFIED", Severity = DiagnosticSeverity.Info, Message = "Document đang có thay đổi chưa lưu." });
        }

        var warningCount = doc.GetWarnings().Count;
        if (warningCount > 0)
        {
            report.Issues.Add(new ReviewIssue { Code = "DOC_WARNINGS_PRESENT", Severity = DiagnosticSeverity.Warning, Message = $"Document đang có {warningCount} warning." });
        }

        var links = ReviewLinksStatus(doc);
        if (links.TotalLinks > 0 && links.LoadedLinks < links.TotalLinks)
        {
            report.Issues.Add(new ReviewIssue { Code = "LINKS_UNLOADED", Severity = DiagnosticSeverity.Warning, Message = $"Có {links.TotalLinks - links.LoadedLinks} link chưa load." });
        }

        var docKey = GetDocumentKey(doc);
        var recentEvents = EventIndex.GetRecent().Where(x => string.Equals(x.DocumentKey, docKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var recentChangeEvents = recentEvents.Count(x => string.Equals(x.EventKind, "DocumentChanged", StringComparison.OrdinalIgnoreCase));
        var recentSaveEvents = recentEvents.Count(x => string.Equals(x.EventKind, "DocumentSaved", StringComparison.OrdinalIgnoreCase));

        if (doc.IsWorkshared && !Settings.AllowSyncTools)
        {
            report.Issues.Add(new ReviewIssue { Code = "SYNC_DISABLED_BY_SETTINGS", Severity = DiagnosticSeverity.Info, Message = "Model workshared nhưng sync tools đang disabled trong settings." });
        }

        report.IssueCount = report.Issues.Count;
        return new ModelHealthResponse
        {
            DocumentKey = docKey,
            ViewKey = doc.ActiveView != null ? GetViewKey(doc.ActiveView) : string.Empty,
            HasPath = !string.IsNullOrWhiteSpace(doc.PathName),
            IsModified = doc.IsModified,
            IsWorkshared = doc.IsWorkshared,
            TotalWarnings = warningCount,
            TotalLinks = links.TotalLinks,
            LoadedLinks = links.LoadedLinks,
            RecentChangeEvents = recentChangeEvents,
            RecentSaveEvents = recentSaveEvents,
            Review = report
        };
    }

    internal ReviewReport BuildExecutionReview(string name, DiffSummary diff)
    {
        var report = new ReviewReport
        {
            Name = name,
            IssueCount = diff.WarningDelta
        };

        if (diff.WarningDelta > 0)
        {
            report.Issues.Add(new ReviewIssue
            {
                Code = "WARNING_DELTA",
                Severity = DiagnosticSeverity.Warning,
                Message = "Warning count tăng sau operation."
            });
        }

        return report;
    }

    private static IEnumerable<string> ExportSnapshotImages(Document doc, View? view, ViewSheet? sheet, CaptureSnapshotRequest request, string summaryLabel)
    {
        var target = (Element?)view ?? sheet ?? throw new InvalidOperationException("Snapshot image export requires a valid view or sheet.");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = string.IsNullOrWhiteSpace(request.ImageOutputPath)
            ? Path.Combine(appData, Contracts.Common.BridgeConstants.AppDataFolderName, "snapshots")
            : (Directory.Exists(request.ImageOutputPath)
                ? request.ImageOutputPath
                : Path.GetDirectoryName(request.ImageOutputPath) ?? request.ImageOutputPath);
        Directory.CreateDirectory(directory);

        var fileStem = string.IsNullOrWhiteSpace(request.ImageOutputPath) || Directory.Exists(request.ImageOutputPath)
            ? Path.Combine(directory, BuildSnapshotFileStem(doc.Title, summaryLabel))
            : Path.Combine(directory, Path.GetFileNameWithoutExtension(request.ImageOutputPath));

        var before = new HashSet<string>(Directory.GetFiles(directory), StringComparer.OrdinalIgnoreCase);
        var options = new ImageExportOptions
        {
            ExportRange = ExportRange.SetOfViews,
            ZoomType = ZoomFitType.FitToPage,
            PixelSize = Math.Max(512, request.ImagePixelSize),
            HLRandWFViewsFileType = ImageFileType.PNG,
            ShadowViewsFileType = ImageFileType.PNG,
            ImageResolution = ImageResolution.DPI_150,
            FilePath = fileStem
        };
        options.SetViewsAndSheets(new List<ElementId> { target.Id });
        doc.ExportImage(options);

        return Directory.GetFiles(directory)
            .Except(before, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSnapshotFileStem(string docTitle, string summaryLabel)
    {
        var raw = $"{docTitle}_{summaryLabel}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
