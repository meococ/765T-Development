using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class ProjectInitServicesTests
{
    [Fact]
    public void Preview_Requires_Primary_Model_When_Multiple_Rvt_Files_Exist()
    {
        using var repo = ProjectInitTestRepo.Create(revitProjectCount: 2, includeFirmStandards: false);
        var services = CreateServices(repo.RepoRoot);

        var preview = services.ProjectInit.Preview(new ProjectInitPreviewRequest
        {
            SourceRootPath = repo.SourceRoot
        });

        Assert.False(preview.IsValid);
        Assert.True(preview.RequiresPrimaryRevitSelection);
        Assert.Contains(preview.Errors, x => x.Contains("PrimaryRevitFilePath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_Creates_Curated_Workspace_Files_Without_Copying_Source_Binaries()
    {
        using var repo = ProjectInitTestRepo.Create(revitProjectCount: 1, includeFirmStandards: false);
        var services = CreateServices(repo.RepoRoot);

        var response = services.ProjectInit.Apply(new ProjectInitApplyRequest
        {
            SourceRootPath = repo.SourceRoot,
            WorkspaceId = "alpha-project",
            DisplayName = "Alpha Project",
            PrimaryRevitFilePath = repo.PrimaryModelPath,
            IncludeLivePrimaryModelSummary = false
        });

        Assert.Equal(StatusCodes.Ok, response.StatusCode);
        Assert.True(File.Exists(response.WorkspaceManifestPath));
        Assert.True(File.Exists(response.ProjectContextPath));
        Assert.True(File.Exists(response.ManifestReportPath));
        Assert.True(File.Exists(response.SummaryReportPath));
        Assert.True(File.Exists(response.ProjectBriefPath));
        Assert.True(File.Exists(response.PrimaryModelReportPath));
        Assert.Equal(ProjectPrimaryModelStatuses.NotRequested, response.PrimaryModelStatus);

        var copiedSourceFiles = Directory.GetFiles(response.WorkspaceRootPath, "*.rvt", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(response.WorkspaceRootPath, "*.rfa", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(response.WorkspaceRootPath, "*.pdf", SearchOption.AllDirectories))
            .ToList();
        Assert.Empty(copiedSourceFiles);

        var manifest = JsonUtil.DeserializeRequired<ProjectSourceManifest>(File.ReadAllText(response.ManifestReportPath));
        Assert.Equal(repo.PrimaryModelPath, manifest.PrimaryRevitFilePath);
        Assert.DoesNotContain(manifest.Files, x => x.Summary.IndexOf("raw text", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Preview_Detects_Revit_Source_Metadata_Without_Opening_Files()
    {
        using var repo = ProjectInitTestRepo.Create(revitProjectCount: 1, includeFirmStandards: false);
        var services = CreateServices(repo.RepoRoot);

        var preview = services.ProjectInit.Preview(new ProjectInitPreviewRequest
        {
            SourceRootPath = repo.SourceRoot,
            PrimaryRevitFilePath = repo.PrimaryModelPath
        });

        Assert.True(preview.IsValid);

        var model = Assert.Single(preview.Manifest.Files, x => string.Equals(x.Extension, ".rvt", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2024", model.RevitVersion);
        Assert.True(model.IsWorkshared);
        Assert.Equal("Workshared (Central file)", model.WorksharingSummary);
        Assert.Contains("Revit 2024", model.Summary, StringComparison.Ordinal);

        var family = Assert.Single(preview.Manifest.Files, x => string.Equals(x.Extension, ".rfa", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2025", family.RevitVersion);
        Assert.Null(family.IsWorkshared);
        Assert.Equal(string.Empty, family.WorksharingSummary);
        Assert.Contains("Revit 2025", family.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextBundle_Prefers_Firm_Standards_And_Adds_Similar_Run_Refs()
    {
        using var repo = ProjectInitTestRepo.Create(revitProjectCount: 1, includeFirmStandards: true);
        var services = CreateServices(repo.RepoRoot);

        var apply = services.ProjectInit.Apply(new ProjectInitApplyRequest
        {
            SourceRootPath = repo.SourceRoot,
            WorkspaceId = "alpha-project",
            DisplayName = "Alpha Project",
            PrimaryRevitFilePath = repo.PrimaryModelPath,
            FirmPackIds = new List<string> { repo.FirmStandardsPackId },
            IncludeLivePrimaryModelSummary = false
        });

        var bundle = services.ProjectContextComposer.GetContextBundle(
            new ProjectContextBundleRequest
            {
                WorkspaceId = apply.WorkspaceId,
                Query = "project init review",
                MaxSourceRefs = 4,
                MaxStandardsRefs = 2
            },
            new[]
            {
                new ToolManifest { ToolName = ToolNames.StandardsResolve },
                new ToolManifest { ToolName = ToolNames.WorkspaceGetManifest }
            },
            _ => new MemoryFindSimilarRunsResponse
            {
                Runs = new List<SimilarTaskRunItem>
                {
                    new SimilarTaskRunItem
                    {
                        RunId = "run-001",
                        TaskKind = "project_init",
                        TaskName = "alpha-project",
                        Status = "completed",
                        Summary = "Bootstrap workspace complete."
                    }
                }
            });

        Assert.True(bundle.Exists);
        Assert.Equal("core_safety", Assert.IsType<List<ProjectLayerSummary>>(bundle.LayerSummaries)[0].LayerKey);
        Assert.Equal(repo.FirmStandardsPackId, Assert.Single(bundle.TopStandardsRefs).SourcePackId);
        Assert.Equal("project_init_review.v1", bundle.RecommendedPlaybookId);
        Assert.Contains(bundle.SourceRefs, x => string.Equals(x.RefKind, "similar_run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HostService_Apply_Succeeds_When_Kernel_Is_Unavailable_And_Marks_Primary_As_Pending()
    {
        using var repo = ProjectInitTestRepo.Create(revitProjectCount: 1, includeFirmStandards: false);
        var services = CreateServices(repo.RepoRoot);
        var host = new ProjectInitHostService(
            services.ProjectInit,
            services.ProjectContextComposer,
            new ThrowingKernelClient(),
            NullLogger<ProjectInitHostService>.Instance);

        var response = await host.ApplyAsync(new ProjectInitApplyRequest
        {
            SourceRootPath = repo.SourceRoot,
            WorkspaceId = "alpha-project",
            PrimaryRevitFilePath = repo.PrimaryModelPath,
            IncludeLivePrimaryModelSummary = true
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Ok, response.StatusCode);
        Assert.Equal(ProjectPrimaryModelStatuses.PendingLiveSummary, response.PrimaryModelStatus);
        Assert.True(response.ContextBundle.Exists);
        Assert.True(File.Exists(response.PrimaryModelReportPath));
    }

    [Fact]
    public async Task DeepScan_HostService_Creates_Report_And_Enriches_Context()
    {
        using var repo = ProjectInitTestRepo.Create(revitProjectCount: 1, includeFirmStandards: false);
        var services = CreateServices(repo.RepoRoot);
        var apply = services.ProjectInit.Apply(new ProjectInitApplyRequest
        {
            SourceRootPath = repo.SourceRoot,
            WorkspaceId = "alpha-project",
            DisplayName = "Alpha Project",
            PrimaryRevitFilePath = repo.PrimaryModelPath,
            IncludeLivePrimaryModelSummary = false
        });

        Assert.Equal(ProjectOnboardingStatuses.Initialized, apply.OnboardingStatus.InitStatus);
        Assert.Equal(ProjectDeepScanStatuses.NotStarted, apply.OnboardingStatus.DeepScanStatus);
        Assert.True(apply.OnboardingStatus.ResumeEligible);

        var host = new ProjectDeepScanHostService(
            services.ProjectDeepScan,
            services.ProjectContextComposer,
            new FakeDeepScanKernelClient(repo.PrimaryModelPath),
            NullLogger<ProjectDeepScanHostService>.Instance);

        var response = await host.RunAsync(new ProjectDeepScanRequest
        {
            WorkspaceId = "alpha-project",
            MaxDocuments = 1,
            MaxSheets = 2,
            MaxSheetIntelligence = 1,
            MaxSchedulesPerSheet = 1,
            MaxScheduleRows = 10,
            MaxFindings = 10,
            IncludeScheduleData = true,
            ForceRescan = true
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Ok, response.StatusCode);
        Assert.True(File.Exists(response.ReportPath));
        Assert.Equal(ProjectDeepScanStatuses.Completed, response.Report.Status);
        Assert.True(response.Report.Stats.DocumentsScanned >= 1);
        Assert.True(response.Report.Findings.Count >= 1);
        Assert.Equal(ProjectDeepScanStatuses.Completed, response.ContextBundle.DeepScanStatus);
        Assert.True(response.ContextBundle.DeepScanFindingCount >= 1);
        Assert.Equal(ProjectOnboardingStatuses.Initialized, response.OnboardingStatus.InitStatus);
        Assert.Equal(ProjectDeepScanStatuses.Completed, response.OnboardingStatus.DeepScanStatus);
        Assert.True(response.OnboardingStatus.ResumeEligible);
    }

    private static (ProjectInitService ProjectInit, ProjectDeepScanService ProjectDeepScan, ProjectContextComposer ProjectContextComposer) CreateServices(string repoRoot)
    {
        var packs = new PackCatalogService(repoRoot);
        var workspaces = new WorkspaceCatalogService(repoRoot);
        var standards = new StandardsCatalogService(packs, workspaces);
        var playbooks = new PlaybookLoaderService(PlaybookLoaderService.LoadAll(repoRoot));
        var orchestration = new PlaybookOrchestrationService(playbooks, packs, workspaces, standards);
        var projectInit = new ProjectInitService(packs, workspaces, repoRoot);
        var projectDeepScan = new ProjectDeepScanService(projectInit, repoRoot);
        var composer = new ProjectContextComposer(projectInit, workspaces, standards, orchestration, repoRoot);
        return (projectInit, projectDeepScan, composer);
    }

    private sealed class ThrowingKernelClient : IKernelClient
    {
        public Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Kernel unavailable for test.");
        }
    }

    private sealed class FakeDeepScanKernelClient : IKernelClient
    {
        private readonly string _primaryModelPath;
        private readonly string _documentKey;

        public FakeDeepScanKernelClient(string primaryModelPath)
        {
            _primaryModelPath = primaryModelPath;
            _documentKey = "doc:alpha";
        }

        public Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
        {
            var result = request.ToolName switch
            {
                ToolNames.DocumentOpenBackgroundRead => Success(new DocumentSummaryDto
                {
                    DocumentKey = _documentKey,
                    Title = "Model_1",
                    PathName = _primaryModelPath,
                    IsWorkshared = true
                }),
                ToolNames.ReviewModelHealth => Success(new ModelHealthResponse
                {
                    DocumentKey = _documentKey,
                    TotalWarnings = 3,
                    Review = new ReviewReport
                    {
                        IssueCount = 1,
                        Issues = new List<ReviewIssue>
                        {
                            new ReviewIssue
                            {
                                Code = "WARNINGS_PRESENT",
                                Severity = DiagnosticSeverity.Warning,
                                Message = "Warnings detected."
                            }
                        }
                    }
                }),
                ToolNames.ReviewLinksStatus => Success(new LinksStatusResponse
                {
                    DocumentKey = _documentKey,
                    TotalLinks = 2,
                    LoadedLinks = 1
                }),
                ToolNames.ReviewWorksetHealth => Success(new WorksetHealthResponse
                {
                    DocumentKey = _documentKey,
                    IsWorkshared = true,
                    TotalWorksets = 4,
                    OpenWorksets = 3,
                    Review = new ReviewReport()
                }),
                ToolNames.SheetListAll => Success(new SheetListResponse
                {
                    DocumentKey = _documentKey,
                    Count = 1,
                    Sheets = new List<SheetItem>
                    {
                        new SheetItem
                        {
                            Id = 101,
                            SheetNumber = "A101",
                            SheetName = "General Notes"
                        }
                    }
                }),
                ToolNames.ReviewSmartQc => Success(new SmartQcResponse
                {
                    DocumentKey = _documentKey,
                    RulesetName = "base-rules",
                    FindingCount = 1,
                    Summary = "1 finding.",
                    Findings = new List<SmartQcFinding>
                    {
                        new SmartQcFinding
                        {
                            RuleId = "sheet.param",
                            Title = "Missing parameter",
                            Category = "sheet",
                            Severity = DiagnosticSeverity.Warning,
                            Message = "Missing issue date.",
                            SheetId = 101,
                            SourceTool = ToolNames.ReviewSmartQc
                        }
                    }
                }),
                ToolNames.ReviewSheetSummary => Success(new SheetSummaryResponse
                {
                    DocumentKey = _documentKey,
                    SheetId = 101,
                    SheetNumber = "A101",
                    SheetName = "General Notes",
                    ViewportCount = 2,
                    ScheduleInstanceCount = 1,
                    Review = new ReviewReport()
                }),
                ToolNames.SheetCaptureIntelligence => Success(new SheetCaptureIntelligenceResponse
                {
                    DocumentKey = _documentKey,
                    SheetId = 101,
                    SheetNumber = "A101",
                    SheetName = "General Notes",
                    Summary = "Captured sheet intelligence.",
                    LayoutMap = "VP1 | VP2",
                    Schedules = new List<SheetScheduleIntelligence>
                    {
                        new SheetScheduleIntelligence
                        {
                            ScheduleInstanceId = 9001,
                            ScheduleViewId = 9101,
                            ScheduleName = "Door Schedule",
                            RowCount = 12,
                            ColumnCount = 4
                        }
                    }
                }),
                ToolNames.DataExtractScheduleStructured => Success(new ScheduleExtractionResponse
                {
                    DocumentKey = _documentKey,
                    ScheduleId = 9101,
                    ScheduleName = "Door Schedule",
                    ReturnedRowCount = 10,
                    TotalRowCount = 12,
                    Summary = "Schedule sample extracted."
                }),
                ToolNames.DocumentCloseNonActive => new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new { closed = true })
                },
                _ => new KernelInvocationResult
                {
                    Succeeded = false,
                    StatusCode = StatusCodes.UnsupportedTool,
                    PayloadJson = string.Empty
                }
            };

            return Task.FromResult(result);
        }

        private static KernelInvocationResult Success<T>(T payload)
        {
            return new KernelInvocationResult
            {
                Succeeded = true,
                StatusCode = StatusCodes.Ok,
                PayloadJson = JsonUtil.Serialize(payload)
            };
        }
    }

    private sealed class ProjectInitTestRepo : IDisposable
    {
        private ProjectInitTestRepo(string repoRoot, string sourceRoot, string primaryModelPath, string firmStandardsPackId)
        {
            RepoRoot = repoRoot;
            SourceRoot = sourceRoot;
            PrimaryModelPath = primaryModelPath;
            FirmStandardsPackId = firmStandardsPackId;
        }

        public string RepoRoot { get; }

        public string SourceRoot { get; }

        public string PrimaryModelPath { get; }

        public string FirmStandardsPackId { get; }

        public static ProjectInitTestRepo Create(int revitProjectCount, bool includeFirmStandards)
        {
            var repoRoot = Path.Combine(Path.GetTempPath(), "BIM765T.ProjectInit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repoRoot);
            File.WriteAllText(Path.Combine(repoRoot, "BIM765T.Revit.Agent.sln"), string.Empty);

            var workspacesRoot = Path.Combine(repoRoot, "workspaces", "default");
            Directory.CreateDirectory(workspacesRoot);
            WriteJson(
                Path.Combine(workspacesRoot, "workspace.json"),
                new WorkspaceManifest
                {
                    WorkspaceId = "default",
                    DisplayName = "Default Workspace",
                    EnabledPacks = new List<string>
                    {
                        "bim765t.standards.core",
                        "bim765t.playbooks.core",
                        "bim765t.skills.core"
                    },
                    PreferredStandardsPacks = new List<string> { "bim765t.standards.core" },
                    PreferredPlaybookPacks = new List<string> { "bim765t.playbooks.core" },
                    AllowedAgents = new List<string> { "orchestrator" },
                    AllowedSpecialists = new List<string> { "audit", "sheet" }
                });

            WritePack(
                repoRoot,
                "standards",
                "core",
                new PackManifest
                {
                    PackType = "standards-pack",
                    PackId = "bim765t.standards.core",
                    DisplayName = "Core Standards",
                    Exports = new List<PackExport>
                    {
                        new PackExport
                        {
                            ExportKind = "standard",
                            ExportId = "templates",
                            RelativePath = Path.Combine("assets", "templates.json")
                        }
                    }
                });
            File.WriteAllText(
                Path.Combine(repoRoot, "packs", "standards", "core", "assets", "templates.json"),
                "{\"view_templates\":{\"architectural_plan\":\"CORE_TEMPLATE\"}}");

            var firmStandardsPackId = includeFirmStandards ? "contoso.standards.firm" : string.Empty;
            if (includeFirmStandards)
            {
                WritePack(
                    repoRoot,
                    "standards",
                    "firm",
                    new PackManifest
                    {
                        PackType = "standards-pack",
                        PackId = firmStandardsPackId,
                        DisplayName = "Contoso Firm Standards",
                        Exports = new List<PackExport>
                        {
                            new PackExport
                            {
                                ExportKind = "standard",
                                ExportId = "templates",
                                RelativePath = Path.Combine("assets", "templates.json")
                            }
                        }
                    });
                File.WriteAllText(
                    Path.Combine(repoRoot, "packs", "standards", "firm", "assets", "templates.json"),
                    "{\"view_templates\":{\"architectural_plan\":\"FIRM_TEMPLATE\"}}");
            }

            WritePack(
                repoRoot,
                "playbooks",
                "core",
                new PackManifest
                {
                    PackType = "playbook-pack",
                    PackId = "bim765t.playbooks.core",
                    DisplayName = "Core Playbooks"
                });
            WriteJson(
                Path.Combine(repoRoot, "packs", "playbooks", "core", "assets", "project_init_review.v1.json"),
                new PlaybookDefinition
                {
                    PlaybookId = "project_init_review.v1",
                    Description = "Review project init context bundle.",
                    Lane = "project_init",
                    RequiredContext = "project",
                    PackId = "bim765t.playbooks.core",
                    TriggerPhrases = new List<string> { "project init", "project review" },
                    StandardsRefs = new List<string> { "templates.json#view_templates.architectural_plan" },
                    RequiredInputs = new List<string> { "workspace_id" },
                    Steps = new List<PlaybookStepDefinition>
                    {
                        new PlaybookStepDefinition
                        {
                            StepName = "Resolve standards",
                            StepId = "standards",
                            StepKind = "standards",
                            Tool = ToolNames.StandardsResolve,
                            Purpose = "Resolve workspace standards.",
                            OutputKey = "standards"
                        }
                    },
                    DecisionGate = new PlaybookDecisionGate
                    {
                        UseWhen = new List<string> { "project init", "project review" }
                    }
                });

            WritePack(
                repoRoot,
                "skills",
                "core",
                new PackManifest
                {
                    PackType = "skill-pack",
                    PackId = "bim765t.skills.core",
                    DisplayName = "Core Skills"
                });
            File.WriteAllText(Path.Combine(repoRoot, "packs", "skills", "core", "assets", "README.md"), "# Core skills");

            var sourceRoot = Path.Combine(repoRoot, "incoming", "AlphaProject");
            Directory.CreateDirectory(sourceRoot);

            string primaryModelPath = string.Empty;
            for (var i = 1; i <= Math.Max(1, revitProjectCount); i++)
            {
                var modelPath = Path.Combine(sourceRoot, $"Model_{i}.rvt");
                var version = 2025 - i;
                var worksharing = i == 1 ? "Central file" : "Not workshared";
                File.WriteAllText(modelPath, $"Format: {version}{Environment.NewLine}Worksharing: {worksharing}{Environment.NewLine}");
                if (i == 1)
                {
                    primaryModelPath = modelPath;
                }
            }

            File.WriteAllText(Path.Combine(sourceRoot, "Family_A.rfa"), "<root><product-version>2025</product-version></root>");
            File.WriteAllText(Path.Combine(sourceRoot, "Project_Standard.pdf"), "pdf-binary-placeholder");

            return new ProjectInitTestRepo(repoRoot, sourceRoot, primaryModelPath, firmStandardsPackId);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RepoRoot))
                {
                    Directory.Delete(RepoRoot, recursive: true);
                }
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }

        private static void WritePack(string repoRoot, string packTypeFolder, string packFolder, PackManifest manifest)
        {
            var root = Path.Combine(repoRoot, "packs", packTypeFolder, packFolder);
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            WriteJson(Path.Combine(root, "pack.json"), manifest);
        }

        private static void WriteJson<T>(string path, T payload)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            File.WriteAllText(path, JsonUtil.Serialize(payload));
        }
    }
}
