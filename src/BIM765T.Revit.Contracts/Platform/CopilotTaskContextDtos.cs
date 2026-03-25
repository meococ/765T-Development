using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class HotStateResponse
{
    [DataMember(Order = 1)]
    public TaskContextResponse TaskContext { get; set; } = new TaskContextResponse();

    [DataMember(Order = 2)]
    public DocumentStateGraphSnapshot Graph { get; set; } = new DocumentStateGraphSnapshot();

    [DataMember(Order = 3)]
    public QueueStateResponse Queue { get; set; } = new QueueStateResponse();

    [DataMember(Order = 4)]
    public List<TaskSummaryResponse> PendingTasks { get; set; } = new List<TaskSummaryResponse>();

    [DataMember(Order = 5)]
    public List<ToolManifest> Tools { get; set; } = new List<ToolManifest>();
}

[DataContract]
public sealed class ContextBundleItem
{
    [DataMember(Order = 1)]
    public string AnchorId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Tier { get; set; } = "warm";

    [DataMember(Order = 3)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SourceKind { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SourcePath { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public string RetrievalHint { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public int Score { get; set; }
}

[DataContract]
public sealed class ContextResolveBundleResponse
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<ContextBundleItem> Items { get; set; } = new List<ContextBundleItem>();
}

[DataContract]
public sealed class ContextSearchAnchorsResponse
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<ContextBundleItem> Items { get; set; } = new List<ContextBundleItem>();
}

[DataContract]
public sealed class ArtifactSummaryResponse
{
    [DataMember(Order = 1)]
    public string ArtifactPath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool Exists { get; set; }

    [DataMember(Order = 3)]
    public long SizeBytes { get; set; }

    [DataMember(Order = 4)]
    public int LineCountEstimate { get; set; }

    [DataMember(Order = 5)]
    public string DetectedFormat { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<string> TopLevelKeys { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string PreviewText { get; set; } = string.Empty;
}

[DataContract]
public sealed class SimilarTaskRunItem
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int Score { get; set; }
}

[DataContract]
public sealed class MemoryFindSimilarRunsResponse
{
    [DataMember(Order = 1)]
    public List<SimilarTaskRunItem> Runs { get; set; } = new List<SimilarTaskRunItem>();
}

[DataContract]
public sealed class ToolCapabilityMatch
{
    [DataMember(Order = 1)]
    public ToolManifest Manifest { get; set; } = new ToolManifest();

    [DataMember(Order = 2)]
    public int Score { get; set; }

    [DataMember(Order = 3)]
    public string Reason { get; set; } = string.Empty;
}

[DataContract]
public sealed class ToolCapabilityLookupResponse
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<ToolCapabilityMatch> Matches { get; set; } = new List<ToolCapabilityMatch>();
}

[DataContract]
public sealed class ToolGuidanceRecord
{
    [DataMember(Order = 1)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string GuidanceSummary { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int RiskScore { get; set; }

    [DataMember(Order = 4)]
    public int CostScore { get; set; }

    [DataMember(Order = 5)]
    public List<string> Prerequisites { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> FollowUps { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> CommonFailureCodes { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> RecommendedRecoveryTools { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public List<string> AntiPatterns { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<string> TypicalChains { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public List<string> RecoveryHints { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> RecommendedTemplates { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 14)]
    public string DeterminismLevel { get; set; } = ToolDeterminismLevels.Deterministic;

    [DataMember(Order = 15)]
    public bool RequiresPolicyPack { get; set; }

    [DataMember(Order = 16)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 17)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 18)]
    public List<string> IssueKinds { get; set; } = new List<string>();
}

[DataContract]
public sealed class ToolGuidanceResponse
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<ToolGuidanceRecord> Guidance { get; set; } = new List<ToolGuidanceRecord>();

    [DataMember(Order = 3)]
    public PlaybookRecommendation RecommendedPlaybook { get; set; } = new PlaybookRecommendation();

    [DataMember(Order = 4)]
    public List<string> RecommendedPackIds { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string ResolvedCapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 6)]
    public PolicyResolution PolicyResolution { get; set; } = new PolicyResolution();

    [DataMember(Order = 7)]
    public List<CapabilitySpecialistDescriptor> RecommendedSpecialists { get; set; } = new List<CapabilitySpecialistDescriptor>();

    [DataMember(Order = 8)]
    public CompiledTaskPlan CompiledPlan { get; set; } = new CompiledTaskPlan();
}

[DataContract]
public sealed class ContextDeltaSummaryResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int RecentOperationCount { get; set; }

    [DataMember(Order = 3)]
    public int RecentEventCount { get; set; }

    [DataMember(Order = 4)]
    public int RecentChangedElementCount { get; set; }

    [DataMember(Order = 5)]
    public string LastMutationTool { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string LastFailureCode { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> SuggestedNextTools { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public int AddedElementEstimate { get; set; }

    [DataMember(Order = 10)]
    public int RemovedElementEstimate { get; set; }

    [DataMember(Order = 11)]
    public int ModifiedElementEstimate { get; set; }

    [DataMember(Order = 12)]
    public List<CountByNameDto> TopCategories { get; set; } = new List<CountByNameDto>();

    [DataMember(Order = 13)]
    public List<CountByNameDto> DisciplineHints { get; set; } = new List<CountByNameDto>();

    [DataMember(Order = 14)]
    public List<string> RecentMutationKinds { get; set; } = new List<string>();
}
