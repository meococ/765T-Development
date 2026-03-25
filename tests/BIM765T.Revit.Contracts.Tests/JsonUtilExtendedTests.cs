using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

/// <summary>
/// Extended JSON serialization tests.
/// Complements the existing JsonUtilTests.
/// </summary>
public sealed class JsonUtilExtendedTests
{
    [Fact]
    public void Serialize_And_Deserialize_Complex_Nested_Object()
    {
        var original = new TaskContextResponse
        {
            Document = new DocumentSummaryDto
            {
                DocumentKey = "path:c:\\test.rvt",
                Title = "Test Project",
                IsActive = true,
                IsWorkshared = true
            },
            Fingerprint = new ContextFingerprint
            {
                DocumentKey = "path:c:\\test.rvt",
                ViewKey = "view:999",
                SelectionCount = 3
            }
        };

        var json = JsonUtil.Serialize(original);
        var result = JsonUtil.DeserializeRequired<TaskContextResponse>(json);

        Assert.Equal("path:c:\\test.rvt", result.Document.DocumentKey);
        Assert.Equal("Test Project", result.Document.Title);
        Assert.True(result.Document.IsActive);
        Assert.Equal("view:999", result.Fingerprint.ViewKey);
        Assert.Equal(3, result.Fingerprint.SelectionCount);
    }

    [Fact]
    public void DeserializeOrDefault_Returns_Default_On_Null()
    {
        var result = JsonUtil.DeserializeOrDefault<ElementQueryRequest>(null);
        Assert.NotNull(result);
        Assert.Equal(200, result.MaxResults);
    }

    [Fact]
    public void DeserializeOrDefault_Returns_Default_On_Whitespace()
    {
        var result = JsonUtil.DeserializeOrDefault<ElementQueryRequest>("   ");
        Assert.NotNull(result);
    }

    [Fact]
    public void DeserializeRequired_Throws_On_Null()
    {
        Assert.ThrowsAny<Exception>(() => JsonUtil.DeserializeRequired<ToolManifest>(null));
    }

    [Fact]
    public void TryDeserialize_Returns_True_On_Valid_Json()
    {
        var ok = JsonUtil.TryDeserialize<ContextFingerprint>("{\"DocumentKey\":\"test\"}", out var result, out var error);
        Assert.True(ok);
        Assert.Equal("test", result.DocumentKey);
        Assert.Null(error);
    }

    [Fact]
    public void TryDeserialize_Returns_False_On_Null()
    {
        var ok = JsonUtil.TryDeserialize<ContextFingerprint>(null, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Serialize_Handles_Empty_Collections()
    {
        var request = new SetParametersRequest();
        var json = JsonUtil.Serialize(request);
        Assert.Contains("Changes", json);

        var result = JsonUtil.DeserializeRequired<SetParametersRequest>(json);
        Assert.NotNull(result.Changes);
    }

    [Fact]
    public void Serialize_Preserves_Enum_Values()
    {
        var record = DiagnosticRecord.Create("CODE", DiagnosticSeverity.Error, "msg");
        var json = JsonUtil.Serialize(record);
        var result = JsonUtil.DeserializeRequired<DiagnosticRecord>(json);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public void Serialize_Handles_Special_Characters_In_Strings()
    {
        var dto = new DocumentSummaryDto
        {
            Title = "Dự án kiểm thử \"đặc biệt\" <test>",
            PathName = "C:\\Users\\Test\\Dự án.rvt"
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<DocumentSummaryDto>(json);

        Assert.Equal(dto.Title, result.Title);
        Assert.Equal(dto.PathName, result.PathName);
    }

    [Fact]
    public void DeserializePayloadOrDefault_Returns_Instance_On_Empty()
    {
        var result = JsonUtil.DeserializePayloadOrDefault<ReviewRuleSetRunRequest>(string.Empty);
        Assert.NotNull(result);
        Assert.Equal("document_health_v1", result.RuleSetName);
        Assert.Equal(100, result.MaxIssues);
    }

    [Fact]
    public void DeserializeRequired_Restores_Default_Int_When_Field_Name_Appears_In_String_Value()
    {
        var json = "{\"ClassName\":\"MaxResults\"}";

        var result = JsonUtil.DeserializeRequired<ElementQueryRequest>(json);

        Assert.Equal("MaxResults", result.ClassName);
        Assert.Equal(200, result.MaxResults);
    }

    [Fact]
    public void DeserializeRequired_Restores_Default_Bool_When_Field_Name_Appears_In_String_Value()
    {
        var json = "{\"ClassName\":\"ViewScopeOnly\"}";

        var result = JsonUtil.DeserializeRequired<ElementQueryRequest>(json);

        Assert.Equal("ViewScopeOnly", result.ClassName);
        Assert.True(result.ViewScopeOnly);
    }

    [Fact]
    public void DeserializeRequired_Preserves_Explicit_Bool_Value()
    {
        var json = "{\"ViewScopeOnly\":false}";

        var result = JsonUtil.DeserializeRequired<ElementQueryRequest>(json);

        Assert.False(result.ViewScopeOnly);
    }

    [Fact]
    public void DeserializeRequired_Restores_Default_Double_When_Field_Is_Missing()
    {
        var result = JsonUtil.DeserializeRequired<DuplicateElementsRequest>("{\"DocumentKey\":\"path:c:/test.rvt\"}");

        Assert.Equal(1.0d, result.ToleranceMm);
        Assert.Equal(200, result.MaxResults);
    }

    [Fact]
    public void DeserializeRequired_Restores_Default_DateTime_When_Field_Is_Missing()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var result = JsonUtil.DeserializeRequired<ToolRequestEnvelope>("{\"ToolName\":\"worker.get_context\"}");
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(result.RequestedAtUtc, before, after);
    }
}
