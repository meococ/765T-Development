using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Sheet & View management - authoring/documentation primitives.
/// Audit/QC registrations da duoc gom ve AuditCenterToolModule.
/// </summary>
internal sealed class SheetViewToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal SheetViewToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var sheetView = _context.SheetView;
        var analysis = _context.TemplateSheetAnalysis;
        var sheetRead = ToolManifestPresets.Read("document", "sheet");
        var viewRead = ToolManifestPresets.Read("document");
        var sheetMutation = ToolManifestPresets.Mutation("document", "sheet");
        var viewMutation = ToolManifestPresets.Mutation("document", "view");

        registry.Register(ToolNames.SheetListAll,
            "List all sheets in the document with optional viewport details.",
            PermissionLevel.Read, ApprovalRequirement.None, false, sheetRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SheetListRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, sheetView.ListSheets(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SheetNumberContains\":\"\",\"SheetNameContains\":\"\",\"IncludeViewports\":false,\"MaxResults\":500}");

        registry.Register(ToolNames.SheetGetViewportLayout,
            "Get viewport positions and view names for a specific sheet.",
            PermissionLevel.Read, ApprovalRequirement.None, false, sheetRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ViewportLayoutRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, sheetView.GetViewportLayout(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SheetId\":0,\"SheetNumber\":\"\"}");

        registry.Register(ToolNames.ViewListTemplates,
            "List all view templates with usage counts.",
            PermissionLevel.Read, ApprovalRequirement.None, false, viewRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ViewTemplateListRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, sheetView.ListViewTemplates(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"NameContains\":\"\",\"ViewType\":\"\",\"MaxResults\":500}");

        registry.RegisterMutationTool<CreateSheetRequest>(
            ToolNames.SheetCreateSafe,
            "Create a new sheet with title block, safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"SheetNumber\":\"A101\",\"SheetName\":\"Floor Plan\",\"TitleBlockTypeId\":null,\"TitleBlockTypeName\":\"\"}",
            ToolManifestPresets.Mutation("document"),
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => sheetView.PreviewCreateSheet(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => sheetView.ExecuteCreateSheet(uiapp, services, doc, payload));

        registry.RegisterMutationTool<RenumberSheetRequest>(
            ToolNames.SheetRenumberSafe,
            "Renumber and optionally rename a sheet safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"SheetId\":0,\"OldSheetNumber\":\"\",\"NewSheetNumber\":\"A102\",\"NewSheetName\":\"\"}",
            sheetMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => sheetView.PreviewRenumberSheet(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => sheetView.ExecuteRenumberSheet(uiapp, services, doc, payload));

        registry.RegisterMutationTool<PlaceViewsOnSheetRequest>(
            ToolNames.SheetPlaceViewsSafe,
            "Place one or more views on a sheet at specified positions, safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"SheetId\":0,\"Views\":[{\"ViewId\":0,\"CenterX\":1.0,\"CenterY\":0.5}]}",
            sheetMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => sheetView.PreviewPlaceViews(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => sheetView.ExecutePlaceViews(uiapp, services, doc, payload));

        registry.RegisterMutationTool<DuplicateViewRequest>(
            ToolNames.ViewDuplicateSafe,
            "Duplicate a view (Duplicate, WithDetailing, AsDependent) safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"ViewId\":0,\"NewName\":\"Plan - Level 1 Copy\",\"DuplicateMode\":\"Duplicate\",\"ActivateAfterCreate\":false}",
            viewMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => sheetView.PreviewDuplicateView(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => sheetView.ExecuteDuplicateView(uiapp, services, doc, payload));

        registry.RegisterMutationTool<CreateProjectViewRequest>(
            ToolNames.ViewCreateProjectViewSafe,
            "Create a project view (floor/ceiling/engineering plan) with optional template and scale, safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"ViewKind\":\"floor_plan\",\"Discipline\":\"architectural\",\"LevelId\":null,\"LevelName\":\"Level 1\",\"ViewName\":\"Architectural - Level 1 - Documentation\",\"TemplateId\":null,\"TemplateName\":\"BIM765T_Arch_Plan_v2\",\"ScaleValue\":100,\"ScaleText\":\"1:100\",\"ActivateAfterCreate\":false}",
            viewMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => sheetView.PreviewCreateProjectView(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => sheetView.ExecuteCreateProjectView(uiapp, services, doc, payload));

        registry.RegisterMutationTool<SetViewTemplateRequest>(
            ToolNames.ViewSetTemplateSafe,
            "Apply or remove a view template safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"ViewId\":0,\"TemplateId\":null,\"TemplateName\":\"\",\"RemoveTemplate\":false}",
            viewMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => sheetView.PreviewSetViewTemplate(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => sheetView.ExecuteSetViewTemplate(uiapp, services, doc, payload));

        registry.RegisterMutationTool<AlignViewportsRequest>(
            ToolNames.ViewportAlignSafe,
            "Align viewports on a sheet (CenterVertical, CenterHorizontal, Center) safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"SheetId\":0,\"AlignMode\":\"CenterVertical\",\"ViewportIds\":[],\"TargetX\":null,\"TargetY\":null}",
            sheetMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => sheetView.PreviewAlignViewports(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => sheetView.ExecuteAlignViewports(uiapp, services, doc, payload));

        registry.Register(ToolNames.ViewTemplateInspect,
            "Deep inspect a view template: filters, graphic overrides, controlled parameters, discipline, scale, detail level, view usage.",
            PermissionLevel.Read, ApprovalRequirement.None, false, ToolManifestPresets.Read("document").WithRiskTags("qc"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TemplateInspectRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, analysis.InspectTemplate(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"TemplateId\":null,\"TemplateName\":\"\",\"IncludeFilterDetails\":true,\"IncludeControlledParameters\":true,\"MaxViewSamples\":20}");

        registry.Register(ToolNames.SheetGroupSummary,
            "Detailed summary for a sheet group (by prefix or pattern): members, viewport counts, template usage, issues.",
            PermissionLevel.Read, ApprovalRequirement.None, false, ToolManifestPresets.Read("document", "sheet").WithRiskTags("qc"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SheetGroupDetailRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, analysis.GetSheetGroupDetail(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SheetNumberPrefix\":\"\",\"SheetNumberPattern\":\"\",\"IncludeTemplateUsage\":true,\"MaxResults\":100}");
    }
}
