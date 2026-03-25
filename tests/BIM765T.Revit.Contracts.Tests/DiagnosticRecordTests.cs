using BIM765T.Revit.Contracts.Common;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class DiagnosticRecordTests
{
    [Fact]
    public void Create_Sets_All_Properties()
    {
        var record = DiagnosticRecord.Create("TEST_CODE", DiagnosticSeverity.Error, "Test message", 42, "{\"key\":\"val\"}");

        Assert.Equal("TEST_CODE", record.Code);
        Assert.Equal(DiagnosticSeverity.Error, record.Severity);
        Assert.Equal("Test message", record.Message);
        Assert.Equal(42, record.SourceId);
        Assert.Equal("{\"key\":\"val\"}", record.DetailsJson);
    }

    [Fact]
    public void Create_Without_Optional_Params_Uses_Defaults()
    {
        var record = DiagnosticRecord.Create("CODE", DiagnosticSeverity.Info, "msg");

        Assert.Equal("CODE", record.Code);
        Assert.Equal(DiagnosticSeverity.Info, record.Severity);
        Assert.Equal("msg", record.Message);
        Assert.Null(record.SourceId);
        Assert.Null(record.DetailsJson);
    }

    [Fact]
    public void Default_Instance_Has_Info_Severity()
    {
        var record = new DiagnosticRecord();

        Assert.Equal(string.Empty, record.Code);
        Assert.Equal(DiagnosticSeverity.Info, record.Severity);
        Assert.Equal(string.Empty, record.Message);
    }

    [Fact]
    public void DiagnosticSeverity_Has_Three_Values()
    {
        var values = Enum.GetValues<DiagnosticSeverity>();
        Assert.Equal(3, values.Length);
        Assert.Contains(DiagnosticSeverity.Info, values);
        Assert.Contains(DiagnosticSeverity.Warning, values);
        Assert.Contains(DiagnosticSeverity.Error, values);
    }

    [Fact]
    public void DiagnosticSeverity_Ordering_Info_Less_Than_Error()
    {
        Assert.True(DiagnosticSeverity.Info < DiagnosticSeverity.Warning);
        Assert.True(DiagnosticSeverity.Warning < DiagnosticSeverity.Error);
    }
}
