using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

public static class ProjectSourceKinds
{
    public const string RevitProject = "revit_project";
    public const string RevitFamily = "revit_family";
    public const string Pdf = "pdf";
    public const string Other = "other";
}

public static class ProjectPrimaryModelStatuses
{
    public const string NotRequested = "not_requested";
    public const string PendingLiveSummary = "pending_live_summary";
    public const string Captured = "captured";
    public const string Failed = "failed";
}

public static class ProjectOnboardingStatuses
{
    public const string NotInitialized = "not_initialized";
    public const string PreviewReady = "preview_ready";
    public const string Initialized = "initialized";
    public const string RequiresPrimaryModel = "requires_primary_model";
    public const string Blocked = "blocked";
}

[DataContract]
public sealed class ProjectSourceFile
{
    [DataMember(Order = 1)]
    public string SourcePath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RelativePath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Extension { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SourceKind { get; set; } = ProjectSourceKinds.Other;

    [DataMember(Order = 6)]
    public long SizeBytes { get; set; }

    [DataMember(Order = 7)]
    public DateTime LastWriteUtc { get; set; }

    [DataMember(Order = 8)]
    public string Fingerprint { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public bool IsPrimaryCandidate { get; set; }

    [DataMember(Order = 10)]
    public bool IsSelectedPrimary { get; set; }

    [DataMember(Order = 11)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string RevitVersion { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public bool? IsWorkshared { get; set; }

    [DataMember(Order = 14)]
    public string WorksharingSummary { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectManifestStats
{
    [DataMember(Order = 1)]
    public int TotalFiles { get; set; }

    [DataMember(Order = 2)]
    public int RevitProjectCount { get; set; }

    [DataMember(Order = 3)]
    public int RevitFamilyCount { get; set; }

    [DataMember(Order = 4)]
    public int PdfCount { get; set; }

    [DataMember(Order = 5)]
    public string PrimaryRevitFilePath { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectSourceManifest
{
    [DataMember(Order = 1)]
    public string SourceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 3)]
    public string PrimaryRevitFilePath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<ProjectSourceFile> Files { get; set; } = new List<ProjectSourceFile>();

    [DataMember(Order = 6)]
    public ProjectManifestStats Stats { get; set; } = new ProjectManifestStats();
}

[DataContract]
public sealed class ProjectLayerSummary
{
    [DataMember(Order = 1)]
    public string LayerKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class OnboardingStatusDto
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string InitStatus { get; set; } = ProjectOnboardingStatuses.NotInitialized;

    [DataMember(Order = 4)]
    public string DeepScanStatus { get; set; } = ProjectDeepScanStatuses.NotStarted;

    [DataMember(Order = 5)]
    public bool ResumeEligible { get; set; }

    [DataMember(Order = 6)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string MissionId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public PendingApprovalRef PendingApproval { get; set; } = new PendingApprovalRef();

    [DataMember(Order = 9)]
    public string PrimaryModelStatus { get; set; } = ProjectPrimaryModelStatuses.NotRequested;

    [DataMember(Order = 10)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectContextRef
{
    [DataMember(Order = 1)]
    public string RefId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RefKind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SourcePath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string RelativePath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string SourcePackId { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectPrimaryModelReport
{
    [DataMember(Order = 1)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Status { get; set; } = ProjectPrimaryModelStatuses.NotRequested;

    [DataMember(Order = 3)]
    public bool PendingLiveSummary { get; set; }

    [DataMember(Order = 4)]
    public DateTime? CapturedUtc { get; set; }

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public DocumentSummaryDto DocumentSummary { get; set; } = new DocumentSummaryDto();
}

[DataContract]
public sealed class ProjectContextState
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SourceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PrimaryRevitFilePath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SourceManifestPath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<string> FirmPackIds { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> EnabledPackIds { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> PreferredStandardsPackIds { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public List<string> PreferredPlaybookPackIds { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public List<string> PendingUnknowns { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public bool FirmDoctrinePending { get; set; }

    [DataMember(Order = 13)]
    public string PrimaryModelStatus { get; set; } = ProjectPrimaryModelStatuses.NotRequested;

    [DataMember(Order = 14)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 15)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 16)]
    public string DeepScanStatus { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public string DeepScanReportPath { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public DateTime? DeepScanGeneratedUtc { get; set; }

    [DataMember(Order = 19)]
    public string DeepScanSummary { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectInitPreviewRequest
{
    [DataMember(Order = 1)]
    public string SourceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> FirmPackIds { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string PrimaryRevitFilePath { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectInitPreviewResponse
{
    [DataMember(Order = 1)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IsValid { get; set; }

    [DataMember(Order = 3)]
    public string SuggestedWorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool WorkspaceExists { get; set; }

    [DataMember(Order = 6)]
    public bool RequiresPrimaryRevitSelection { get; set; }

    [DataMember(Order = 7)]
    public ProjectSourceManifest Manifest { get; set; } = new ProjectSourceManifest();

    [DataMember(Order = 8)]
    public ProjectManifestStats ManifestStats { get; set; } = new ProjectManifestStats();

    [DataMember(Order = 9)]
    public List<ProjectLayerSummary> LayerSummaries { get; set; } = new List<ProjectLayerSummary>();

    [DataMember(Order = 10)]
    public List<string> EffectivePackIds { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public List<string> FirmPackIds { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> Errors { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public List<string> Warnings { get; set; } = new List<string>();

    [DataMember(Order = 14)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public OnboardingStatusDto OnboardingStatus { get; set; } = new OnboardingStatusDto();
}

[DataContract]
public sealed class ProjectInitApplyRequest
{
    [DataMember(Order = 1)]
    public string SourceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> FirmPackIds { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string PrimaryRevitFilePath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public bool AllowExistingWorkspaceOverwrite { get; set; }

    [DataMember(Order = 7)]
    public bool IncludeLivePrimaryModelSummary { get; set; } = true;
}

[DataContract]
public sealed class ProjectInitApplyResponse
{
    [DataMember(Order = 1)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string WorkspaceRootPath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public ProjectSourceManifest Manifest { get; set; } = new ProjectSourceManifest();

    [DataMember(Order = 5)]
    public ProjectManifestStats ManifestStats { get; set; } = new ProjectManifestStats();

    [DataMember(Order = 6)]
    public string ProjectContextPath { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string WorkspaceManifestPath { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string ManifestReportPath { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string SummaryReportPath { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string ProjectBriefPath { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string PrimaryModelReportPath { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string PrimaryModelStatus { get; set; } = ProjectPrimaryModelStatuses.NotRequested;

    [DataMember(Order = 13)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public ProjectContextBundleResponse ContextBundle { get; set; } = new ProjectContextBundleResponse();

    [DataMember(Order = 15)]
    public OnboardingStatusDto OnboardingStatus { get; set; } = new OnboardingStatusDto();
}

[DataContract]
public sealed class ProjectManifestRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectManifestResponse
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
    public string ManifestPath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SummaryReportPath { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public ProjectSourceManifest Manifest { get; set; } = new ProjectSourceManifest();

    [DataMember(Order = 8)]
    public ProjectManifestStats ManifestStats { get; set; } = new ProjectManifestStats();

    [DataMember(Order = 9)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectContextBundleRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int MaxSourceRefs { get; set; } = 8;

    [DataMember(Order = 4)]
    public int MaxStandardsRefs { get; set; } = 6;
}

[DataContract]
public sealed class ProjectContextBundleResponse
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
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ProjectBrief { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public ProjectManifestStats ManifestStats { get; set; } = new ProjectManifestStats();

    [DataMember(Order = 8)]
    public string PrimaryModelStatus { get; set; } = ProjectPrimaryModelStatuses.NotRequested;

    [DataMember(Order = 9)]
    public List<ProjectLayerSummary> LayerSummaries { get; set; } = new List<ProjectLayerSummary>();

    [DataMember(Order = 10)]
    public List<ProjectContextRef> TopStandardsRefs { get; set; } = new List<ProjectContextRef>();

    [DataMember(Order = 11)]
    public List<ProjectContextRef> SourceRefs { get; set; } = new List<ProjectContextRef>();

    [DataMember(Order = 12)]
    public List<string> PendingUnknowns { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public string RecommendedPlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string StandardsSummary { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string DeepScanStatus { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string DeepScanSummary { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public int DeepScanFindingCount { get; set; }

    [DataMember(Order = 18)]
    public string DeepScanReportPath { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public List<ProjectContextRef> DeepScanRefs { get; set; } = new List<ProjectContextRef>();

    [DataMember(Order = 20)]
    public OnboardingStatusDto OnboardingStatus { get; set; } = new OnboardingStatusDto();
}
