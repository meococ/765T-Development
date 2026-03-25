using BIM765T.Revit.Contracts.Common;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class StatusCodesTests
{
    [Fact]
    public void Ok_Equals_Expected_Value()
    {
        Assert.Equal("OK", StatusCodes.Ok);
    }

    [Fact]
    public void InvalidRequest_Equals_Expected_Value()
    {
        Assert.Equal("INVALID_REQUEST", StatusCodes.InvalidRequest);
    }

    [Fact]
    public void UnsupportedTool_Equals_Expected_Value()
    {
        Assert.Equal("UNSUPPORTED_TOOL", StatusCodes.UnsupportedTool);
    }

    [Fact]
    public void InternalError_Equals_Expected_Value()
    {
        Assert.Equal("INTERNAL_ERROR", StatusCodes.InternalError);
    }

    [Fact]
    public void Timeout_Equals_Expected_Value()
    {
        Assert.Equal("TIMEOUT", StatusCodes.Timeout);
    }

    [Fact]
    public void WorkflowNotFound_Equals_Expected_Value()
    {
        Assert.Equal("WORKFLOW_NOT_FOUND", StatusCodes.WorkflowNotFound);
    }

    [Fact]
    public void PreviewRunRequired_Equals_Expected_Value()
    {
        Assert.Equal("PREVIEW_RUN_REQUIRED", StatusCodes.PreviewRunRequired);
    }

    [Fact]
    public void All_StatusCodes_Are_NonEmpty_UpperSnakeCase()
    {
        var fields = typeof(StatusCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.True(fields.Length > 0, "StatusCodes should have at least one constant.");

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"StatusCodes.{field.Name} should not be empty.");
            Assert.DoesNotContain(value, static ch => char.IsLetter(ch) && char.IsLower(ch));
        }
    }

    [Fact]
    public void All_StatusCodes_Are_Unique()
    {
        var fields = typeof(StatusCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();
        var distinctValues = values.Distinct(StringComparer.Ordinal).ToList();

        Assert.Equal(values.Count, distinctValues.Count);
    }

    [Fact]
    public void StatusCodes_Count_Is_At_Least_30()
    {
        var fields = typeof(StatusCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.True(fields.Length >= 30, $"Expected at least 30 status codes, found {fields.Length}.");
    }
}
