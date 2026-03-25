using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Validation;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class ProjectInitValidatorTests
{
    [Fact]
    public void Validate_Throws_For_Invalid_ProjectInitPreview()
    {
        var request = new ProjectInitPreviewRequest();

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));

        Assert.Contains(ex.Diagnostics, d => d.Code == "PROJECT_SOURCE_ROOT_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_Invalid_ProjectContextBundle()
    {
        var request = new ProjectContextBundleRequest
        {
            WorkspaceId = string.Empty,
            MaxSourceRefs = 0,
            MaxStandardsRefs = 0
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));

        Assert.Contains(ex.Diagnostics, d => d.Code == "PROJECT_WORKSPACE_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "PROJECT_MAX_SOURCE_REFS_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "PROJECT_MAX_STANDARDS_REFS_INVALID");
    }

    [Fact]
    public void Validate_Allows_Valid_ProjectInitApply()
    {
        var request = new ProjectInitApplyRequest
        {
            SourceRootPath = @"D:\Projects\SampleA",
            WorkspaceId = "sample-a",
            PrimaryRevitFilePath = @"D:\Projects\SampleA\Model_A.rvt",
            IncludeLivePrimaryModelSummary = false
        };

        ToolPayloadValidator.Validate(request);
    }

    [Fact]
    public void Validate_Throws_For_Invalid_ProjectDeepScan()
    {
        var request = new ProjectDeepScanRequest
        {
            WorkspaceId = string.Empty,
            MaxDocuments = 0,
            MaxSheets = 0,
            MaxSheetIntelligence = 0,
            MaxSchedulesPerSheet = 0,
            MaxScheduleRows = 0,
            MaxFindings = 0
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));

        Assert.Contains(ex.Diagnostics, d => d.Code == "PROJECT_WORKSPACE_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "PROJECT_DEEP_SCAN_MAX_DOCUMENTS_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "PROJECT_DEEP_SCAN_MAX_FINDINGS_INVALID");
    }
}
