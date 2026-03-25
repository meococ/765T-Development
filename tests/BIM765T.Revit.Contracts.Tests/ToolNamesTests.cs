using BIM765T.Revit.Contracts.Bridge;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class ToolNamesTests
{
    [Fact]
    public void All_ToolNames_Follow_DotSeparated_Convention()
    {
        var fields = typeof(ToolNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.True(fields.Length > 0);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"ToolNames.{field.Name} should not be empty.");
            Assert.True(value.Contains('.'), $"ToolNames.{field.Name} = \"{value}\" should contain a dot separator.");
        }
    }

    [Fact]
    public void All_ToolNames_Are_Lowercase()
    {
        var fields = typeof(ToolNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.DoesNotContain(value, static ch => char.IsLetter(ch) && char.IsUpper(ch));
        }
    }

    [Fact]
    public void All_ToolNames_Are_Unique()
    {
        var fields = typeof(ToolNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void SessionListOpenDocuments_Has_Session_Prefix()
    {
        Assert.StartsWith("session.", ToolNames.SessionListOpenDocuments);
    }

    [Fact]
    public void ElementQuery_Has_Element_Prefix()
    {
        Assert.StartsWith("element.", ToolNames.ElementQuery);
    }

    [Fact]
    public void FileSaveDocument_Has_File_Prefix()
    {
        Assert.StartsWith("file.", ToolNames.FileSaveDocument);
    }

    [Fact]
    public void ReviewModelWarnings_Has_Review_Prefix()
    {
        Assert.StartsWith("review.", ToolNames.ReviewModelWarnings);
    }

    [Fact]
    public void WorkflowList_Has_Workflow_Prefix()
    {
        Assert.StartsWith("workflow.", ToolNames.WorkflowList);
    }

    [Fact]
    public void ElementExplain_Has_Element_Prefix()
    {
        Assert.StartsWith("element.", ToolNames.ElementExplain);
    }

    [Fact]
    public void ParameterTrace_Has_Parameter_Prefix()
    {
        Assert.StartsWith("parameter.", ToolNames.ParameterTrace);
    }

    [Fact]
    public void ReviewFamilyAxisAlignmentGlobal_Has_Review_Prefix()
    {
        Assert.StartsWith("review.", ToolNames.ReviewFamilyAxisAlignmentGlobal);
    }

    [Fact]
    public void ReportRoundExternalizationPlan_Has_Report_Prefix()
    {
        Assert.StartsWith("report.", ToolNames.ReportRoundExternalizationPlan);
    }

    [Fact]
    public void FamilyBuildRoundProjectWrappersSafe_Has_Family_Prefix()
    {
        Assert.StartsWith("family.", ToolNames.FamilyBuildRoundProjectWrappersSafe);
    }

    [Fact]
    public void WorkflowFixLoopPlan_Has_Workflow_Prefix()
    {
        Assert.StartsWith("workflow.", ToolNames.WorkflowFixLoopPlan);
    }

    [Fact]
    public void FamilyLoadSafe_Has_Family_Prefix()
    {
        Assert.StartsWith("family.", ToolNames.FamilyLoadSafe);
    }

    [Fact]
    public void ExportIfcSafe_Has_Export_Prefix()
    {
        Assert.StartsWith("export.", ToolNames.ExportIfcSafe);
    }
}
