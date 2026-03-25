using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Validation;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

/// <summary>
/// Extended validator tests covering all major request DTOs.
/// Complements the existing ToolPayloadValidatorTests.
/// </summary>
public sealed class ToolPayloadValidatorExtendedTests
{
    // ── Null payload ────────────────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Null_Payload()
    {
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate<SetParametersRequest>(null!));
        Assert.Contains(ex.Diagnostics, d => d.Code == "PAYLOAD_NULL");
    }

    // ── DeleteElementsRequest ──────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Empty_DeleteElements()
    {
        var request = new DeleteElementsRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "DELETE_ELEMENT_IDS_EMPTY");
    }

    // ── MoveElementsRequest ────────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Empty_MoveElements()
    {
        var request = new MoveElementsRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "MOVE_ELEMENT_IDS_EMPTY");
    }

    [Fact]
    public void Validate_Throws_For_Zero_Delta_MoveElements()
    {
        var request = new MoveElementsRequest
        {
            ElementIds = new System.Collections.Generic.List<int> { 1 },
            DeltaX = 0, DeltaY = 0, DeltaZ = 0
        };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "MOVE_DELTA_ZERO");
    }

    [Fact]
    public void Validate_Allows_Valid_MoveElements()
    {
        var request = new MoveElementsRequest
        {
            ElementIds = new System.Collections.Generic.List<int> { 1 },
            DeltaX = 1.0
        };
        ToolPayloadValidator.Validate(request);
    }

    // ── PlaceFamilyInstanceRequest ─────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Invalid_FamilySymbolId()
    {
        var request = new PlaceFamilyInstanceRequest { FamilySymbolId = 0 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FAMILY_SYMBOL_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_Invalid_PlacementMode()
    {
        var request = new PlaceFamilyInstanceRequest { FamilySymbolId = 1, PlacementMode = "unknown" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "PLACEMENT_MODE_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_CurvePlacement_Without_Endpoints()
    {
        var request = new PlaceFamilyInstanceRequest { FamilySymbolId = 1, PlacementMode = "curve" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "CURVE_POINTS_REQUIRED");
    }

    // ── SaveAsDocumentRequest ──────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Empty_SaveAs_FilePath()
    {
        var request = new SaveAsDocumentRequest { FilePath = "" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FILE_PATH_REQUIRED");
    }

    // ── OpenBackgroundDocumentRequest ──────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Empty_OpenBackground_FilePath()
    {
        var request = new OpenBackgroundDocumentRequest { FilePath = "" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FILE_PATH_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_Relative_OpenBackground_FilePath()
    {
        var request = new OpenBackgroundDocumentRequest { FilePath = "relative/path.rvt" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FILE_PATH_NOT_ROOTED");
    }

    // ── CloseDocumentRequest ───────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Empty_CloseDocument_Key()
    {
        var request = new CloseDocumentRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "DOCUMENT_KEY_REQUIRED");
    }

    // ── SynchronizeRequest ─────────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Overlong_SyncComment()
    {
        var request = new SynchronizeRequest { Comment = new string('x', 1025) };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "SYNC_COMMENT_TOO_LONG");
    }

    [Fact]
    public void Validate_Allows_Valid_SyncComment()
    {
        var request = new SynchronizeRequest { Comment = "short comment" };
        ToolPayloadValidator.Validate(request);
    }

    // ── Create3DViewRequest ────────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Empty_3DView_Name()
    {
        var request = new Create3DViewRequest { ViewName = "" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "VIEW_NAME_REQUIRED");
    }

    // ── ApplyViewFilterRequest ─────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Missing_View_And_Filter_Identifiers()
    {
        var request = new ApplyViewFilterRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "VIEW_IDENTIFIER_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "FILTER_IDENTIFIER_REQUIRED");
    }

    // ── TaskContextRequest ─────────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Negative_TaskContext_Limits()
    {
        var request = new TaskContextRequest { MaxRecentOperations = -1, MaxRecentEvents = -1 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_CONTEXT_LIMIT_INVALID");
    }

    [Fact]
    public void Validate_Allows_Valid_TaskContext()
    {
        var request = new TaskContextRequest();
        ToolPayloadValidator.Validate(request);
    }

    // ── ElementQueryRequest ────────────────────────────────────────────

    [Fact]
    public void Validate_Throws_For_Zero_MaxResults_ElementQuery()
    {
        var request = new ElementQueryRequest { MaxResults = 0 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "MAX_RESULTS_INVALID");
    }

    [Fact]
    public void Validate_Allows_Default_ElementQuery()
    {
        var request = new ElementQueryRequest();
        ToolPayloadValidator.Validate(request);
    }

    // ── ReviewParameterCompletenessRequest ─────────────────────────────

    [Fact]
    public void Validate_Throws_For_Empty_RequiredParameterNames()
    {
        var request = new ReviewParameterCompletenessRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "REQUIRED_PARAMETERS_EMPTY");
    }

    [Fact]
    public void Validate_Throws_For_Empty_FixLoopScenario()
    {
        var request = new FixLoopPlanRequest { ScenarioName = "", MaxIssues = 10, MaxActions = 10 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FIX_LOOP_SCENARIO_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_FamilyLoad_Without_Root()
    {
        var request = new FamilyLoadRequest { RelativeFamilyPath = "Round.rfa" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FAMILY_LIBRARY_ROOT_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_ScheduleCreate_Without_Category()
    {
        var request = new ScheduleCreateRequest { ScheduleName = "Test" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "SCHEDULE_CATEGORY_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_IfcExport_Without_Preset()
    {
        var request = new IfcExportRequest { OutputRootName = "exports", RelativeOutputPath = "ifc" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "EXPORT_PRESET_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_RoundPenetrationCutPlan_Without_Source_Classes()
    {
        var request = new RoundPenetrationCutPlanRequest
        {
            SourceElementClasses = new System.Collections.Generic.List<string>(),
            HostElementClasses = new System.Collections.Generic.List<string> { "GYB" },
            SourceFamilyNameContains = new System.Collections.Generic.List<string> { "PIP" }
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "ROUND_PEN_SOURCE_CLASSES_EMPTY");
    }

    [Fact]
    public void Validate_Throws_For_RoundPenetrationCutBatch_Negative_Retry()
    {
        var request = new CreateRoundPenetrationCutBatchRequest
        {
            SourceElementClasses = new System.Collections.Generic.List<string> { "PIP" },
            HostElementClasses = new System.Collections.Generic.List<string> { "GYB" },
            SourceFamilyNameContains = new System.Collections.Generic.List<string> { "PIP" },
            MaxCutRetries = -1
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "ROUND_PEN_CUT_RETRY_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_RoundPenetrationReviewPacket_Invalid_MaxItems()
    {
        var request = new RoundPenetrationReviewPacketRequest
        {
            SourceElementClasses = new System.Collections.Generic.List<string> { "PIP" },
            HostElementClasses = new System.Collections.Generic.List<string> { "GYB" },
            SourceFamilyNameContains = new System.Collections.Generic.List<string> { "PIP" },
            MaxItems = 0
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "ROUND_PEN_REVIEW_MAX_ITEMS_INVALID");
    }

    [Fact]
    public void Validate_Allows_Valid_RoundPenetrationCutQc()
    {
        var request = new RoundPenetrationCutQcRequest
        {
            SourceElementClasses = new System.Collections.Generic.List<string> { "PIP", "PPF" },
            HostElementClasses = new System.Collections.Generic.List<string> { "GYB", "WFR" },
            SourceFamilyNameContains = new System.Collections.Generic.List<string> { "PIP", "PPF" }
        };

        ToolPayloadValidator.Validate(request);
    }
}
