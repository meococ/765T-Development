using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class TaskMetricsRequest
{
    [DataMember(Order = 1)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 100;
}

[DataContract]
public sealed class TaskResidualsRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;
}

[DataContract]
public sealed class TaskPromoteMemoryRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PromotionKind { get; set; } = "lesson";

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string Notes { get; set; } = string.Empty;
}

[DataContract]
public sealed class HotStateRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int MaxRecentOperations { get; set; } = 10;

    [DataMember(Order = 3)]
    public int MaxRecentEvents { get; set; } = 10;

    [DataMember(Order = 4)]
    public int MaxPendingTasks { get; set; } = 10;

    [DataMember(Order = 5)]
    public bool IncludeGraph { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IncludeToolCatalog { get; set; }
}

[DataContract]
public sealed class ContextResolveBundleRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public int MaxAnchors { get; set; } = 8;

    [DataMember(Order = 5)]
    public bool IncludeHot { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IncludeWarm { get; set; } = true;

    [DataMember(Order = 7)]
    public bool IncludeCold { get; set; }
}

[DataContract]
public sealed class ContextSearchAnchorsRequest
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public int MaxResults { get; set; } = 20;
}

[DataContract]
public sealed class ArtifactSummarizeRequest
{
    [DataMember(Order = 1)]
    public string ArtifactPath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int MaxChars { get; set; } = 2000;

    [DataMember(Order = 3)]
    public int MaxLines { get; set; } = 40;
}

[DataContract]
public sealed class MemoryFindSimilarRunsRequest
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
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int MaxResults { get; set; } = 10;
}

[DataContract]
public sealed class ToolCapabilityLookupRequest
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> RiskTags { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public List<string> RequiredContext { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 10;

    [DataMember(Order = 5)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> IssueKinds { get; set; } = new List<string>();
}

[DataContract]
public sealed class ToolGuidanceRequest
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> ToolNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public int MaxResults { get; set; } = 10;

    [DataMember(Order = 4)]
    public string DocumentContext { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string PreferredCapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> IssueKinds { get; set; } = new List<string>();
}

[DataContract]
public sealed class ContextDeltaSummaryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int MaxRecentOperations { get; set; } = 10;

    [DataMember(Order = 3)]
    public int MaxRecentEvents { get; set; } = 10;

    [DataMember(Order = 4)]
    public int MaxRecommendations { get; set; } = 5;
}

[DataContract]
public sealed class TaskMemoryPromotionRecord
{
    [DataMember(Order = 1)]
    public string PromotionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PromotionKind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public string Notes { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public string ApprovedByCaller { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 13)]
    public MemoryRecord MemoryRecord { get; set; } = new MemoryRecord();
}

[DataContract]
public sealed class TaskMemoryPromotionResponse
{
    [DataMember(Order = 1)]
    public TaskMemoryPromotionRecord Promotion { get; set; } = new TaskMemoryPromotionRecord();

    [DataMember(Order = 2)]
    public string CandidatePath { get; set; } = string.Empty;
}
