using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Hull;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class MutationFileAndDomainToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal MutationFileAndDomainToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var mutation = _context.Mutation;
        var fileLifecycle = _context.FileLifecycle;
        var hullCollector = _context.HullCollector;
        var hullPlanner = _context.HullPlanner;
        var hullValidator = _context.HullValidator;
        var mutationDocument = ToolManifestPresets.Mutation("document");
        var batchMutationDocument = mutationDocument.WithBatchMode("chunked");
        var fileLifecycleDocument = ToolManifestPresets.FileLifecycle("document");
        var reviewDocument = ToolManifestPresets.Review("document");

        registry.RegisterMutationTool<SetParametersRequest>(
            ToolNames.ParameterSetSafe,
            "Safely set parameter values with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            string.Empty,
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            (doc, payload) => payload.DocumentKey = string.IsNullOrWhiteSpace(payload.DocumentKey) ? platform.GetDocumentKey(doc) : payload.DocumentKey,
            (_, services, doc, payload, request) => mutation.PreviewSetParameters(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteSetParameters(services, doc, payload));
        registry.RegisterMutationTool<SetParametersRequest>(
            ToolNames.BatchSetParameters,
            "Batch-safe alias for setting multiple parameter values in one guarded workflow.",
            ApprovalRequirement.ConfirmToken,
            string.Empty,
            batchMutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            (doc, payload) => payload.DocumentKey = string.IsNullOrWhiteSpace(payload.DocumentKey) ? platform.GetDocumentKey(doc) : payload.DocumentKey,
            (_, services, doc, payload, request) => mutation.PreviewSetParameters(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteSetParameters(services, doc, payload));
        registry.Register(
            ToolNames.ElementDeleteSafe,
            "Safely delete elements with dry-run, dependency preview, and confirm.",
            PermissionLevel.Mutate,
            ApprovalRequirement.HighRiskToken,
            true,
            mutationDocument.WithRiskTags("delete"),
            (uiapp, request) =>
            {
                if (!(platform.Settings.AllowWriteTools && platform.Settings.AllowDeleteTools))
                {
                    return ToolResponses.Failure(request, StatusCodes.WriteDisabled);
                }

                var payload = ToolPayloads.Read<DeleteElementsRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);

                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var previewResult = mutation.PreviewDelete(platform, doc, payload, request);
                    return ToolResponses.ConfirmationRequired(request, platform.FinalizePreviewResult(uiapp, doc, request, previewResult));
                }

                if (!platform.MatchesExpectedContextStrict(uiapp, doc, request.ExpectedContextJson))
                {
                    return ToolResponses.Failure(request, StatusCodes.ContextMismatch,
                        "Delete tools yêu cầu expected_context khi execute. Gọi dry-run trước để lấy fingerprint.");
                }

                var approval = platform.ValidateApprovalRequest(uiapp, doc, request);
                if (!string.Equals(approval, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))
                {
                    return ToolResponses.Failure(request, approval);
                }

                var impact = mutation.AnalyzeDeleteImpact(doc, payload.ElementIds);
                if (impact.HasUnexpectedDependents && !payload.AllowDependentDeletes)
                {
                    return new ToolResponseEnvelope
                    {
                        RequestId = request.RequestId,
                        ToolName = request.ToolName,
                        CorrelationId = request.CorrelationId,
                        Succeeded = false,
                        StatusCode = StatusCodes.DeleteDependencyBlocked,
                        Diagnostics = impact.Diagnostics,
                        Artifacts = impact.Artifacts,
                        ExecutedAtUtc = DateTime.UtcNow
                    };
                }

                return ToolResponses.FromExecutionResult(request, mutation.ExecuteDelete(platform, doc, payload));
            });
        registry.RegisterMutationTool<MoveElementsRequest>(
            ToolNames.ElementMoveSafe,
            "Safely move elements with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            string.Empty,
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => mutation.PreviewMoveElements(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteMoveElements(services, doc, payload));
        registry.RegisterMutationTool<MoveElementsRequest>(
            ToolNames.BatchMoveElements,
            "Batch-safe alias for moving multiple elements in one guarded workflow.",
            ApprovalRequirement.ConfirmToken,
            string.Empty,
            batchMutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => mutation.PreviewMoveElements(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteMoveElements(services, doc, payload));
        registry.RegisterMutationTool<RotateElementsRequest>(
            ToolNames.ElementRotateSafe,
            "Safely rotate elements with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            string.Empty,
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => mutation.PreviewRotateElements(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecuteRotateElements(services, doc, payload));
        registry.RegisterMutationTool<PlaceFamilyInstanceRequest>(
            ToolNames.ElementPlaceFamilyInstanceSafe,
            "Safely place family instance with dry-run and confirm.",
            ApprovalRequirement.HighRiskToken,
            string.Empty,
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => mutation.PreviewPlaceFamilyInstance(services, doc, payload, request),
            (_, services, doc, payload) => mutation.ExecutePlaceFamilyInstance(services, doc, payload));

        registry.Register(ToolNames.FileSaveDocument, "Save current document with confirm token.", PermissionLevel.FileLifecycle, ApprovalRequirement.HighRiskToken, true, fileLifecycleDocument.WithRiskTags("save"),
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSaveTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.SaveDisabled);
                }

                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                {
                    return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                }
                if (request.DryRun)
                {
                    var preview = fileLifecycle.Preview(ToolNames.FileSaveDocument, platform, doc, request);
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

                return ToolResponses.FromExecutionResult(request, fileLifecycle.Save(doc), StatusCodes.SaveSucceeded);
            });
        registry.Register(ToolNames.FileSaveAsDocument, "Save document as new file with confirm token.", PermissionLevel.FileLifecycle, ApprovalRequirement.HighRiskToken, true, fileLifecycleDocument.WithRiskTags("save"),
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSaveTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.SaveDisabled);
                }

                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                {
                    return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                }
                var payload = ToolPayloads.Read<SaveAsDocumentRequest>(request);
                if (request.DryRun)
                {
                    var preview = fileLifecycle.Preview(ToolNames.FileSaveAsDocument, platform, doc, request);
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

                return ToolResponses.FromExecutionResult(request, fileLifecycle.SaveAs(doc, payload), StatusCodes.SaveAsSucceeded);
            });
        registry.Register(ToolNames.WorksharingSynchronizeWithCentral, "Synchronize workshared model with central.", PermissionLevel.FileLifecycle, ApprovalRequirement.HighRiskToken, true, fileLifecycleDocument.WithRiskTags("sync"),
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSyncTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.SyncDisabled);
                }

                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                {
                    return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                }
                var payload = ToolPayloads.Read<SynchronizeRequest>(request);
                if (request.DryRun)
                {
                    var preview = fileLifecycle.Preview(ToolNames.WorksharingSynchronizeWithCentral, platform, doc, request);
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

                return ToolResponses.FromExecutionResult(request, fileLifecycle.Synchronize(doc, payload), StatusCodes.SyncSucceeded);
            });

        registry.Register(ToolNames.DomainHullDryRun, "Sample domain module: Hull dry-run.", PermissionLevel.Review, ApprovalRequirement.None, true, reviewDocument,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                var dry = ToolPayloads.Read<HullDryRunRequest>(request);
                var collected = hullCollector.Collect(doc);
                var planned = hullPlanner.Plan(doc, collected, dry);
                hullValidator.Enrich(planned);
                return ToolResponses.Success(request, planned);
            });
    }
}
