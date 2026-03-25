using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Parameter operations la module rieng: truy vet, list shared/project params,
/// va cac mutation parameter-centric co the tai su dung across BIM lanes.
/// </summary>
internal sealed class ParameterToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal ParameterToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var dataExport = _context.DataExport;
        var mutation = _context.Mutation;
        var parameterRead = ToolManifestPresets.Read("document")
            .WithDomainGroup("parameter")
            .WithTaskFamily("parameter_ops")
            .WithPackId("bim765t.core.platform");
        var parameterMutation = ToolManifestPresets.Mutation("document")
            .WithDomainGroup("parameter")
            .WithTaskFamily("parameter_ops")
            .WithPackId("bim765t.core.platform");
        var parameterBatchMutation = parameterMutation.WithBatchMode("chunked");

        registry.Register(ToolNames.ParameterListShared,
            "List all shared/project parameters with their bindings and categories.",
            PermissionLevel.Read, ApprovalRequirement.None, false, parameterRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SharedParameterListRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, dataExport.ListSharedParameters(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"GroupNameContains\":\"\",\"NameContains\":\"\",\"MaxResults\":500}");

        registry.RegisterMutationTool<CopyParametersBetweenRequest>(
            ToolNames.ParameterCopyBetweenSafe,
            "Copy parameter values from one element to multiple target elements, safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"SourceElementId\":0,\"TargetElementIds\":[],\"ParameterNames\":[],\"SkipReadOnly\":true}",
            parameterMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => mutation.PreviewCopyParameters(services, doc, payload, request),
            (uiapp, services, doc, payload) => mutation.ExecuteCopyParameters(services, doc, payload));

        registry.RegisterMutationTool<AddSharedParameterRequest>(
            ToolNames.ParameterAddSharedSafe,
            "Add a new shared parameter to specified categories, safely with dry-run and confirm. High-risk operation.",
            ApprovalRequirement.HighRiskToken,
            "{\"DocumentKey\":\"\",\"ParameterName\":\"MyParam\",\"GroupName\":\"Data\",\"ParameterType\":\"Text\",\"CategoryNames\":[\"Walls\"],\"IsInstance\":true}",
            parameterMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => mutation.PreviewAddSharedParameter(services, doc, payload, request),
            (uiapp, services, doc, payload) => mutation.ExecuteAddSharedParameter(services, doc, payload));

        registry.RegisterMutationTool<BatchFillParameterRequest>(
            ToolNames.ParameterBatchFillSafe,
            "Batch fill a parameter value on multiple elements by category or element IDs, safely with dry-run and confirm.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"ParameterName\":\"Comments\",\"FillValue\":\"Reviewed\",\"FillMode\":\"OnlyEmpty\",\"ElementIds\":[],\"CategoryNames\":[\"Walls\"]}",
            parameterBatchMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => mutation.PreviewBatchFillParameter(services, doc, payload, request),
            (uiapp, services, doc, payload) => mutation.ExecuteBatchFillParameter(services, doc, payload));
    }
}
