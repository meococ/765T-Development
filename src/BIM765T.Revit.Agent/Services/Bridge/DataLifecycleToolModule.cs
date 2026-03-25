using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Data lifecycle: export/import/preview va schedule extraction co cau truc.
/// Khong tron voi audit/QC hay parameter authoring.
/// </summary>
internal sealed class DataLifecycleToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal DataLifecycleToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var dataExport = _context.DataExport;
        var schedules = _context.ScheduleExtraction;
        var mutation = _context.Mutation;
        var dataRead = ToolManifestPresets.Read("document")
            .WithDomainGroup("data")
            .WithTaskFamily("delivery_data")
            .WithPackId("bim765t.core.platform");
        var dataMutation = ToolManifestPresets.Mutation("document")
            .WithDomainGroup("data")
            .WithTaskFamily("delivery_data")
            .WithPackId("bim765t.core.platform");

        registry.Register(ToolNames.DataExport,
            "Export element parameters to JSON or CSV format. Supports filtering by category and parameter.",
            PermissionLevel.Read, ApprovalRequirement.None, false, dataRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<DataExportRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, dataExport.ExportData(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[\"Walls\"],\"ParameterNames\":[],\"Format\":\"json\",\"OutputPath\":\"\",\"MaxResults\":5000}");

        registry.Register(ToolNames.DataExportSchedule,
            "Export schedule table data (headers + rows) to JSON.",
            PermissionLevel.Read, ApprovalRequirement.None, false, dataRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ExportScheduleRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, dataExport.ExportScheduleData(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ScheduleId\":0,\"ScheduleName\":\"\",\"Format\":\"json\"}");

        registry.Register(ToolNames.DataPreviewImport,
            "Preview a data import file without making changes - shows match counts, parameter names, warnings.",
            PermissionLevel.Read, ApprovalRequirement.None, false, dataRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<DataImportPreviewRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, dataExport.PreviewImport(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"InputPath\":\"\",\"Format\":\"csv\",\"MatchParameterName\":\"Mark\",\"MaxPreviewRows\":20}");

        registry.RegisterMutationTool<DataImportRequest>(
            ToolNames.DataImportSafe,
            "Import data from file and set parameter values on matched elements. High-risk operation with dry-run.",
            ApprovalRequirement.HighRiskToken,
            "{\"DocumentKey\":\"\",\"InputPath\":\"\",\"Format\":\"csv\",\"MatchParameterName\":\"Mark\",\"SkipReadOnly\":true}",
            dataMutation,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => mutation.PreviewDataImport(services, doc, payload, request),
            (uiapp, services, doc, payload) => mutation.ExecuteDataImport(services, doc, payload));

        registry.Register(
            ToolNames.DataExtractScheduleStructured,
            "Extract schedule data into structured rows/columns so AI can inspect schedules without screenshots or CSV cleanup.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            dataRead.WithRulePackTags("schedule_structured"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ScheduleExtractionRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, schedules.Extract(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ScheduleId\":0,\"ScheduleName\":\"Door Schedule\",\"MaxRows\":200,\"IncludeEmptyRows\":false,\"IncludeColumnMetadata\":true}");
    }
}
