using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ProjectDeepScanService
{
    private readonly ProjectInitService _projectInit;
    private readonly string _workspacesRoot;

    public ProjectDeepScanService(ProjectInitService projectInit, string? baseDirectory = null, string? workspaceRootPath = null)
    {
        _projectInit = projectInit ?? throw new ArgumentNullException(nameof(projectInit));
        _workspacesRoot = RepoLayoutService.ResolveWorkspacesRoot(workspaceRootPath, baseDirectory);
    }

    public ProjectDeepScanPlan Prepare(ProjectDeepScanRequest request)
    {
        request ??= new ProjectDeepScanRequest();
        var workspaceId = ProjectInitService.NormalizeWorkspaceId(request.WorkspaceId);
        var workspaceRoot = Path.Combine(_workspacesRoot, workspaceId);
        var contextPath = Path.Combine(workspaceRoot, "project.context.json");
        var reportPath = Path.Combine(workspaceRoot, "reports", "project-brain.deep-scan.json");
        var summaryReportPath = Path.Combine(workspaceRoot, "reports", "project-brain.deep-scan.summary.md");

        var context = ProjectInitService.ReadJson<ProjectContextState>(contextPath);
        if (context == null)
        {
            throw new InvalidOperationException(StatusCodes.ProjectContextNotInitialized);
        }

        var manifestResponse = _projectInit.GetManifest(new ProjectManifestRequest { WorkspaceId = workspaceId });
        if (!manifestResponse.Exists || manifestResponse.Manifest == null)
        {
            throw new InvalidOperationException(StatusCodes.ProjectManifestNotFound);
        }

        var manifest = manifestResponse.Manifest;
        manifest.Stats ??= manifestResponse.ManifestStats ?? new ProjectManifestStats();
        var primaryPath = ProjectInitService.NormalizeExistingPath(context.PrimaryRevitFilePath)
            ?? ProjectInitService.NormalizeExistingPath(manifest.PrimaryRevitFilePath)
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(primaryPath))
        {
            throw new InvalidOperationException(StatusCodes.ProjectDeepScanPrimaryModelMissing);
        }

        var revitProjects = (manifest.Files ?? new List<ProjectSourceFile>())
            .Where(x => string.Equals(x.SourceKind, ProjectSourceKinds.RevitProject, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.SourcePath))
            .ToList();

        var targetFiles = request.IncludeSecondaryRevitProjects
            ? revitProjects
                .OrderByDescending(x => ProjectInitService.PathsEqual(x.SourcePath, primaryPath))
                .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, request.MaxDocuments))
                .Select(x => x.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string> { primaryPath };

        if (request.IncludeSecondaryRevitProjects
            && !targetFiles.Any(x => ProjectInitService.PathsEqual(x, primaryPath)))
        {
            targetFiles.Insert(0, primaryPath);
        }

        targetFiles = targetFiles
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxDocuments))
            .ToList();

        return new ProjectDeepScanPlan(
            workspaceId,
            workspaceRoot,
            contextPath,
            reportPath,
            summaryReportPath,
            context,
            manifest,
            manifest.Stats,
            primaryPath,
            targetFiles);
    }

    public ProjectDeepScanResponse Save(ProjectDeepScanRequest request, ProjectDeepScanReport report)
    {
        request ??= new ProjectDeepScanRequest();
        report ??= new ProjectDeepScanReport();
        var plan = Prepare(request);

        report.WorkspaceId = plan.WorkspaceId;
        report.WorkspaceRootPath = plan.WorkspaceRoot;
        report.PrimaryRevitFilePath = string.IsNullOrWhiteSpace(report.PrimaryRevitFilePath) ? plan.PrimaryRevitFilePath : report.PrimaryRevitFilePath;
        report.GeneratedUtc = report.GeneratedUtc == default ? DateTime.UtcNow : report.GeneratedUtc;
        report.ManifestStats ??= plan.ManifestStats ?? new ProjectManifestStats();
        report.Stats = RecalculateStats(report);
        report.Status = ResolveStatus(report);
        report.Summary = BuildSummary(report);

        Directory.CreateDirectory(Path.Combine(plan.WorkspaceRoot, "reports"));
        ProjectInitService.WriteJson(plan.ReportPath, report);
        ProjectInitService.WriteMarkdown(plan.SummaryReportPath, BuildMarkdown(plan, report));
        UpdateProjectContext(plan, report);

        return new ProjectDeepScanResponse
        {
            StatusCode = StatusCodes.Ok,
            WorkspaceId = plan.WorkspaceId,
            WorkspaceRootPath = plan.WorkspaceRoot,
            ReportPath = plan.ReportPath,
            SummaryReportPath = plan.SummaryReportPath,
            Report = report,
            Summary = report.Summary,
            OnboardingStatus = _projectInit.GetOnboardingStatus(plan.WorkspaceId)
        };
    }

    public ProjectDeepScanReportResponse GetReport(ProjectDeepScanGetRequest request)
    {
        request ??= new ProjectDeepScanGetRequest();
        var workspaceId = ProjectInitService.NormalizeWorkspaceId(request.WorkspaceId);
        var workspaceRoot = Path.Combine(_workspacesRoot, workspaceId);
        var reportPath = Path.Combine(workspaceRoot, "reports", "project-brain.deep-scan.json");
        var summaryReportPath = Path.Combine(workspaceRoot, "reports", "project-brain.deep-scan.summary.md");
        if (!File.Exists(reportPath))
        {
            return new ProjectDeepScanReportResponse
            {
                StatusCode = StatusCodes.ProjectDeepScanReportNotFound,
                Exists = false,
                WorkspaceId = workspaceId,
                WorkspaceRootPath = workspaceRoot,
                ReportPath = reportPath,
                SummaryReportPath = summaryReportPath,
                Summary = "Workspace chua co project-brain deep scan report.",
                OnboardingStatus = _projectInit.GetOnboardingStatus(workspaceId)
            };
        }

        var report = ProjectInitService.ReadJson<ProjectDeepScanReport>(reportPath) ?? new ProjectDeepScanReport();
        report.WorkspaceId = string.IsNullOrWhiteSpace(report.WorkspaceId) ? workspaceId : report.WorkspaceId;
        report.WorkspaceRootPath = string.IsNullOrWhiteSpace(report.WorkspaceRootPath) ? workspaceRoot : report.WorkspaceRootPath;
        report.Stats = RecalculateStats(report);
        report.Status = ResolveStatus(report);
        report.Summary = string.IsNullOrWhiteSpace(report.Summary) ? BuildSummary(report) : report.Summary;
        return new ProjectDeepScanReportResponse
        {
            StatusCode = StatusCodes.Ok,
            Exists = true,
            WorkspaceId = workspaceId,
            WorkspaceRootPath = workspaceRoot,
            ReportPath = reportPath,
            SummaryReportPath = summaryReportPath,
            Report = report,
            Summary = report.Summary,
            OnboardingStatus = _projectInit.GetOnboardingStatus(workspaceId)
        };
    }

    private static void UpdateProjectContext(ProjectDeepScanPlan plan, ProjectDeepScanReport report)
    {
        var context = plan.Context ?? new ProjectContextState();
        context.WorkspaceId = string.IsNullOrWhiteSpace(context.WorkspaceId) ? plan.WorkspaceId : context.WorkspaceId;
        context.DeepScanStatus = report.Status;
        context.DeepScanReportPath = plan.ReportPath;
        context.DeepScanGeneratedUtc = report.GeneratedUtc;
        context.DeepScanSummary = report.Summary;
        context.UpdatedUtc = DateTime.UtcNow;
        var pending = (context.PendingUnknowns ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "Project Brain deep scan pending", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (string.Equals(report.Status, ProjectDeepScanStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        {
            pending.Add("Project Brain deep scan failed");
        }

        context.PendingUnknowns = pending
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ProjectInitService.WriteJson(plan.ContextPath, context);
    }

    private static ProjectDeepScanStats RecalculateStats(ProjectDeepScanReport report)
    {
        report ??= new ProjectDeepScanReport();
        var docs = report.Documents ?? new List<ProjectDeepScanDocumentReport>();
        var findings = report.Findings ?? new List<ProjectDeepScanFinding>();
        var sheets = docs.SelectMany(x => x.Sheets ?? new List<ProjectDeepScanSheetReport>()).ToList();
        var schedules = sheets.SelectMany(x => x.ScheduleSamples ?? new List<ProjectDeepScanScheduleReport>()).ToList();
        return new ProjectDeepScanStats
        {
            DocumentsRequested = Math.Max(docs.Count, report.Stats?.DocumentsRequested ?? 0),
            DocumentsScanned = docs.Count(x => string.Equals(x.Status, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Status, ProjectDeepScanStatuses.Partial, StringComparison.OrdinalIgnoreCase)),
            DocumentsFailed = docs.Count(x => string.Equals(x.Status, ProjectDeepScanStatuses.Failed, StringComparison.OrdinalIgnoreCase)),
            SheetsScanned = sheets.Count,
            ScheduleSamples = schedules.Count,
            FindingCount = findings.Count,
            WarningCount = docs.Sum(x => x.ModelHealth?.TotalWarnings ?? 0),
            TotalLinks = docs.Sum(x => x.LinksStatus?.TotalLinks ?? 0),
            LoadedLinks = docs.Sum(x => x.LinksStatus?.LoadedLinks ?? 0)
        };
    }

    private static string ResolveStatus(ProjectDeepScanReport report)
    {
        var stats = report.Stats ?? new ProjectDeepScanStats();
        if (stats.DocumentsScanned == 0 && stats.DocumentsFailed > 0)
        {
            return ProjectDeepScanStatuses.Failed;
        }

        if (stats.DocumentsFailed > 0)
        {
            return ProjectDeepScanStatuses.Partial;
        }

        if (stats.DocumentsScanned > 0)
        {
            return ProjectDeepScanStatuses.Completed;
        }

        return ProjectDeepScanStatuses.NotStarted;
    }

    private static string BuildSummary(ProjectDeepScanReport report)
    {
        var stats = report.Stats ?? new ProjectDeepScanStats();
        return $"Deep scan {report.Status}: docs={stats.DocumentsScanned}/{Math.Max(stats.DocumentsRequested, report.Documents.Count)}, sheets={stats.SheetsScanned}, schedules={stats.ScheduleSamples}, findings={stats.FindingCount}, warnings={stats.WarningCount}.";
    }

    private static string BuildMarkdown(ProjectDeepScanPlan plan, ProjectDeepScanReport report)
    {
        var stats = report.Stats ?? new ProjectDeepScanStats();
        var builder = new StringBuilder();
        builder.AppendLine("# Project Brain deep scan");
        builder.AppendLine();
        builder.AppendLine($"- WorkspaceId: `{plan.WorkspaceId}`");
        builder.AppendLine($"- GeneratedUtc: {report.GeneratedUtc:O}");
        builder.AppendLine($"- Status: {report.Status}");
        builder.AppendLine($"- PrimaryModel: {(string.IsNullOrWhiteSpace(report.PrimaryRevitFilePath) ? "pending" : "`" + report.PrimaryRevitFilePath + "`")}");
        builder.AppendLine($"- DocumentsScanned: {stats.DocumentsScanned}/{Math.Max(stats.DocumentsRequested, report.Documents.Count)}");
        builder.AppendLine($"- SheetsScanned: {stats.SheetsScanned}");
        builder.AppendLine($"- ScheduleSamples: {stats.ScheduleSamples}");
        builder.AppendLine($"- Findings: {stats.FindingCount}");
        builder.AppendLine($"- Warnings: {stats.WarningCount}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine(report.Summary);
        builder.AppendLine();

        if ((report.Strengths ?? new List<string>()).Count > 0)
        {
            builder.AppendLine("## Strengths");
            foreach (var item in report.Strengths.Take(6))
            {
                builder.AppendLine($"- {item}");
            }
            builder.AppendLine();
        }

        if ((report.Weaknesses ?? new List<string>()).Count > 0)
        {
            builder.AppendLine("## Weaknesses");
            foreach (var item in report.Weaknesses.Take(8))
            {
                builder.AppendLine($"- {item}");
            }
            builder.AppendLine();
        }

        if ((report.Findings ?? new List<ProjectDeepScanFinding>()).Count > 0)
        {
            builder.AppendLine("## Top findings");
            foreach (var finding in report.Findings.Take(12))
            {
                builder.AppendLine($"- [{finding.Severity}] {finding.Title}: {finding.Message}");
            }
        }

        return builder.ToString();
    }
}

public sealed class ProjectDeepScanPlan
{
    public ProjectDeepScanPlan(
        string workspaceId,
        string workspaceRoot,
        string contextPath,
        string reportPath,
        string summaryReportPath,
        ProjectContextState context,
        ProjectSourceManifest manifest,
        ProjectManifestStats manifestStats,
        string primaryRevitFilePath,
        IReadOnlyList<string> targetFiles)
    {
        WorkspaceId = workspaceId;
        WorkspaceRoot = workspaceRoot;
        ContextPath = contextPath;
        ReportPath = reportPath;
        SummaryReportPath = summaryReportPath;
        Context = context;
        Manifest = manifest;
        ManifestStats = manifestStats;
        PrimaryRevitFilePath = primaryRevitFilePath;
        TargetFiles = targetFiles;
    }

    public string WorkspaceId { get; }

    public string WorkspaceRoot { get; }

    public string ContextPath { get; }

    public string ReportPath { get; }

    public string SummaryReportPath { get; }

    public ProjectContextState Context { get; }

    public ProjectSourceManifest Manifest { get; }

    public ProjectManifestStats ManifestStats { get; }

    public string PrimaryRevitFilePath { get; }

    public IReadOnlyList<string> TargetFiles { get; }
}
