using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class DeliveryOpsToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal DeliveryOpsToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var delivery = _context.DeliveryOps;
        var readNoContext = ToolManifestPresets.Read();
        var readDocument = ToolManifestPresets.Read("document");
        var mutationDocument = ToolManifestPresets.Mutation("document");
        var fileLifecycleDocument = ToolManifestPresets.FileLifecycle("document");

        registry.Register(
            ToolNames.FamilyListLibraryRoots,
            "List allowlisted family library roots available for safe family loading.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, delivery.ListFamilyLibraryRoots()));

        registry.Register(
            ToolNames.ExportListPresets,
            "List delivery presets and allowlisted output roots for IFC/DWG/PDF operations.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readDocument,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                return ToolResponses.Success(request, delivery.ListPresets(doc));
            });

        registry.Register(
            ToolNames.StorageValidateOutputTarget,
            "Validate an output target against allowlisted output roots before export/print execution.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<OutputTargetValidationRequest>(request);
                return ToolResponses.Success(request, delivery.ValidateOutputTarget(payload));
            },
            "{\"DocumentKey\":\"\",\"OperationKind\":\"export\",\"OutputRootName\":\"\",\"RelativePath\":\"\"}");

        registry.Register(
            ToolNames.SchedulePreviewCreate,
            "Preview a generic model schedule definition: resolved category, fields, filters, sorts, and warnings.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ScheduleCreateRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, delivery.PreviewScheduleCreate(doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ScheduleName\":\"\",\"CategoryName\":\"\",\"Fields\":[],\"Filters\":[],\"Sorts\":[],\"IsItemized\":true,\"IncludeLinkedFiles\":false,\"ShowGrandTotal\":false,\"MaxFieldCount\":25}");

        registry.Register(
            ToolNames.FamilyLoadSafe,
            "Load a family or specific family symbols from an allowlisted library root with dry-run and explicit overwrite policy.",
            PermissionLevel.Mutate,
            ApprovalRequirement.HighRiskToken,
            true,
            mutationDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowWriteTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.WriteDisabled);
                }

                var payload = ToolPayloads.Read<FamilyLoadRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var preview = delivery.PreviewFamilyLoad(doc, payload, request);
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

                return ToolResponses.FromExecutionResult(request, delivery.ExecuteFamilyLoad(doc, payload));
            },
            "{\"DocumentKey\":\"\",\"LibraryRootName\":\"\",\"RelativeFamilyPath\":\"\",\"TypeNames\":[],\"LoadAllSymbols\":false,\"OverwriteExisting\":false}");

        registry.Register(
            ToolNames.ScheduleCreateSafe,
            "Create a generic model schedule with bounded fields/filters/sorts using dry-run and approval.",
            PermissionLevel.Mutate,
            ApprovalRequirement.HighRiskToken,
            true,
            mutationDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowWriteTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.WriteDisabled);
                }

                var payload = ToolPayloads.Read<ScheduleCreateRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var preview = delivery.PreviewCreateSchedule(doc, payload, request);
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

                return ToolResponses.FromExecutionResult(request, delivery.ExecuteCreateSchedule(doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ScheduleName\":\"\",\"CategoryName\":\"\",\"Fields\":[],\"Filters\":[],\"Sorts\":[],\"IsItemized\":true,\"IncludeLinkedFiles\":false,\"ShowGrandTotal\":false,\"MaxFieldCount\":25}");

        registry.Register(
            ToolNames.ExportIfcSafe,
            "Export IFC using a named preset and an allowlisted output root with dry-run and approval.",
            PermissionLevel.FileLifecycle,
            ApprovalRequirement.HighRiskToken,
            true,
            fileLifecycleDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSaveTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.SaveDisabled);
                }

                var payload = ToolPayloads.Read<IfcExportRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var preview = delivery.PreviewIfcExport(uiapp, doc, payload, request);
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

                return ToolResponses.FromExecutionResult(request, delivery.ExecuteIfcExport(uiapp, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"PresetName\":\"\",\"OutputRootName\":\"\",\"RelativeOutputPath\":\"\",\"FileName\":\"\",\"ViewId\":null,\"ViewName\":\"\",\"OverwriteExisting\":false}");

        registry.Register(
            ToolNames.ExportDwgSafe,
            "Export DWG using a named preset/native setup and an allowlisted output root with dry-run and approval.",
            PermissionLevel.FileLifecycle,
            ApprovalRequirement.HighRiskToken,
            true,
            fileLifecycleDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSaveTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.SaveDisabled);
                }

                var payload = ToolPayloads.Read<DwgExportRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var preview = delivery.PreviewDwgExport(uiapp, doc, payload, request);
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

                return ToolResponses.FromExecutionResult(request, delivery.ExecuteDwgExport(uiapp, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"PresetName\":\"\",\"OutputRootName\":\"\",\"RelativeOutputPath\":\"\",\"FileName\":\"\",\"ViewIds\":[],\"SheetIds\":[],\"UseActiveViewWhenEmpty\":true,\"OverwriteExisting\":false}");

        registry.Register(
            ToolNames.SheetPrintPdfSafe,
            "Export sheets to PDF using a named preset and allowlisted output root with dry-run and approval.",
            PermissionLevel.FileLifecycle,
            ApprovalRequirement.HighRiskToken,
            true,
            ToolManifestPresets.FileLifecycle("document", "sheet"),
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSaveTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.SaveDisabled);
                }

                var payload = ToolPayloads.Read<PdfPrintRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var preview = delivery.PreviewPdfPrint(doc, payload, request);
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

                return ToolResponses.FromExecutionResult(request, delivery.ExecutePdfPrint(doc, payload));
            },
            "{\"DocumentKey\":\"\",\"PresetName\":\"\",\"OutputRootName\":\"\",\"RelativeOutputPath\":\"\",\"FileName\":\"\",\"SheetIds\":[],\"SheetNumbers\":[],\"Combine\":true,\"OverwriteExisting\":false}");
    }
}
