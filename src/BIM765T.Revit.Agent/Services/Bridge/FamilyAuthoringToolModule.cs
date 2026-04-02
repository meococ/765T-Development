using System;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Registers family authoring tools: parameter management, formula, type catalog,
/// geometry creation (extrusion, sweep, blend, revolution), reference planes,
/// subcategory, nested family loading, save.
///
/// Tools in this module require the active document to be a Family Document (.rfa).
/// All mutation tools follow the standard dry-run → approval → execute flow.
/// </summary>
internal sealed class FamilyAuthoringToolModule : IToolModule
{
    private readonly ToolModuleContext _context;
    private readonly FamilyAuthoringService _authoring;

    internal FamilyAuthoringToolModule(ToolModuleContext context)
    {
        _context = context;
        _authoring = new FamilyAuthoringService();
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var readFamilyDoc = ToolManifestPresets.Read("family_document")
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal);
        var mutationFamilyDoc = ToolManifestPresets.Mutation("family_document")
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal);
        var fileLifecycleFamilyDoc = ToolManifestPresets.FileLifecycle("family_document")
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal);

        // ── READ TOOLS ──

        registry.Register(
            ToolNames.FamilyListGeometry,
            "List all geometry forms, reference planes, parameters, and types in the current family document.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readFamilyDoc,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                return ToolResponses.Success(request, _authoring.ListGeometry(platform, doc));
            });

        // ── MUTATION TOOLS: Parameter ──

        registry.RegisterMutationTool<FamilyAddParameterRequest>(
            ToolNames.FamilyAddParameterSafe,
            "Add a new family parameter with type, group, and optional default value. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"ParameterName\":\"\",\"ParameterType\":\"Length\",\"ParameterGroup\":\"geometry\",\"IsInstance\":true,\"DefaultValue\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewAddParameter(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteAddParameter(services, doc, payload));

        registry.RegisterMutationTool<FamilySetFormulaRequest>(
            ToolNames.FamilySetParameterFormulaSafe,
            "Set or update a formula on an existing family parameter. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"ParameterName\":\"\",\"Formula\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewSetFormula(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteSetFormula(services, doc, payload));

        registry.RegisterMutationTool<FamilySetTypeCatalogRequest>(
            ToolNames.FamilySetTypeCatalogSafe,
            "Create or update family types with parameter values (type catalog builder). Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"Types\":[{\"TypeName\":\"\",\"ParameterValues\":{}}]}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewSetTypeCatalog(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteSetTypeCatalog(services, doc, payload));

        // ── MUTATION TOOLS: Geometry ──

        registry.RegisterMutationTool<FamilyCreateExtrusionRequest>(
            ToolNames.FamilyCreateExtrusionSafe,
            "Create an extrusion (solid or void) from a closed profile in the family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"Profile\":[{\"X\":0,\"Y\":0,\"Z\":0}],\"IsSolid\":true,\"StartOffset\":0,\"EndOffset\":1.0,\"SketchPlaneName\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewCreateExtrusion(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteCreateExtrusion(services, doc, payload));

        registry.RegisterMutationTool<FamilyCreateSweepRequest>(
            ToolNames.FamilyCreateSweepSafe,
            "Create a sweep (solid or void) from profile and path points in the family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"Profile\":[{\"X\":0,\"Y\":0,\"Z\":0}],\"PathPoints\":[{\"X\":0,\"Y\":0,\"Z\":0}],\"IsSolid\":true,\"SketchPlaneName\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewCreateSweep(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteCreateSweep(services, doc, payload));

        registry.RegisterMutationTool<FamilyCreateBlendRequest>(
            ToolNames.FamilyCreateBlendSafe,
            "Create a blend (solid or void) from bottom and top profiles in the family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"BottomProfile\":[{\"X\":0,\"Y\":0,\"Z\":0}],\"TopProfile\":[{\"X\":0,\"Y\":0,\"Z\":0}],\"TopOffset\":1.0,\"IsSolid\":true,\"SketchPlaneName\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewCreateBlend(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteCreateBlend(services, doc, payload));

        registry.RegisterMutationTool<FamilyCreateRevolutionRequest>(
            ToolNames.FamilyCreateRevolutionSafe,
            "Create a revolution (solid or void) from profile and axis in the family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"Profile\":[{\"X\":0,\"Y\":0,\"Z\":0}],\"IsSolid\":true,\"AxisOriginX\":0,\"AxisOriginY\":0,\"AxisDirectionX\":0,\"AxisDirectionY\":1,\"StartAngle\":0,\"EndAngle\":360,\"SketchPlaneName\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewCreateRevolution(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteCreateRevolution(services, doc, payload));

        // ── MUTATION TOOLS: Structure ──

        registry.RegisterMutationTool<FamilyAddReferencePlaneRequest>(
            ToolNames.FamilyAddReferencePlaneSafe,
            "Add a named reference plane in the family document for constraining geometry.",
            ApprovalRequirement.ConfirmToken,
            "{\"Name\":\"\",\"OriginX\":0,\"OriginY\":0,\"OriginZ\":0,\"NormalX\":1,\"NormalY\":0,\"NormalZ\":0,\"ExtentFeet\":1.0}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewAddReferencePlane(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteAddReferencePlane(services, doc, payload));

        registry.RegisterMutationTool<FamilySetSubcategoryRequest>(
            ToolNames.FamilySetSubcategorySafe,
            "Assign a subcategory to a geometry form element in the family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"FormElementId\":0,\"SubcategoryName\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewSetSubcategory(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteSetSubcategory(services, doc, payload));

        registry.RegisterMutationTool<FamilyLoadNestedRequest>(
            ToolNames.FamilyLoadNestedSafe,
            "Load a nested family (.rfa) into the current family document from an absolute path.",
            ApprovalRequirement.ConfirmToken,
            "{\"FamilyFilePath\":\"\",\"OverwriteExisting\":false}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewLoadNested(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteLoadNested(services, doc, payload));

        // ── MUTATION TOOLS: Tier 1 — Dimension, Alignment, Connector ──

        registry.RegisterMutationTool<FamilyAddDimensionRequest>(
            ToolNames.FamilyAddDimensionSafe,
            "Create a dimension between two reference planes and optionally label (bind) it to a family parameter. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"ReferencePlane1Name\":\"\",\"ReferencePlane2Name\":\"\",\"LabelParameterName\":\"\",\"IsLocked\":false,\"DimensionType\":\"linear\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewAddDimension(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteAddDimension(services, doc, payload));

        registry.RegisterMutationTool<FamilyAddAlignmentRequest>(
            ToolNames.FamilyAddAlignmentSafe,
            "Create an alignment constraint between a reference plane and a geometry face, optionally locked. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"ReferencePlaneName\":\"\",\"GeometryElementId\":0,\"GeometryFaceIndex\":0,\"IsLocked\":true}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewAddAlignment(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteAddAlignment(services, doc, payload));

        registry.RegisterMutationTool<FamilyAddConnectorRequest>(
            ToolNames.FamilyAddConnectorSafe,
            "Add a MEP connector (duct, pipe, electrical, conduit, cable tray) to a geometry form in the family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"ConnectorDomain\":\"duct\",\"HostGeometryElementId\":0,\"ConnectorType\":\"End\",\"ProfileOption\":\"Round\",\"Direction\":\"Z\",\"SystemClassification\":\"Other\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewAddConnector(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteAddConnector(services, doc, payload));

        // ── MUTATION TOOLS: Tier 2 — Visibility, Subcategory Creation, Material, Param Visibility ──

        registry.RegisterMutationTool<FamilySetVisibilityRequest>(
            ToolNames.FamilySetVisibilitySafe,
            "Set visibility per detail level (Fine/Medium/Coarse) and view direction for a geometry form. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"FormElementId\":0,\"IsShownInFine\":true,\"IsShownInMedium\":true,\"IsShownInCoarse\":true,\"IsShownInPlanRCP\":true,\"IsShownInFrontBack\":true,\"IsShownInLeftRight\":true}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewSetVisibility(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteSetVisibility(services, doc, payload));

        registry.RegisterMutationTool<FamilyCreateSubcategoryRequest>(
            ToolNames.FamilyCreateSubcategorySafe,
            "Create a new subcategory within the family category with optional line weight, color, and material. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"SubcategoryName\":\"\",\"LineWeight\":0,\"LineColorR\":-1,\"LineColorG\":-1,\"LineColorB\":-1,\"MaterialName\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewCreateSubcategory(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteCreateSubcategory(services, doc, payload));

        registry.RegisterMutationTool<FamilyBindMaterialRequest>(
            ToolNames.FamilyBindMaterialSafe,
            "Bind a material family parameter to a geometry form element for appearance control. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"FormElementId\":0,\"MaterialParameterName\":\"\",\"DefaultMaterialName\":\"\"}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewBindMaterial(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteBindMaterial(services, doc, payload));

        registry.RegisterMutationTool<FamilySetParameterVisibilityRequest>(
            ToolNames.FamilySetParameterVisibilitySafe,
            "Associate a Yes/No parameter to control geometry visibility (conditional visibility). Creates parameter if not exists. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"FormElementId\":0,\"VisibilityParameterName\":\"\",\"DefaultVisible\":true}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewSetParameterVisibility(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteSetParameterVisibility(services, doc, payload));

        // ── MUTATION TOOLS: Tier 3 — Spline Extrusion, Shared Parameter, Category ──

        registry.RegisterMutationTool<FamilyCreateSplineExtrusionRequest>(
            ToolNames.FamilyCreateSplineExtrusionSafe,
            "Create an extrusion from a spline profile (Hermite or NURBS) for smooth curved geometry. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"SplineType\":\"hermite\",\"ControlPoints\":[{\"X\":0,\"Y\":0}],\"IsClosed\":true,\"StartOffset\":0,\"EndOffset\":1.0,\"IsVoid\":false}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewCreateSplineExtrusion(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteCreateSplineExtrusion(services, doc, payload));

        registry.RegisterMutationTool<FamilyAddSharedParameterRequest>(
            ToolNames.FamilyAddSharedParameterSafe,
            "Add a shared parameter from a shared parameter file (.txt) to the family. Optionally replace existing family parameter. Requires active family document.",
            ApprovalRequirement.ConfirmToken,
            "{\"SharedParameterFilePath\":\"\",\"GroupName\":\"\",\"ParameterName\":\"\",\"ParameterGroup\":\"PG_DATA\",\"IsInstance\":false,\"ReplaceExisting\":false}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewAddSharedParameter(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteAddSharedParameter(services, doc, payload));

        registry.RegisterMutationTool<FamilySetCategoryRequest>(
            ToolNames.FamilySetCategorySafe,
            "Set or change the family category (e.g. Generic Models, Mechanical Equipment, Pipe Fittings). Requires active family document.",
            ApprovalRequirement.HighRiskToken,
            "{\"CategoryName\":\"\",\"BuiltInCategoryId\":-1}",
            mutationFamilyDoc,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewSetCategory(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteSetCategory(services, doc, payload));

        // ── FILE LIFECYCLE TOOLS ──

        registry.RegisterMutationTool<FamilySaveRequest>(
            ToolNames.FamilySaveSafe,
            "Save or Save-As the current family document with optional compact and overwrite options.",
            ApprovalRequirement.HighRiskToken,
            "{\"SaveAsPath\":\"\",\"OverwriteExisting\":true,\"CompactFile\":false}",
            fileLifecycleFamilyDoc,
            () => platform.Settings.AllowSaveTools,
            StatusCodes.SaveDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (_, services, doc, payload, request) => _authoring.PreviewSave(services, doc, payload, request),
            (_, services, doc, payload) => _authoring.ExecuteSave(services, doc, payload));

        // ── CREATE FAMILY DOCUMENT FROM TEMPLATE ──
        // Special: không cần family doc active sẵn — tool TẠO doc mới.
        // Dùng Register() thay vì RegisterMutationTool vì không resolve existing document.

        var fileLifecycleAny = ToolManifestPresets.FileLifecycle()
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal)
            .WithRiskTags("file_create", "document_switch");

        registry.Register(
            ToolNames.FamilyCreateDocumentSafe,
            "Create a new Revit family document (.rfa) from a template (.rft). " +
            "Supports template categories: generic_model, duct_fitting, pipe_fitting, mechanical_equipment, electrical_fixture, structural_framing, custom. " +
            "Saves to disk and optionally activates in Revit UI. Subsequent family.* tools then operate on this new document.",
            PermissionLevel.FileLifecycle,
            ApprovalRequirement.HighRiskToken,
            true,
            fileLifecycleAny,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowSaveTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.SaveDisabled);
                }

                var payload = ToolPayloads.Read<FamilyCreateDocumentRequest>(request);

                // Use active doc for approval context (may be project doc or null)
                var contextDoc = uiapp.ActiveUIDocument?.Document;

                if (request.DryRun)
                {
                    var previewResult = _authoring.PreviewCreateDocument(uiapp, payload);
                    if (contextDoc != null)
                    {
                        return ToolResponses.ConfirmationRequired(request,
                            platform.FinalizePreviewResult(uiapp, contextDoc, request, previewResult));
                    }

                    // No active document — build minimal preview without context fingerprint
                    previewResult.PreviewRunId = Guid.NewGuid().ToString("N");
                    previewResult.ApprovalToken = BuildNoContextApprovalToken(previewResult.PreviewRunId);
                    return ToolResponses.ConfirmationRequired(request, previewResult);
                }

                if (contextDoc != null)
                {
                    var approval = platform.ValidateApprovalRequest(uiapp, contextDoc, request);
                    if (!string.Equals(approval, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))
                    {
                        return ToolResponses.Failure(request, approval);
                    }
                }
                else if (!IsValidNoContextApproval(request.PreviewRunId, request.ApprovalToken))
                {
                    return ToolResponses.Failure(request, StatusCodes.ApprovalInvalid);
                }

                return ToolResponses.FromExecutionResult(request, _authoring.ExecuteCreateDocument(uiapp, payload));
            },
            "{\"TemplateCategory\":\"generic_model\",\"SaveAsPath\":\"C:\\\\Families\\\\MyFamily.rfa\",\"ActivateInUI\":true,\"UnitSystem\":\"metric\"}");
    }

    private static string BuildNoContextApprovalToken(string previewRunId)
    {
        return "family_create_document:" + (previewRunId ?? string.Empty);
    }

    private static bool IsValidNoContextApproval(string previewRunId, string approvalToken)
    {
        return !string.IsNullOrWhiteSpace(previewRunId)
            && string.Equals(approvalToken, BuildNoContextApprovalToken(previewRunId), StringComparison.Ordinal);
    }
}
