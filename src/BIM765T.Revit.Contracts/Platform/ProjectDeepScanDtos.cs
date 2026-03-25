using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

public static class ProjectDeepScanStatuses
{
    public const string NotStarted = "not_started";
    public const string Completed = "completed";
    public const string Partial = "partial";
    public const string Failed = "failed";
}

[DataContract]
public sealed class ProjectDeepScanRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IncludeSecondaryRevitProjects { get; set; }

    [DataMember(Order = 3)]
    public int MaxDocuments { get; set; } = 3;

    [DataMember(Order = 4)]
    public int MaxSheets { get; set; } = 8;

    [DataMember(Order = 5)]
    public int MaxSheetIntelligence { get; set; } = 4;

    [DataMember(Order = 6)]
    public bool IncludeScheduleData { get; set; } = true;

    [DataMember(Order = 7)]
    public int MaxSchedulesPerSheet { get; set; } = 3;

    [DataMember(Order = 8)]
    public int MaxScheduleRows { get; set; } = 25;

    [DataMember(Order = 9)]
    public int MaxFindings { get; set; } = 50;

    [DataMember(Order = 10)]
    public string SmartQcRulesetName { get; set; } = "base-rules";

    [DataMember(Order = 11)]
    public bool ForceRescan { get; set; }
}

[DataContract]
public sealed class ProjectDeepScanGetRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectDeepScanStats
{
    [DataMember(Order = 1)]
    public int DocumentsRequested { get; set; }

    [DataMember(Order = 2)]
    public int DocumentsScanned { get; set; }

    [DataMember(Order = 3)]
    public int DocumentsFailed { get; set; }

    [DataMember(Order = 4)]
    public int SheetsScanned { get; set; }

    [DataMember(Order = 5)]
    public int ScheduleSamples { get; set; }

    [DataMember(Order = 6)]
    public int FindingCount { get; set; }

    [DataMember(Order = 7)]
    public int WarningCount { get; set; }

    [DataMember(Order = 8)]
    public int TotalLinks { get; set; }

    [DataMember(Order = 9)]
    public int LoadedLinks { get; set; }
}

[DataContract]
public sealed class ProjectDeepScanFinding
{
    [DataMember(Order = 1)]
    public string FindingId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;

    [DataMember(Order = 6)]
    public string SourceTool { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public int? ElementId { get; set; }

    [DataMember(Order = 10)]
    public string EvidenceRef { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string SuggestedAction { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectDeepScanScheduleReport
{
    [DataMember(Order = 1)]
    public string SourceSheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ScheduleId { get; set; }

    [DataMember(Order = 3)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public ScheduleExtractionResponse Extraction { get; set; } = new ScheduleExtractionResponse();
}

[DataContract]
public sealed class ProjectDeepScanSheetReport
{
    [DataMember(Order = 1)]
    public int SheetId { get; set; }

    [DataMember(Order = 2)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SheetName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public SheetSummaryResponse Summary { get; set; } = new SheetSummaryResponse();

    [DataMember(Order = 5)]
    public SheetCaptureIntelligenceResponse Intelligence { get; set; } = new SheetCaptureIntelligenceResponse();

    [DataMember(Order = 6)]
    public List<ProjectDeepScanScheduleReport> ScheduleSamples { get; set; } = new List<ProjectDeepScanScheduleReport>();
}

[DataContract]
public sealed class ProjectDeepScanDocumentReport
{
    [DataMember(Order = 1)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Status { get; set; } = ProjectDeepScanStatuses.NotStarted;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public DocumentSummaryDto DocumentSummary { get; set; } = new DocumentSummaryDto();

    [DataMember(Order = 7)]
    public ModelHealthResponse ModelHealth { get; set; } = new ModelHealthResponse();

    [DataMember(Order = 8)]
    public LinksStatusResponse LinksStatus { get; set; } = new LinksStatusResponse();

    [DataMember(Order = 9)]
    public WorksetHealthResponse WorksetHealth { get; set; } = new WorksetHealthResponse();

    [DataMember(Order = 10)]
    public SmartQcResponse SmartQc { get; set; } = new SmartQcResponse();

    [DataMember(Order = 11)]
    public int SheetCountDiscovered { get; set; }

    [DataMember(Order = 12)]
    public List<ProjectDeepScanSheetReport> Sheets { get; set; } = new List<ProjectDeepScanSheetReport>();
}

[DataContract]
public sealed class ProjectDeepScanReport
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 4)]
    public string Status { get; set; } = ProjectDeepScanStatuses.NotStarted;

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string PrimaryRevitFilePath { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public ProjectManifestStats ManifestStats { get; set; } = new ProjectManifestStats();

    [DataMember(Order = 8)]
    public ProjectDeepScanStats Stats { get; set; } = new ProjectDeepScanStats();

    [DataMember(Order = 9)]
    public List<ProjectDeepScanDocumentReport> Documents { get; set; } = new List<ProjectDeepScanDocumentReport>();

    [DataMember(Order = 10)]
    public List<ProjectDeepScanFinding> Findings { get; set; } = new List<ProjectDeepScanFinding>();

    [DataMember(Order = 11)]
    public List<string> Strengths { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> Weaknesses { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public List<string> PendingUnknowns { get; set; } = new List<string>();

    [DataMember(Order = 14)]
    public List<ProjectContextRef> EvidenceRefs { get; set; } = new List<ProjectContextRef>();
}

[DataContract]
public sealed class ProjectDeepScanResponse
{
    [DataMember(Order = 1)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string WorkspaceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ReportPath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SummaryReportPath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public ProjectDeepScanReport Report { get; set; } = new ProjectDeepScanReport();

    [DataMember(Order = 7)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public ProjectContextBundleResponse ContextBundle { get; set; } = new ProjectContextBundleResponse();

    [DataMember(Order = 9)]
    public OnboardingStatusDto OnboardingStatus { get; set; } = new OnboardingStatusDto();
}

[DataContract]
public sealed class ProjectDeepScanReportResponse
{
    [DataMember(Order = 1)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool Exists { get; set; }

    [DataMember(Order = 3)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string WorkspaceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ReportPath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SummaryReportPath { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public ProjectDeepScanReport Report { get; set; } = new ProjectDeepScanReport();

    [DataMember(Order = 8)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public OnboardingStatusDto OnboardingStatus { get; set; } = new OnboardingStatusDto();
}
