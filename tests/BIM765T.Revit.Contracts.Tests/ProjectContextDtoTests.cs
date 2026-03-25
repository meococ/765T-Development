using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class ProjectContextDtoTests
{
    [Fact]
    public void ProjectInitApplyResponse_RoundTrips_With_ContextBundle()
    {
        var response = new ProjectInitApplyResponse
        {
            StatusCode = "OK",
            WorkspaceId = "alpha-project",
            WorkspaceRootPath = @"D:\repo\workspaces\alpha-project",
            Manifest = new ProjectSourceManifest
            {
                SourceRootPath = @"D:\incoming\alpha",
                PrimaryRevitFilePath = @"D:\incoming\alpha\Model_A.rvt",
                Summary = "1 rvt, 1 pdf",
                Files = new List<ProjectSourceFile>
                {
                    new ProjectSourceFile
                    {
                        SourcePath = @"D:\incoming\alpha\Model_A.rvt",
                        RelativePath = "Model_A.rvt",
                        FileName = "Model_A.rvt",
                        SourceKind = ProjectSourceKinds.RevitProject,
                        LastWriteUtc = new DateTime(2026, 3, 21, 9, 0, 0, DateTimeKind.Utc),
                        IsPrimaryCandidate = true,
                        IsSelectedPrimary = true,
                        Summary = "Primary model",
                        RevitVersion = "2025",
                        IsWorkshared = true,
                        WorksharingSummary = "Workshared (Central file)"
                    }
                },
                GeneratedUtc = new DateTime(2026, 3, 21, 9, 0, 0, DateTimeKind.Utc),
                Stats = new ProjectManifestStats
                {
                    TotalFiles = 2,
                    RevitProjectCount = 1,
                    PdfCount = 1,
                    PrimaryRevitFilePath = @"D:\incoming\alpha\Model_A.rvt"
                }
            },
            ManifestStats = new ProjectManifestStats
            {
                TotalFiles = 2,
                RevitProjectCount = 1,
                PdfCount = 1,
                PrimaryRevitFilePath = @"D:\incoming\alpha\Model_A.rvt"
            },
            WorkspaceManifestPath = @"D:\repo\workspaces\alpha-project\workspace.json",
            ProjectContextPath = @"D:\repo\workspaces\alpha-project\project.context.json",
            ManifestReportPath = @"D:\repo\workspaces\alpha-project\reports\project-init.manifest.json",
            SummaryReportPath = @"D:\repo\workspaces\alpha-project\reports\project-init.summary.md",
            ProjectBriefPath = @"D:\repo\workspaces\alpha-project\memory\project-brief.md",
            PrimaryModelReportPath = @"D:\repo\workspaces\alpha-project\reports\project-init.primary-model.json",
            PrimaryModelStatus = ProjectPrimaryModelStatuses.PendingLiveSummary,
            Summary = "Workspace alpha-project da duoc bootstrap.",
            ContextBundle = new ProjectContextBundleResponse
            {
                StatusCode = "OK",
                Exists = true,
                WorkspaceId = "alpha-project",
                Summary = "Bundle san sang.",
                ProjectBrief = "Firm doctrine pending",
                ManifestStats = new ProjectManifestStats
                {
                    TotalFiles = 2,
                    RevitProjectCount = 1,
                    PdfCount = 1
                },
                PrimaryModelStatus = ProjectPrimaryModelStatuses.PendingLiveSummary,
                TopStandardsRefs = new List<ProjectContextRef>
                {
                    new ProjectContextRef
                    {
                        RefId = "templates.json#view_templates.architectural_plan",
                        Title = "architectural_plan",
                        RefKind = "standard_ref",
                        SourcePath = "templates.json",
                        RelativePath = "assets/templates.json",
                        Summary = "Resolved by firm pack",
                        SourcePackId = "contoso.standards.firm"
                    }
                },
                PendingUnknowns = new List<string> { "Firm doctrine pending" }
            }
        };

        var json = JsonUtil.Serialize(response);
        var restored = JsonUtil.DeserializeRequired<ProjectInitApplyResponse>(json);

        Assert.Equal("alpha-project", restored.WorkspaceId);
        Assert.Equal(2, restored.ManifestStats.TotalFiles);
        Assert.Equal(ProjectPrimaryModelStatuses.PendingLiveSummary, restored.PrimaryModelStatus);
        Assert.True(restored.ContextBundle.Exists);
        Assert.Equal("2025", restored.Manifest.Files[0].RevitVersion);
        Assert.True(restored.Manifest.Files[0].IsWorkshared);
        Assert.Equal("Workshared (Central file)", restored.Manifest.Files[0].WorksharingSummary);
        Assert.Equal("contoso.standards.firm", restored.ContextBundle.TopStandardsRefs[0].SourcePackId);
        Assert.Contains("Firm doctrine pending", restored.ContextBundle.PendingUnknowns);
    }

    [Fact]
    public void WorkerContextResponse_RoundTrips_ProjectContextFields()
    {
        var response = new WorkerContextResponse
        {
            SessionId = "session-1",
            WorkspaceId = "alpha-project",
            ProjectSummary = "Project init da bootstrap 3 file.",
            ProjectBrief = "Primary model pending live summary.",
            ProjectPrimaryModelStatus = ProjectPrimaryModelStatuses.PendingLiveSummary,
            ProjectTopRefs = new List<string>
            {
                "source:1 | Model_A.rvt | D:\\incoming\\alpha\\Model_A.rvt"
            },
            ProjectPendingUnknowns = new List<string>
            {
                "Primary model live summary pending"
            }
        };

        var json = JsonUtil.Serialize(response);
        var restored = JsonUtil.DeserializeRequired<WorkerContextResponse>(json);

        Assert.Equal("alpha-project", restored.WorkspaceId);
        Assert.Equal("Project init da bootstrap 3 file.", restored.ProjectSummary);
        Assert.Equal(ProjectPrimaryModelStatuses.PendingLiveSummary, restored.ProjectPrimaryModelStatus);
        Assert.Single(restored.ProjectTopRefs);
        Assert.Contains("Model_A.rvt", restored.ProjectTopRefs[0]);
        Assert.Contains("Primary model live summary pending", restored.ProjectPendingUnknowns);
    }

    [Fact]
    public void ProjectDeepScanResponse_RoundTrips_With_Report()
    {
        var response = new ProjectDeepScanResponse
        {
            StatusCode = "OK",
            WorkspaceId = "alpha-project",
            WorkspaceRootPath = @"D:\repo\workspaces\alpha-project",
            ReportPath = @"D:\repo\workspaces\alpha-project\reports\project-brain.deep-scan.json",
            SummaryReportPath = @"D:\repo\workspaces\alpha-project\reports\project-brain.deep-scan.summary.md",
            Summary = "Deep scan completed.",
            Report = new ProjectDeepScanReport
            {
                WorkspaceId = "alpha-project",
                Status = ProjectDeepScanStatuses.Completed,
                Summary = "Deep scan completed.",
                PrimaryRevitFilePath = @"D:\incoming\alpha\Model_A.rvt",
                Stats = new ProjectDeepScanStats
                {
                    DocumentsRequested = 1,
                    DocumentsScanned = 1,
                    FindingCount = 2
                },
                Findings = new List<ProjectDeepScanFinding>
                {
                    new ProjectDeepScanFinding
                    {
                        FindingId = "smartqc:1",
                        Category = "sheet",
                        Title = "Missing parameter",
                        Message = "Sheet parameter is empty.",
                        SourceTool = "review.smart_qc"
                    }
                }
            },
            ContextBundle = new ProjectContextBundleResponse
            {
                StatusCode = "OK",
                Exists = true,
                WorkspaceId = "alpha-project",
                DeepScanStatus = ProjectDeepScanStatuses.Completed,
                DeepScanSummary = "Deep scan completed.",
                DeepScanFindingCount = 2
            }
        };

        var json = JsonUtil.Serialize(response);
        var restored = JsonUtil.DeserializeRequired<ProjectDeepScanResponse>(json);

        Assert.Equal("alpha-project", restored.WorkspaceId);
        Assert.Equal(ProjectDeepScanStatuses.Completed, restored.Report.Status);
        Assert.Equal(2, restored.ContextBundle.DeepScanFindingCount);
        Assert.Single(restored.Report.Findings);
    }
}
