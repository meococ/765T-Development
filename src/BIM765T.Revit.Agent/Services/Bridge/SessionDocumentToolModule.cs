using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class SessionDocumentToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal SessionDocumentToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var fileLifecycle = _context.FileLifecycle;
        var sessionRead = ToolManifestPresets.Read();
        var documentRead = ToolManifestPresets.Read("document");
        var documentViewRead = ToolManifestPresets.Read("document", "view").WithTouchesActiveView();
        var fileLifecycleDocument = ToolManifestPresets.FileLifecycle("document");

        registry.Register(ToolNames.SessionListTools, "List tool manifests/capabilities.", PermissionLevel.Read, ApprovalRequirement.None, false, sessionRead,
            (uiapp, request) =>
            {
                var audience = ResolveCatalogAudience(request);
                return ToolResponses.Success(request, new ToolCatalogResponse { Tools = registry.GetToolCatalog(audience) });
            });
        registry.Register(ToolNames.SessionGetCapabilities, "Get bridge runtime capabilities.", PermissionLevel.Read, ApprovalRequirement.None, false, sessionRead,
            (uiapp, request) =>
            {
                var audience = ResolveCatalogAudience(request);
                return ToolResponses.Success(request, platform.GetCapabilities(registry.GetToolCatalog(audience)));
            });
        registry.Register(ToolNames.SessionListOpenDocuments, "List all open Revit documents.", PermissionLevel.Read, ApprovalRequirement.None, false, sessionRead,
            (uiapp, request) => ToolResponses.Success(request, platform.ListOpenDocuments(uiapp)));
        registry.Register(ToolNames.SessionGetRecentEvents, "Get recent tracked Revit events.", PermissionLevel.Read, ApprovalRequirement.None, false, sessionRead,
            (uiapp, request) => ToolResponses.Success(request, new RecentEventsResponse { Events = platform.EventIndex.GetRecent() }));
        registry.Register(ToolNames.SessionGetRecentOperations, "Get recent operation journal entries.", PermissionLevel.Read, ApprovalRequirement.None, false, sessionRead,
            (uiapp, request) => ToolResponses.Success(request, new RecentOperationsResponse { Operations = platform.Journal.GetRecent() }));
        registry.Register(ToolNames.SessionGetTaskContext, "Get active task context bundle: document, view, selection, history, capabilities.", PermissionLevel.Read, ApprovalRequirement.None, false, documentViewRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskContextRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, platform.GetTaskContext(uiapp, doc, payload, registry.GetToolCatalog(ToolCatalogFilter.ToolCatalogAudience.WorkerUi)));
            },
            "{\"DocumentKey\":\"\",\"MaxRecentOperations\":10,\"MaxRecentEvents\":10,\"IncludeCapabilities\":true,\"IncludeToolCatalog\":true}");

        registry.Register(ToolNames.DocumentGetActive, "Get active document summary.", PermissionLevel.Read, ApprovalRequirement.None, false, documentRead,
            (uiapp, request) => ToolResponses.Success(request, platform.SummarizeDocument(uiapp, platform.ResolveDocument(uiapp, string.Empty))));
        registry.Register(ToolNames.DocumentGetMetadata, "Get document metadata by target document.", PermissionLevel.Read, ApprovalRequirement.None, false, documentRead,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                return ToolResponses.Success(request, platform.SummarizeDocument(uiapp, doc));
            });
        registry.Register(ToolNames.DocumentGetContextFingerprint, "Get context fingerprint for confirmable operations.", PermissionLevel.Read, ApprovalRequirement.None, false, documentRead,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                return ToolResponses.Success(request, platform.BuildContextFingerprint(uiapp, doc));
            });
        registry.Register(ToolNames.DocumentOpenBackgroundRead, "Open a non-active document in read-first mode.", PermissionLevel.Read, ApprovalRequirement.None, false, sessionRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<OpenBackgroundDocumentRequest>(request);
                return ToolResponses.Success(request, fileLifecycle.OpenBackgroundRead(uiapp, platform, payload));
            });
        registry.Register(ToolNames.DocumentCloseNonActive, "Close a non-active open document with confirm token.", PermissionLevel.FileLifecycle, ApprovalRequirement.HighRiskToken, true, fileLifecycleDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSaveTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.FileOperationBlocked);
                }

                var payload = ToolPayloads.Read<CloseDocumentRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var preview = fileLifecycle.PreviewCloseNonActive(platform, doc, request);
                    return ToolResponses.ConfirmationRequired(request, platform.FinalizePreviewResult(uiapp, doc, request, preview));
                }

                if (!platform.MatchesExpectedContextStrict(uiapp, doc, request.ExpectedContextJson))
                {
                    return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                }

                var approval = platform.ValidateApprovalRequest(uiapp, doc, request);
                if (!string.Equals(approval, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))
                {
                    return ToolResponses.Failure(request, approval);
                }

                return ToolResponses.FromExecutionResult(request, fileLifecycle.CloseNonActive(uiapp, platform, doc, payload));
            });

        registry.Register(ToolNames.ViewGetActiveContext, "Get active view context.", PermissionLevel.Read, ApprovalRequirement.None, false, documentViewRead,
            (uiapp, request) => ToolResponses.Success(request, platform.GetActiveViewContext(uiapp)));
        registry.Register(ToolNames.ViewGetCurrentLevelContext, "Get current level context.", PermissionLevel.Read, ApprovalRequirement.None, false, documentViewRead,
            (uiapp, request) => ToolResponses.Success(request, platform.GetActiveViewContext(uiapp)));
        registry.Register(ToolNames.SelectionGet, "Get current selection summary.", PermissionLevel.Read, ApprovalRequirement.None, false, documentViewRead,
            (uiapp, request) => ToolResponses.Success(request, platform.GetSelection(uiapp)));
    }

    private static ToolCatalogFilter.ToolCatalogAudience ResolveCatalogAudience(ToolRequestEnvelope request)
    {
        var payload = ToolPayloads.Read<ToolCatalogRequest>(request);
        var audience = payload.Audience ?? string.Empty;
        return audience.ToLowerInvariant() switch
        {
            ToolCatalogAudiences.Mcp => ToolCatalogFilter.ToolCatalogAudience.Mcp,
            ToolCatalogAudiences.PublicCatalog => ToolCatalogFilter.ToolCatalogAudience.PublicCatalog,
            _ => ToolCatalogFilter.ToolCatalogAudience.WorkerUi
        };
    }
}
