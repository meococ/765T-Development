using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Sheet & View management — tạo sheet, duplicate view, viewport alignment.
/// Tất cả mutation đều đi qua dry-run → approval → execute flow.
/// </summary>
internal sealed class SheetViewManagementService
{
    // ── READ: List tất cả sheets trong document ──
    internal SheetListResponse ListSheets(PlatformServices services, Document doc, SheetListRequest request)
    {
        request ??= new SheetListRequest();
        var result = new SheetListResponse { DocumentKey = services.GetDocumentKey(doc) };

        IEnumerable<ViewSheet> sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(s => !s.IsPlaceholder);

        if (!string.IsNullOrWhiteSpace(request.SheetNumberContains))
            sheets = sheets.Where(s => (s.SheetNumber ?? string.Empty).IndexOf(request.SheetNumberContains, StringComparison.OrdinalIgnoreCase) >= 0);
        if (!string.IsNullOrWhiteSpace(request.SheetNameContains))
            sheets = sheets.Where(s => (s.Name ?? string.Empty).IndexOf(request.SheetNameContains, StringComparison.OrdinalIgnoreCase) >= 0);

        foreach (var sheet in sheets.OrderBy(s => s.SheetNumber ?? string.Empty).Take(Math.Max(1, request.MaxResults)))
        {
            var item = new SheetItem
            {
                Id = checked((int)sheet.Id.Value),
                SheetNumber = sheet.SheetNumber ?? string.Empty,
                SheetName = sheet.Name ?? string.Empty
            };

            var issuedBy = sheet.LookupParameter("Issued By");
            if (issuedBy != null) item.IssuedBy = issuedBy.AsValueString() ?? string.Empty;
            var issuedTo = sheet.LookupParameter("Issued To");
            if (issuedTo != null) item.IssuedTo = issuedTo.AsValueString() ?? string.Empty;

            // Always populate viewport count (lightweight — no viewport detail resolve)
            var vpIds = sheet.GetAllViewports();
            item.ViewportCount = vpIds.Count;

            // Title block name
            try
            {
                var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .ToElements();
                item.TitleBlockName = titleBlocks.Count > 0 ? (titleBlocks[0]?.Name ?? string.Empty) : string.Empty;
            }
            catch { /* view-specific collector can fail on corrupt sheets */ }

            // Current revision
            var revParam = sheet.LookupParameter("Current Revision");
            if (revParam != null) item.CurrentRevision = revParam.AsValueString() ?? string.Empty;

            if (request.IncludeViewports)
            {
                foreach (var vpId in vpIds)
                {
                    if (doc.GetElement(vpId) is Viewport vp)
                    {
                        var center = vp.GetBoxCenter();
                        var viewOnSheet = doc.GetElement(vp.ViewId);
                        item.Viewports.Add(new ViewportItem
                        {
                            ViewportId = checked((int)vp.Id.Value),
                            ViewId = checked((int)vp.ViewId.Value),
                            ViewName = viewOnSheet?.Name ?? string.Empty,
                            CenterX = Math.Round(center.X, 4),
                            CenterY = Math.Round(center.Y, 4)
                        });
                    }
                }
            }
            result.Sheets.Add(item);
        }
        result.Count = result.Sheets.Count;
        return result;
    }

    // ── READ: Viewport layout cho 1 sheet cụ thể ──
    internal ViewportLayoutResponse GetViewportLayout(PlatformServices services, Document doc, ViewportLayoutRequest request)
    {
        var result = new ViewportLayoutResponse { DocumentKey = services.GetDocumentKey(doc) };
        ViewSheet? sheet = null;

        if (request.SheetId > 0)
            sheet = doc.GetElement(new ElementId((long)request.SheetId)) as ViewSheet;
        if (sheet == null && !string.IsNullOrWhiteSpace(request.SheetNumber))
            sheet = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(s => string.Equals(s.SheetNumber, request.SheetNumber, StringComparison.OrdinalIgnoreCase));
        if (sheet == null) throw new InvalidOperationException("Sheet not found.");

        result.SheetId = checked((int)sheet.Id.Value);
        result.SheetNumber = sheet.SheetNumber ?? string.Empty;

        foreach (var vpId in sheet.GetAllViewports())
        {
            if (doc.GetElement(vpId) is Viewport vp)
            {
                var center = vp.GetBoxCenter();
                var viewOnSheet = doc.GetElement(vp.ViewId);
                result.Viewports.Add(new ViewportItem
                {
                    ViewportId = checked((int)vp.Id.Value),
                    ViewId = checked((int)vp.ViewId.Value),
                    ViewName = viewOnSheet?.Name ?? string.Empty,
                    CenterX = Math.Round(center.X, 4),
                    CenterY = Math.Round(center.Y, 4)
                });
            }
        }
        return result;
    }

    // ── READ: List view templates ──
    internal ViewTemplateListResponse ListViewTemplates(PlatformServices services, Document doc, ViewTemplateListRequest request)
    {
        request ??= new ViewTemplateListRequest();
        var result = new ViewTemplateListResponse { DocumentKey = services.GetDocumentKey(doc) };

        var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var templates = allViews.Where(v => v.IsTemplate).ToList();
        var nonTemplates = allViews.Where(v => !v.IsTemplate).ToList();

        // PERF: Pre-compute template usage map O(n) thay vì O(n*m)
        var templateUsage = nonTemplates
            .Where(v => v.ViewTemplateId != ElementId.InvalidElementId)
            .GroupBy(v => v.ViewTemplateId)
            .ToDictionary(g => g.Key, g => g.Count());

        if (!string.IsNullOrWhiteSpace(request.NameContains))
            templates = templates.Where(v => (v.Name ?? string.Empty).IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (!string.IsNullOrWhiteSpace(request.ViewType))
            templates = templates.Where(v => v.ViewType.ToString().IndexOf(request.ViewType, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        foreach (var tmpl in templates.OrderBy(v => v.Name ?? string.Empty).Take(Math.Max(1, request.MaxResults)))
        {
            var usageCount = templateUsage.TryGetValue(tmpl.Id, out var count) ? count : 0;
            var item = new ViewTemplateItem
            {
                Id = checked((int)tmpl.Id.Value),
                Name = tmpl.Name ?? string.Empty,
                ViewType = tmpl.ViewType.ToString(),
                UsageCount = usageCount
            };

            // Enhanced fields — Discipline, FilterCount, ControlledParameterCount, DetailLevel
            try
            {
                var disc = tmpl.Discipline;
                item.Discipline = disc.ToString();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* ViewType may not expose Discipline property */ }

            try { item.FilterCount = tmpl.GetFilters().Count; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* Filter API not available on this template type */ }

            try { item.ControlledParameterCount = tmpl.GetTemplateParameterIds().Count; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* Template does not support parameter control enumeration */ }

            try { item.DetailLevel = tmpl.DetailLevel.ToString(); }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* DetailLevel not applicable to this view template type */ }

            result.Templates.Add(item);
        }
        result.Count = result.Templates.Count;
        return result;
    }

    // ── MUTATION: Create sheet ──
    internal ExecutionResult PreviewCreateSheet(UIApplication uiapp, PlatformServices services, Document doc, CreateSheetRequest payload, ToolRequestEnvelope request)
    {
        var diags = new List<DiagnosticRecord>();
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .FirstOrDefault(s => string.Equals(s.SheetNumber, payload.SheetNumber, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            diags.Add(DiagnosticRecord.Create("SHEET_NUMBER_EXISTS", DiagnosticSeverity.Error, $"Sheet number '{payload.SheetNumber}' already exists (Id={existing.Id.Value})."));
        if (string.IsNullOrWhiteSpace(payload.SheetNumber))
            diags.Add(DiagnosticRecord.Create("SHEET_NUMBER_EMPTY", DiagnosticSeverity.Error, "SheetNumber is required."));

        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags,
            Artifacts = new List<string> { $"SheetNumber={payload.SheetNumber}", $"SheetName={payload.SheetName}" }
        };
    }

    internal ExecutionResult ExecuteCreateSheet(UIApplication uiapp, PlatformServices services, Document doc, CreateSheetRequest payload)
    {
        var diags = new List<DiagnosticRecord>();
        ElementId titleBlockId = ElementId.InvalidElementId;

        if (payload.TitleBlockTypeId.HasValue && payload.TitleBlockTypeId.Value > 0)
            titleBlockId = new ElementId((long)payload.TitleBlockTypeId.Value);
        else if (!string.IsNullOrWhiteSpace(payload.TitleBlockTypeName))
        {
            var tb = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>().FirstOrDefault(f => (f.Name ?? string.Empty).IndexOf(payload.TitleBlockTypeName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (tb != null) titleBlockId = tb.Id;
            else diags.Add(DiagnosticRecord.Create("TITLEBLOCK_NOT_FOUND", DiagnosticSeverity.Warning, $"Title block '{payload.TitleBlockTypeName}' not found, using default."));
        }

        using var group = new TransactionGroup(doc, "765TAgent::sheet.create_safe");
        group.Start();
        using var tx = new Transaction(doc, "Create sheet");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        var sheet = ViewSheet.Create(doc, titleBlockId);
        sheet.SheetNumber = payload.SheetNumber;
        sheet.Name = payload.SheetName;
        tx.Commit();

        var createdId = checked((int)sheet.Id.Value);
        var diff = new DiffSummary { CreatedIds = new List<int> { createdId } };

        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult
        {
            OperationName = "sheet.create_safe", DryRun = false, ChangedIds = new List<int> { createdId },
            DiffSummary = diff, Diagnostics = diags,
            Artifacts = new List<string> { $"CreatedSheetId={createdId}", $"SheetNumber={payload.SheetNumber}" }
        };
    }

    // ── MUTATION: Renumber sheet ──
    internal ExecutionResult PreviewRenumberSheet(UIApplication uiapp, PlatformServices services, Document doc, RenumberSheetRequest payload, ToolRequestEnvelope request)
    {
        var diags = new List<DiagnosticRecord>();
        var sheet = doc.GetElement(new ElementId((long)payload.SheetId)) as ViewSheet;
        if (sheet == null) diags.Add(DiagnosticRecord.Create("SHEET_NOT_FOUND", DiagnosticSeverity.Error, $"Sheet Id={payload.SheetId} not found."));

        var conflict = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .FirstOrDefault(s => string.Equals(s.SheetNumber, payload.NewSheetNumber, StringComparison.OrdinalIgnoreCase) && s.Id.Value != payload.SheetId);
        if (conflict != null)
            diags.Add(DiagnosticRecord.Create("SHEET_NUMBER_CONFLICT", DiagnosticSeverity.Error, $"Sheet number '{payload.NewSheetNumber}' already used by sheet Id={conflict.Id.Value}."));

        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags, ChangedIds = new List<int> { payload.SheetId },
            Artifacts = new List<string> { $"OldNumber={payload.OldSheetNumber}", $"NewNumber={payload.NewSheetNumber}" }
        };
    }

    internal ExecutionResult ExecuteRenumberSheet(UIApplication uiapp, PlatformServices services, Document doc, RenumberSheetRequest payload)
    {
        var diags = new List<DiagnosticRecord>();
        var sheet = doc.GetElement(new ElementId((long)payload.SheetId)) as ViewSheet
            ?? throw new InvalidOperationException($"Sheet Id={payload.SheetId} not found.");

        using var group = new TransactionGroup(doc, "765TAgent::sheet.renumber_safe");
        group.Start();
        using var tx = new Transaction(doc, "Renumber sheet");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        var oldNumber = sheet.SheetNumber;
        sheet.SheetNumber = payload.NewSheetNumber;
        if (!string.IsNullOrWhiteSpace(payload.NewSheetName)) sheet.Name = payload.NewSheetName;
        tx.Commit();

        var diff = new DiffSummary
        {
            ModifiedIds = new List<int> { payload.SheetId },
            ParameterChanges = new List<ParameterChangeRecord>
            {
                new ParameterChangeRecord { ElementId = payload.SheetId, ParameterName = "Sheet Number", BeforeValue = oldNumber, AfterValue = payload.NewSheetNumber }
            }
        };

        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult { OperationName = "sheet.renumber_safe", DryRun = false, ChangedIds = new List<int> { payload.SheetId }, DiffSummary = diff, Diagnostics = diags };
    }

    // ── MUTATION: Place views on sheet ──
    internal ExecutionResult PreviewPlaceViews(UIApplication uiapp, PlatformServices services, Document doc, PlaceViewsOnSheetRequest payload, ToolRequestEnvelope request)
    {
        var diags = new List<DiagnosticRecord>();
        var sheet = doc.GetElement(new ElementId((long)payload.SheetId)) as ViewSheet;
        if (sheet == null) diags.Add(DiagnosticRecord.Create("SHEET_NOT_FOUND", DiagnosticSeverity.Error, $"Sheet Id={payload.SheetId} not found."));

        foreach (var vp in payload.Views)
        {
            var view = doc.GetElement(new ElementId((long)vp.ViewId)) as View;
            if (view == null) diags.Add(DiagnosticRecord.Create("VIEW_NOT_FOUND", DiagnosticSeverity.Error, $"View Id={vp.ViewId} not found."));
            else if (view.IsTemplate) diags.Add(DiagnosticRecord.Create("VIEW_IS_TEMPLATE", DiagnosticSeverity.Error, $"View '{view.Name}' is a template, cannot place on sheet."));
        }

        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags, ChangedIds = new List<int> { payload.SheetId },
            Artifacts = new List<string> { $"SheetId={payload.SheetId}", $"ViewCount={payload.Views.Count}" }
        };
    }

    internal ExecutionResult ExecutePlaceViews(UIApplication uiapp, PlatformServices services, Document doc, PlaceViewsOnSheetRequest payload)
    {
        var diags = new List<DiagnosticRecord>();
        var sheet = doc.GetElement(new ElementId((long)payload.SheetId)) as ViewSheet
            ?? throw new InvalidOperationException($"Sheet Id={payload.SheetId} not found.");
        var createdIds = new List<int>();

        using var group = new TransactionGroup(doc, "765TAgent::sheet.place_views_safe");
        group.Start();
        using var tx = new Transaction(doc, "Place views on sheet");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        foreach (var vp in payload.Views)
        {
            try
            {
                var viewport = Viewport.Create(doc, sheet.Id, new ElementId((long)vp.ViewId), new XYZ(vp.CenterX, vp.CenterY, 0));
                createdIds.Add(checked((int)viewport.Id.Value));
            }
            catch (Exception ex)
            {
                diags.Add(DiagnosticRecord.Create("VIEWPORT_CREATE_FAILED", DiagnosticSeverity.Error, $"Failed to place view Id={vp.ViewId}: {ex.Message}"));
            }
        }
        tx.Commit();

        var diff = new DiffSummary { CreatedIds = createdIds };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult { OperationName = "sheet.place_views_safe", DryRun = false, ChangedIds = createdIds, DiffSummary = diff, Diagnostics = diags };
    }

    // ── MUTATION: Duplicate view ──
    internal ExecutionResult PreviewDuplicateView(UIApplication uiapp, PlatformServices services, Document doc, DuplicateViewRequest payload, ToolRequestEnvelope request)
    {
        var diags = new List<DiagnosticRecord>();
        var view = doc.GetElement(new ElementId((long)payload.ViewId)) as View;
        if (view == null) diags.Add(DiagnosticRecord.Create("VIEW_NOT_FOUND", DiagnosticSeverity.Error, $"View Id={payload.ViewId} not found."));
        if (view != null && !view.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            diags.Add(DiagnosticRecord.Create("VIEW_CANNOT_DUPLICATE", DiagnosticSeverity.Error, $"View '{view.Name}' cannot be duplicated."));

        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags, ChangedIds = new List<int> { payload.ViewId },
            Artifacts = new List<string> { $"SourceView={view?.Name ?? "?"}", $"NewName={payload.NewName}", $"Mode={payload.DuplicateMode}" }
        };
    }

    internal ExecutionResult ExecuteDuplicateView(UIApplication uiapp, PlatformServices services, Document doc, DuplicateViewRequest payload)
    {
        var diags = new List<DiagnosticRecord>();
        var view = doc.GetElement(new ElementId((long)payload.ViewId)) as View
            ?? throw new InvalidOperationException($"View Id={payload.ViewId} not found.");

        var dupOption = payload.DuplicateMode?.ToLowerInvariant() switch
        {
            "withdetailing" => ViewDuplicateOption.WithDetailing,
            "asadependent" => ViewDuplicateOption.AsDependent,
            _ => ViewDuplicateOption.Duplicate
        };

        using var group = new TransactionGroup(doc, "765TAgent::view.duplicate_safe");
        group.Start();
        using var tx = new Transaction(doc, "Duplicate view");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        var newViewId = view.Duplicate(dupOption);
        var newView = doc.GetElement(newViewId) as View;
        if (newView != null && !string.IsNullOrWhiteSpace(payload.NewName))
            newView.Name = payload.NewName;
        tx.Commit();

        if (payload.ActivateAfterCreate && newView != null)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc != null) uidoc.ActiveView = newView;
        }

        var createdId = checked((int)newViewId.Value);
        var diff = new DiffSummary { CreatedIds = new List<int> { createdId } };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult
        {
            OperationName = "view.duplicate_safe", DryRun = false, ChangedIds = new List<int> { createdId }, DiffSummary = diff, Diagnostics = diags,
            Artifacts = new List<string> { $"NewViewId={createdId}", $"NewName={newView?.Name ?? payload.NewName}" }
        };
    }

    // ── MUTATION: Set view template ──
    internal ExecutionResult PreviewCreateProjectView(UIApplication uiapp, PlatformServices services, Document doc, CreateProjectViewRequest payload, ToolRequestEnvelope request)
    {
        var plan = BuildProjectViewPlan(doc, payload);
        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = plan.Diagnostics,
            Artifacts = new List<string>
            {
                $"ViewKind={plan.ViewKind}",
                $"Level={plan.LevelName}",
                $"ViewName={plan.ViewName}",
                $"Template={plan.TemplateName}",
                $"Scale={plan.ScaleText}"
            }
        };
    }

    internal ExecutionResult ExecuteCreateProjectView(UIApplication uiapp, PlatformServices services, Document doc, CreateProjectViewRequest payload)
    {
        var plan = BuildProjectViewPlan(doc, payload);
        if (plan.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(plan.Diagnostics.First(x => x.Severity == DiagnosticSeverity.Error).Message);
        }

        using var group = new TransactionGroup(doc, "765TAgent::view.create_project_view_safe");
        group.Start();
        using var tx = new Transaction(doc, "Create project view");
        tx.Start();
        AgentFailureHandling.Configure(tx, plan.Diagnostics);

        View createdView = CreateProjectView(doc, plan);
        createdView.Name = plan.ViewName;
        if (plan.TemplateId != ElementId.InvalidElementId)
        {
            createdView.ViewTemplateId = plan.TemplateId;
        }

        if (plan.ScaleValue > 0)
        {
            try
            {
                createdView.Scale = plan.ScaleValue;
            }
            catch (Exception ex)
            {
                plan.Diagnostics.Add(DiagnosticRecord.Create("VIEW_SCALE_SET_FAILED", DiagnosticSeverity.Warning, $"Cannot set scale on '{plan.ViewName}': {ex.Message}"));
            }
        }

        tx.Commit();

        if (payload.ActivateAfterCreate && uiapp.ActiveUIDocument != null)
        {
            uiapp.ActiveUIDocument.ActiveView = createdView;
        }

        var createdId = checked((int)createdView.Id.Value);
        var diff = new DiffSummary { CreatedIds = new List<int> { createdId } };
        if (plan.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        return new ExecutionResult
        {
            OperationName = "view.create_project_view_safe",
            DryRun = false,
            ChangedIds = new List<int> { createdId },
            DiffSummary = diff,
            Diagnostics = plan.Diagnostics,
            Artifacts = new List<string>
            {
                $"CreatedViewId={createdId}",
                $"ViewName={createdView.Name}",
                $"Level={plan.LevelName}",
                $"Template={plan.TemplateName}",
                $"Scale={plan.ScaleText}"
            }
        };
    }

    // ?????? MUTATION: Set view template ??????
    internal ExecutionResult PreviewSetViewTemplate(UIApplication uiapp, PlatformServices services, Document doc, SetViewTemplateRequest payload, ToolRequestEnvelope request)
    {
        var diags = new List<DiagnosticRecord>();
        var view = doc.GetElement(new ElementId((long)payload.ViewId)) as View;
        if (view == null) diags.Add(DiagnosticRecord.Create("VIEW_NOT_FOUND", DiagnosticSeverity.Error, $"View Id={payload.ViewId} not found."));

        if (!payload.RemoveTemplate && payload.TemplateId == null && !string.IsNullOrWhiteSpace(payload.TemplateName))
        {
            var tmpl = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, payload.TemplateName, StringComparison.OrdinalIgnoreCase));
            if (tmpl == null) diags.Add(DiagnosticRecord.Create("TEMPLATE_NOT_FOUND", DiagnosticSeverity.Error, $"Template '{payload.TemplateName}' not found."));
        }

        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags, ChangedIds = new List<int> { payload.ViewId }
        };
    }

    internal ExecutionResult ExecuteSetViewTemplate(UIApplication uiapp, PlatformServices services, Document doc, SetViewTemplateRequest payload)
    {
        var diags = new List<DiagnosticRecord>();
        var view = doc.GetElement(new ElementId((long)payload.ViewId)) as View
            ?? throw new InvalidOperationException($"View Id={payload.ViewId} not found.");

        using var group = new TransactionGroup(doc, "765TAgent::view.set_template_safe");
        group.Start();
        using var tx = new Transaction(doc, "Set view template");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        var oldTemplateId = view.ViewTemplateId;
        if (payload.RemoveTemplate)
        {
            view.ViewTemplateId = ElementId.InvalidElementId;
        }
        else
        {
            ElementId tmplId = ElementId.InvalidElementId;
            if (payload.TemplateId.HasValue && payload.TemplateId.Value > 0)
                tmplId = new ElementId((long)payload.TemplateId.Value);
            else if (!string.IsNullOrWhiteSpace(payload.TemplateName))
            {
                var tmpl = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, payload.TemplateName, StringComparison.OrdinalIgnoreCase));
                if (tmpl != null) tmplId = tmpl.Id;
            }
            if (tmplId != ElementId.InvalidElementId) view.ViewTemplateId = tmplId;
        }
        tx.Commit();

        var diff = new DiffSummary { ModifiedIds = new List<int> { payload.ViewId } };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult { OperationName = "view.set_template_safe", DryRun = false, ChangedIds = new List<int> { payload.ViewId }, DiffSummary = diff, Diagnostics = diags };
    }

    // ── MUTATION: Align viewports ──
    internal ExecutionResult PreviewAlignViewports(UIApplication uiapp, PlatformServices services, Document doc, AlignViewportsRequest payload, ToolRequestEnvelope request)
    {
        var diags = new List<DiagnosticRecord>();
        var sheet = doc.GetElement(new ElementId((long)payload.SheetId)) as ViewSheet;
        if (sheet == null) diags.Add(DiagnosticRecord.Create("SHEET_NOT_FOUND", DiagnosticSeverity.Error, $"Sheet Id={payload.SheetId} not found."));

        foreach (var vpId in payload.ViewportIds)
        {
            if (!(doc.GetElement(new ElementId((long)vpId)) is Viewport))
                diags.Add(DiagnosticRecord.Create("VIEWPORT_NOT_FOUND", DiagnosticSeverity.Error, $"Viewport Id={vpId} not found."));
        }

        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags, ChangedIds = payload.ViewportIds,
            Artifacts = new List<string> { $"AlignMode={payload.AlignMode}", $"ViewportCount={payload.ViewportIds.Count}" }
        };
    }

    internal ExecutionResult ExecuteAlignViewports(UIApplication uiapp, PlatformServices services, Document doc, AlignViewportsRequest payload)
    {
        var diags = new List<DiagnosticRecord>();
        using var group = new TransactionGroup(doc, "765TAgent::viewport.align_safe");
        group.Start();
        using var tx = new Transaction(doc, "Align viewports");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        var viewports = payload.ViewportIds
            .Select(id => doc.GetElement(new ElementId((long)id)) as Viewport)
            .Where(vp => vp != null)
            .ToList();

        if (viewports.Count > 0)
        {
            double targetX = payload.TargetX ?? viewports.Average(vp => vp!.GetBoxCenter().X);
            double targetY = payload.TargetY ?? viewports.Average(vp => vp!.GetBoxCenter().Y);

            foreach (var vp in viewports)
            {
                var center = vp!.GetBoxCenter();
                var newCenter = payload.AlignMode?.ToLowerInvariant() switch
                {
                    "centervertical" => new XYZ(targetX, center.Y, 0),
                    "centerhorizontal" => new XYZ(center.X, targetY, 0),
                    "center" => new XYZ(targetX, targetY, 0),
                    _ => new XYZ(targetX, center.Y, 0)
                };
                vp.SetBoxCenter(newCenter);
            }
        }
        tx.Commit();

        var diff = new DiffSummary { ModifiedIds = payload.ViewportIds };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult { OperationName = "viewport.align_safe", DryRun = false, ChangedIds = payload.ViewportIds, DiffSummary = diff, Diagnostics = diags };
    }
    private static View CreateProjectView(Document doc, ProjectViewPlan plan)
    {
        switch (plan.ViewFamily)
        {
            case ViewFamily.FloorPlan:
            case ViewFamily.CeilingPlan:
                return ViewPlan.Create(doc, plan.ViewFamilyTypeId, plan.LevelId);
            default:
                throw new InvalidOperationException($"ViewKind '{plan.ViewKind}' is not supported by view.create_project_view_safe yet.");
        }
    }

    private static ProjectViewPlan BuildProjectViewPlan(Document doc, CreateProjectViewRequest payload)
    {
        payload ??= new CreateProjectViewRequest();
        var diagnostics = new List<DiagnosticRecord>();
        var normalizedViewKind = (payload.ViewKind ?? string.Empty).Trim().ToLowerInvariant();
        var family = ResolveViewFamily(normalizedViewKind, diagnostics);
        var level = ResolveLevel(doc, payload, family, diagnostics);
        var template = ResolveTemplate(doc, payload, diagnostics);
        var scaleValue = ResolveScale(payload, diagnostics);
        var viewName = (payload.ViewName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(viewName))
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, viewName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                diagnostics.Add(DiagnosticRecord.Create("VIEW_NAME_EXISTS", DiagnosticSeverity.Error, $"View '{viewName}' already exists (Id={existing.Id.Value}).", checked((int)existing.Id.Value)));
            }
        }

        var viewFamilyTypeId = ElementId.InvalidElementId;
        if (family.HasValue)
        {
            var viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == family.Value);
            if (viewFamilyType == null)
            {
                diagnostics.Add(DiagnosticRecord.Create("VIEW_FAMILY_TYPE_NOT_FOUND", DiagnosticSeverity.Error, $"No ViewFamilyType found for '{normalizedViewKind}'."));
            }
            else
            {
                viewFamilyTypeId = viewFamilyType.Id;
            }
        }

        return new ProjectViewPlan
        {
            Diagnostics = diagnostics,
            ViewKind = normalizedViewKind,
            ViewName = viewName,
            ViewFamily = family ?? ViewFamily.Invalid,
            ViewFamilyTypeId = viewFamilyTypeId,
            LevelId = level?.Id ?? ElementId.InvalidElementId,
            LevelName = level?.Name ?? payload.LevelName ?? string.Empty,
            TemplateId = template?.Id ?? ElementId.InvalidElementId,
            TemplateName = template?.Name ?? payload.TemplateName ?? string.Empty,
            ScaleValue = scaleValue,
            ScaleText = !string.IsNullOrWhiteSpace(payload.ScaleText) ? payload.ScaleText : (scaleValue > 0 ? $"1:{scaleValue}" : string.Empty)
        };
    }

    private static ViewFamily? ResolveViewFamily(string normalizedViewKind, ICollection<DiagnosticRecord> diagnostics)
    {
        switch (normalizedViewKind)
        {
            case "floor_plan":
            case "architectural_plan":
                return ViewFamily.FloorPlan;
            case "ceiling_plan":
                return ViewFamily.CeilingPlan;
            case "engineering_plan":
            case "mep_plan":
                return ViewFamily.FloorPlan;
            default:
                diagnostics.Add(DiagnosticRecord.Create("VIEW_KIND_UNSUPPORTED", DiagnosticSeverity.Error, $"ViewKind '{normalizedViewKind}' is not supported yet by view.create_project_view_safe."));
                return null;
        }
    }

    private static Level? ResolveLevel(Document doc, CreateProjectViewRequest payload, ViewFamily? family, ICollection<DiagnosticRecord> diagnostics)
    {
        if (family != ViewFamily.FloorPlan && family != ViewFamily.CeilingPlan)
        {
            return null;
        }

        Level? level = null;
        if (payload.LevelId.HasValue && payload.LevelId.Value > 0)
        {
            level = doc.GetElement(new ElementId((long)payload.LevelId.Value)) as Level;
        }

        if (level == null && !string.IsNullOrWhiteSpace(payload.LevelName))
        {
            level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(x => string.Equals(x.Name, payload.LevelName, StringComparison.OrdinalIgnoreCase))
                ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(x => x.Name.IndexOf(payload.LevelName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (level == null)
        {
            diagnostics.Add(DiagnosticRecord.Create("LEVEL_NOT_FOUND", DiagnosticSeverity.Error, $"Cannot resolve level '{payload.LevelName}' for project view creation."));
        }

        return level;
    }

    private static View? ResolveTemplate(Document doc, CreateProjectViewRequest payload, ICollection<DiagnosticRecord> diagnostics)
    {
        if (payload.TemplateId.HasValue && payload.TemplateId.Value > 0)
        {
            var templateById = doc.GetElement(new ElementId((long)payload.TemplateId.Value)) as View;
            if (templateById == null || !templateById.IsTemplate)
            {
                diagnostics.Add(DiagnosticRecord.Create("TEMPLATE_NOT_FOUND", DiagnosticSeverity.Error, $"Template Id={payload.TemplateId.Value} was not found or is not a template."));
                return null;
            }

            return templateById;
        }

        if (string.IsNullOrWhiteSpace(payload.TemplateName))
        {
            return null;
        }

        var template = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(x => x.IsTemplate && string.Equals(x.Name, payload.TemplateName, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            diagnostics.Add(DiagnosticRecord.Create("TEMPLATE_NOT_FOUND", DiagnosticSeverity.Error, $"Template '{payload.TemplateName}' was not found."));
        }

        return template;
    }

    private static int ResolveScale(CreateProjectViewRequest payload, ICollection<DiagnosticRecord> diagnostics)
    {
        if (payload.ScaleValue.HasValue)
        {
            if (payload.ScaleValue.Value <= 0)
            {
                diagnostics.Add(DiagnosticRecord.Create("VIEW_SCALE_INVALID", DiagnosticSeverity.Error, "ScaleValue must be > 0."));
                return 0;
            }

            return payload.ScaleValue.Value;
        }

        if (string.IsNullOrWhiteSpace(payload.ScaleText))
        {
            return 0;
        }

        var scaleText = payload.ScaleText.Trim();
        var colonIndex = scaleText.IndexOf(':');
        var numericPart = colonIndex >= 0 ? scaleText.Substring(colonIndex + 1) : scaleText;
        if (int.TryParse(numericPart, out var value) && value > 0)
        {
            return value;
        }

        diagnostics.Add(DiagnosticRecord.Create("VIEW_SCALE_INVALID", DiagnosticSeverity.Error, $"Cannot parse scale '{payload.ScaleText}'."));
        return 0;
    }

    private sealed class ProjectViewPlan
    {
        internal List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
        internal string ViewKind { get; set; } = string.Empty;
        internal string ViewName { get; set; } = string.Empty;
        internal ViewFamily ViewFamily { get; set; } = ViewFamily.Invalid;
        internal ElementId ViewFamilyTypeId { get; set; } = ElementId.InvalidElementId;
        internal ElementId LevelId { get; set; } = ElementId.InvalidElementId;
        internal string LevelName { get; set; } = string.Empty;
        internal ElementId TemplateId { get; set; } = ElementId.InvalidElementId;
        internal string TemplateName { get; set; } = string.Empty;
        internal int ScaleValue { get; set; }
        internal string ScaleText { get; set; } = string.Empty;
    }

}
