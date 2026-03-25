using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Validation;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class ToolPayloadValidatorTests
{
    [Fact]
    public void Validate_Throws_For_Invalid_SetParametersRequest()
    {
        var request = new SetParametersRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "PARAMETER_CHANGES_EMPTY");
    }

    [Fact]
    public void Validate_Throws_For_Invalid_RuleOperator()
    {
        var request = new CreateOrUpdateViewFilterRequest
        {
            FilterName = "X",
            Rules =
            {
                new ViewFilterRuleRequest
                {
                    ParameterName = "Comments",
                    Operator = "boom",
                    Value = "EX"
                }
            }
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FILTER_OPERATOR_INVALID");
    }

    [Fact]
    public void Validate_Allows_Valid_AddTextNoteRequest()
    {
        var request = new AddTextNoteRequest
        {
            Text = "hello"
        };

        ToolPayloadValidator.Validate(request);
    }

    [Fact]
    public void Validate_Throws_For_Invalid_CaptureSnapshotRequest()
    {
        var request = new CaptureSnapshotRequest
        {
            Scope = "elements",
            MaxElements = 0
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "SNAPSHOT_ELEMENT_IDS_EMPTY");
        Assert.Contains(ex.Diagnostics, d => d.Code == "SNAPSHOT_MAX_ELEMENTS_INVALID");
    }


    [Fact]
    public void Validate_Throws_For_Invalid_CreateRoundShadowBatchRequest()
    {
        var request = new CreateRoundShadowBatchRequest
        {
            SourceFamilyName = string.Empty,
            RoundFamilyName = string.Empty,
            MaxResults = 0,
            SetCommentsTrace = true,
            TraceCommentPrefix = string.Empty,
            PlacementMode = "boom"
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "SOURCE_FAMILY_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "ROUND_FAMILY_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "MAX_RESULTS_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "TRACE_PREFIX_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "ROUND_SHADOW_PLACEMENT_MODE_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_Invalid_RoundShadowCleanupRequest()
    {
        var request = new RoundShadowCleanupRequest
        {
            UseLatestSuccessfulBatchWhenEmpty = false,
            RequireTraceCommentMatch = true,
            TraceCommentPrefix = string.Empty,
            MaxResults = 0
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "MAX_RESULTS_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "CLEANUP_SCOPE_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "TRACE_PREFIX_REQUIRED");
    }

}
