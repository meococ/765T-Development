using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class IntelligenceToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal IntelligenceToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var familyXray = _context.FamilyXray;
        var sheetIntelligence = _context.SheetIntelligence;
        var readDocument = ToolManifestPresets.Read("document");
        var qcRead = ToolManifestPresets.Read("document").WithRiskTags("qc", "intelligence");

        registry.Register(
            ToolNames.FamilyXray,
            "Inspect a family definition deeply: types, nested families, parameters, formulas, reference planes, and connectors.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readDocument.WithRulePackTags("family_xray").WithRiskTags("intelligence", "family"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FamilyXrayRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, familyXray.Xray(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"FamilyId\":0,\"FamilyName\":\"M_Round_Duct_Diffuser\",\"IncludeNestedFamilies\":true,\"IncludeConnectors\":true,\"IncludeReferencePlanes\":true,\"MaxNestedFamilies\":25,\"MaxParameters\":200,\"MaxTypeNames\":25}");

        registry.Register(
            ToolNames.SheetCaptureIntelligence,
            "Capture sheet intelligence in structured form: title block data, viewport composition, schedule placements, sheet notes, and optional artifacts.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            qcRead.WithRulePackTags("sheet_intelligence"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SheetCaptureIntelligenceRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, sheetIntelligence.Capture(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SheetId\":0,\"SheetNumber\":\"A101\",\"IncludeViewportDetails\":true,\"IncludeScheduleData\":true,\"MaxViewports\":20,\"MaxSchedules\":10,\"MaxSheetTextNotes\":50,\"MaxViewportTextNotes\":20,\"WriteArtifacts\":false}");
    }
}
