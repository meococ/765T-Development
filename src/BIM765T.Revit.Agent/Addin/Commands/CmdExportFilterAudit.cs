using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Addin.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdExportFilterAudit : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc?.Document;
        if (doc == null)
        {
            TaskDialog.Show("765T Filter Audit", "Khong co document dang mo.");
            return Result.Cancelled;
        }

        var activeView = doc.ActiveView;
        if (activeView == null)
        {
            TaskDialog.Show("765T Filter Audit", "Khong resolve duoc active view.");
            return Result.Cancelled;
        }

        try
        {
            var viewAutomation = new ViewAutomationService();
            var allFilters = viewAutomation.ListViewFilters(doc, new ListViewFiltersRequest
            {
                IncludeCategoryNames = true,
                IncludeRuleSummary = true,
                MaxResults = 5000
            });

            var export = new FilterAuditExport
            {
                GeneratedUtc = DateTime.UtcNow,
                Document = BuildDocumentSummary(doc),
                ActiveView = BuildViewSummary(doc, activeView),
                ActiveViewScope = BuildScopeAudit(doc, activeView, viewAutomation)
            };

            if (activeView.ViewTemplateId != ElementId.InvalidElementId)
            {
                var templateView = doc.GetElement(activeView.ViewTemplateId) as View;
                if (templateView != null)
                {
                    export.TemplateView = BuildViewSummary(doc, templateView);
                    export.TemplateScope = BuildScopeAudit(doc, templateView, viewAutomation);
                }
            }

            export.TotalDocumentFilterCount = allFilters.TotalCount;
            export.AllDocumentFilters = allFilters.Filters ?? new List<ViewFilterSummary>();

            var diagnosticsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                BridgeConstants.AppDataFolderName,
                "diagnostics");
            Directory.CreateDirectory(diagnosticsDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var latestPath = Path.Combine(diagnosticsDir, "filter-audit.active.json");
            var archivePath = Path.Combine(diagnosticsDir, "filter-audit." + stamp + ".json");
            var json = JsonUtil.Serialize(export);

            File.WriteAllText(latestPath, json, new UTF8Encoding(false));
            File.WriteAllText(archivePath, json, new UTF8Encoding(false));

            TaskDialog.Show(
                "765T Filter Audit",
                "Da export filter audit.\n" +
                $"Document filters: {export.TotalDocumentFilterCount}\n" +
                $"Active view filters: {export.ActiveViewScope.Filters.Count}\n" +
                $"Template filters: {export.TemplateScope?.Filters.Count ?? 0}\n\n" +
                latestPath);

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("765T Filter Audit", "Export fail:\n" + ex);
            return Result.Failed;
        }
    }

    private static FilterDocumentSummary BuildDocumentSummary(Document doc)
    {
        return new FilterDocumentSummary
        {
            Title = doc.Title ?? string.Empty,
            PathName = doc.PathName ?? string.Empty,
            IsFamilyDocument = doc.IsFamilyDocument,
            IsWorkshared = doc.IsWorkshared
        };
    }

    private static FilterViewSummary BuildViewSummary(Document doc, View view)
    {
        var templateName = string.Empty;
        if (view.ViewTemplateId != ElementId.InvalidElementId)
        {
            templateName = (doc.GetElement(view.ViewTemplateId) as View)?.Name ?? string.Empty;
        }

        return new FilterViewSummary
        {
            ViewId = checked((int)view.Id.Value),
            ViewName = view.Name ?? string.Empty,
            ViewType = view.ViewType.ToString(),
            IsTemplate = view.IsTemplate,
            TemplateId = view.ViewTemplateId != ElementId.InvalidElementId
                ? checked((int)view.ViewTemplateId.Value)
                : 0,
            TemplateName = templateName
        };
    }

    private static FilterScopeAudit BuildScopeAudit(Document doc, View view, ViewAutomationService viewAutomation)
    {
        var audit = new FilterScopeAudit
        {
            View = BuildViewSummary(doc, view)
        };

        ICollection<ElementId> filterIds;
        try
        {
            filterIds = view.GetFilters();
        }
        catch
        {
            filterIds = new List<ElementId>();
        }

        foreach (var filterId in filterIds)
        {
            var inspect = viewAutomation.InspectFilter(doc, new InspectFilterRequest
            {
                FilterId = checked((int)filterId.Value),
                IncludeViewUsage = true,
                IncludeTemplateUsage = true
            });

            var scopeFilter = new ScopeFilterAudit
            {
                Filter = inspect,
                ScopeUsage = view.IsTemplate
                    ? inspect.TemplateUsages.Where(x => x.ViewId == checked((int)view.Id.Value)).ToList()
                    : inspect.ViewUsages.Where(x => x.ViewId == checked((int)view.Id.Value)).ToList()
            };

            audit.Filters.Add(scopeFilter);
        }

        return audit;
    }
}

[DataContract]
internal sealed class FilterAuditExport
{
    [DataMember(Order = 1)]
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 2)]
    public FilterDocumentSummary Document { get; set; } = new FilterDocumentSummary();

    [DataMember(Order = 3)]
    public FilterViewSummary ActiveView { get; set; } = new FilterViewSummary();

    [DataMember(Order = 4)]
    public FilterScopeAudit ActiveViewScope { get; set; } = new FilterScopeAudit();

    [DataMember(Order = 5)]
    public FilterViewSummary? TemplateView { get; set; }

    [DataMember(Order = 6)]
    public FilterScopeAudit? TemplateScope { get; set; }

    [DataMember(Order = 7)]
    public int TotalDocumentFilterCount { get; set; }

    [DataMember(Order = 8)]
    public List<ViewFilterSummary> AllDocumentFilters { get; set; } = new List<ViewFilterSummary>();
}

[DataContract]
internal sealed class FilterDocumentSummary
{
    [DataMember(Order = 1)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PathName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool IsFamilyDocument { get; set; }

    [DataMember(Order = 4)]
    public bool IsWorkshared { get; set; }
}

[DataContract]
internal sealed class FilterViewSummary
{
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsTemplate { get; set; }

    [DataMember(Order = 5)]
    public int TemplateId { get; set; }

    [DataMember(Order = 6)]
    public string TemplateName { get; set; } = string.Empty;
}

[DataContract]
internal sealed class FilterScopeAudit
{
    [DataMember(Order = 1)]
    public FilterViewSummary View { get; set; } = new FilterViewSummary();

    [DataMember(Order = 2)]
    public List<ScopeFilterAudit> Filters { get; set; } = new List<ScopeFilterAudit>();
}

[DataContract]
internal sealed class ScopeFilterAudit
{
    [DataMember(Order = 1)]
    public FilterInspectResult Filter { get; set; } = new FilterInspectResult();

    [DataMember(Order = 2)]
    public List<FilterUsageEntry> ScopeUsage { get; set; } = new List<FilterUsageEntry>();
}
