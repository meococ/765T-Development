using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class ViewAnnotationAndTypeToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal ViewAnnotationAndTypeToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var mutation = _context.Mutation;
        var viewAutomation = _context.ViewAutomation;
        var typeCatalog = _context.TypeCatalog;
        var viewRead = ToolManifestPresets.Read("document");
        var viewMutation = ToolManifestPresets.Mutation("document", "view").WithTouchesActiveView();
        var annotationMutation = ToolManifestPresets.Mutation("document", "view").WithTouchesActiveView();
        var typeRead = ToolManifestPresets.Read("document");

        registry.RegisterMutationTool<Create3DViewRequest>(
            ToolNames.ViewCreate3dSafe,
            "Create a new 3D view safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"ViewName\":\"Coordination 3D\",\"UseActive3DOrientationWhenPossible\":true,\"CopySectionBoxFromActive3D\":true,\"FailIfExists\":true,\"DuplicateIfExists\":false,\"ActivateViewAfterCreate\":false}",
            viewMutation,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (uiapp, services, doc, payload, request) => viewAutomation.PreviewCreate3DView(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => viewAutomation.ExecuteCreate3DView(uiapp, services, doc, payload));

        registry.Register(ToolNames.ViewListFilters, "List all ParameterFilterElements in the document with category and rule summary.", PermissionLevel.Read, ApprovalRequirement.None, false, viewRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ListViewFiltersRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, viewAutomation.ListViewFilters(doc, payload));
            },
            "{\"DocumentKey\":\"\",\"NameContains\":\"\",\"IncludeCategoryNames\":true,\"IncludeRuleSummary\":true,\"MaxResults\":500}");

        registry.Register(ToolNames.ViewInspectFilter, "Get full detail of a single filter: categories, rules, and all views/templates using it.", PermissionLevel.Read, ApprovalRequirement.None, false, viewRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<InspectFilterRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, viewAutomation.InspectFilter(doc, payload));
            },
            "{\"FilterName\":\"BIM765T_Filter_EX\",\"IncludeViewUsage\":true,\"IncludeTemplateUsage\":true}");

        registry.RegisterMutationTool<RemoveFilterFromViewRequest>(
            ToolNames.ViewRemoveFilterFromViewSafe,
            "Remove a filter from a view safely with dry-run and confirm. Does not delete the filter element.",
            ApprovalRequirement.ConfirmToken,
            "{\"ViewName\":\"Coordination 3D\",\"FilterName\":\"BIM765T_Filter_EX\"}",
            viewMutation,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (uiapp, services, doc, payload, request) => viewAutomation.PreviewRemoveFilterFromView(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => viewAutomation.ExecuteRemoveFilterFromView(uiapp, services, doc, payload));

        registry.RegisterMutationTool<DeleteFilterRequest>(
            ToolNames.ViewDeleteFilterSafe,
            "Delete a ParameterFilterElement from the document safely. Preview shows usage across all views/templates.",
            ApprovalRequirement.ConfirmToken,
            "{\"FilterName\":\"BIM765T_Filter_EX\",\"ForceRemoveFromAllViews\":false}",
            viewMutation.WithRiskTags("delete"),
            () => platform.Settings.AllowDeleteTools,
            StatusCodes.DeleteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (uiapp, services, doc, payload, request) => viewAutomation.PreviewDeleteFilter(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => viewAutomation.ExecuteDeleteFilter(uiapp, services, doc, payload));

        registry.RegisterMutationTool<CreateOrUpdateViewFilterRequest>(
            ToolNames.ViewCreateOrUpdateFilterSafe,
            "Create or update a parameter filter safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"FilterName\":\"BIM765T_Filter_EX\",\"CategoryNames\":[\"Walls\"],\"Rules\":[{\"ParameterName\":\"Comments\",\"Operator\":\"contains\",\"Value\":\"EX\",\"CaseSensitive\":false}],\"OverwriteIfExists\":true,\"InferCategoriesFromSelectionWhenEmpty\":true}",
            viewMutation,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (uiapp, services, doc, payload, request) => viewAutomation.PreviewCreateOrUpdateViewFilter(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => viewAutomation.ExecuteCreateOrUpdateViewFilter(uiapp, services, doc, payload));
        registry.RegisterMutationTool<ApplyViewFilterRequest>(
            ToolNames.ViewApplyFilterSafe,
            "Apply an existing filter to a view safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"ViewName\":\"Coordination 3D\",\"FilterName\":\"BIM765T_Filter_EX\",\"Visible\":true,\"Halftone\":false,\"Transparency\":0,\"ProjectionLineColorRed\":255,\"ProjectionLineColorGreen\":0,\"ProjectionLineColorBlue\":0}",
            viewMutation,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (uiapp, services, doc, payload, request) => viewAutomation.PreviewApplyViewFilter(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => viewAutomation.ExecuteApplyViewFilter(uiapp, services, doc, payload));

        registry.RegisterMutationTool<AddTextNoteRequest>(
            ToolNames.AnnotationAddTextNoteSafe,
            "Safely add a TextNote to the active/target view with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            string.Empty,
            annotationMutation,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => mutation.PreviewAddTextNote(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteAddTextNote(services, doc, payload));
        registry.RegisterMutationTool<UpdateTextNoteStyleRequest>(
            ToolNames.AnnotationUpdateTextNoteStyleSafe,
            "Safely duplicate/reuse a text note type, shrink style, and recolor a specific text note without changing other notes.",
            ApprovalRequirement.ConfirmToken,
            "{\"TextNoteId\":0,\"TargetTypeName\":\"\",\"TextSizeValue\":\"1/8\\\"\",\"Red\":255,\"Green\":0,\"Blue\":0,\"DuplicateCurrentTypeIfNeeded\":true,\"ReuseMatchingExistingType\":true}",
            annotationMutation,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => mutation.PreviewUpdateTextNoteStyle(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteUpdateTextNoteStyle(services, doc, payload));
        registry.RegisterMutationTool<UpdateTextNoteContentRequest>(
            ToolNames.AnnotationUpdateTextNoteContentSafe,
            "Safely update the content of a specific text note with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"TextNoteId\":0,\"NewText\":\"xin chào\"}",
            annotationMutation,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => mutation.PreviewUpdateTextNoteContent(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteUpdateTextNoteContent(services, doc, payload));

        registry.Register(ToolNames.TypeListElementTypes, "List element types in the current/target document with usage counts.", PermissionLevel.Read, ApprovalRequirement.None, false, typeRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementTypeQueryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, typeCatalog.ListElementTypes(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[],\"ClassName\":\"\",\"NameContains\":\"\",\"IncludeParameters\":false,\"OnlyInUse\":false,\"MaxResults\":500}");
        registry.Register(ToolNames.AnnotationListTextNoteTypes, "List all TextNoteType definitions in the current/target document.", PermissionLevel.Read, ApprovalRequirement.None, false, typeRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementTypeQueryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, typeCatalog.ListTextNoteTypes(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"NameContains\":\"\",\"IncludeParameters\":true,\"OnlyInUse\":false,\"MaxResults\":500}");
        registry.Register(ToolNames.AnnotationGetTextTypeUsage, "List TextNoteType usage counts and sample note ids.", PermissionLevel.Read, ApprovalRequirement.None, false, typeRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TextNoteTypeUsageRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, typeCatalog.GetTextTypeUsage(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"NameContains\":\"\",\"OnlyInUse\":true,\"MaxResults\":200,\"MaxSampleTextNotesPerType\":10}");
    }
}

