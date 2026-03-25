using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── QC & Model Audit DTOs ──

[DataContract]
public sealed class NamingAuditRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public string ExpectedPattern { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Scope { get; set; } = "views";

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class NamingViolationItem
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string ElementName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Violation { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SuggestedName { get; set; } = string.Empty;
}

[DataContract]
public sealed class NamingAuditResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalChecked { get; set; }

    [DataMember(Order = 3)]
    public int ViolationCount { get; set; }

    [DataMember(Order = 4)]
    public List<NamingViolationItem> Violations { get; set; } = new List<NamingViolationItem>();
}

[DataContract]
public sealed class UnusedViewsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IncludeSchedules { get; set; }

    [DataMember(Order = 3)]
    public bool IncludeLegends { get; set; }

    [DataMember(Order = 4)]
    public bool ExcludeTemplates { get; set; } = true;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class UnusedViewItem
{
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Reason { get; set; } = string.Empty;
}

[DataContract]
public sealed class UnusedViewsResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalViews { get; set; }

    [DataMember(Order = 3)]
    public int UnusedCount { get; set; }

    [DataMember(Order = 4)]
    public List<UnusedViewItem> UnusedViews { get; set; } = new List<UnusedViewItem>();
}

[DataContract]
public sealed class UnusedFamiliesRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public bool IncludeSystemFamilies { get; set; }

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class UnusedFamilyItem
{
    [DataMember(Order = 1)]
    public int FamilyId { get; set; }

    [DataMember(Order = 2)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int TypeCount { get; set; }

    [DataMember(Order = 5)]
    public long EstimatedSizeKb { get; set; }
}

[DataContract]
public sealed class UnusedFamiliesResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalFamilies { get; set; }

    [DataMember(Order = 3)]
    public int UnusedCount { get; set; }

    [DataMember(Order = 4)]
    public long TotalEstimatedSizeKb { get; set; }

    [DataMember(Order = 5)]
    public List<UnusedFamilyItem> UnusedFamilies { get; set; } = new List<UnusedFamilyItem>();
}

[DataContract]
public sealed class DuplicateElementsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public double ToleranceMm { get; set; } = 1.0;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 200;
}

[DataContract]
public sealed class DuplicateGroup
{
    [DataMember(Order = 1)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 2)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Location { get; set; } = string.Empty;
}

[DataContract]
public sealed class DuplicateElementsResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalChecked { get; set; }

    [DataMember(Order = 3)]
    public int DuplicateGroupCount { get; set; }

    [DataMember(Order = 4)]
    public List<DuplicateGroup> DuplicateGroups { get; set; } = new List<DuplicateGroup>();
}

[DataContract]
public sealed class WarningsCleanupRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SeverityFilter { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string CategoryFilter { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 200;
}

[DataContract]
public sealed class WarningCleanupItem
{
    [DataMember(Order = 1)]
    public string WarningType { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<int> AffectedElementIds { get; set; } = new List<int>();

    [DataMember(Order = 4)]
    public string SuggestedAction { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool AutoFixAvailable { get; set; }
}

[DataContract]
public sealed class WarningsCleanupResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalWarnings { get; set; }

    [DataMember(Order = 3)]
    public int AutoFixableCount { get; set; }

    [DataMember(Order = 4)]
    public List<WarningCleanupItem> Warnings { get; set; } = new List<WarningCleanupItem>();

    [DataMember(Order = 5)]
    public Dictionary<string, int> ByCategory { get; set; } = new Dictionary<string, int>();
}

[DataContract]
public sealed class ModelStandardsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> RuleNames { get; set; } = new List<string>();
}

[DataContract]
public sealed class StandardsCheckItem
{
    [DataMember(Order = 1)]
    public string RuleName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int AffectedCount { get; set; }

    [DataMember(Order = 5)]
    public List<int> SampleElementIds { get; set; } = new List<int>();
}

[DataContract]
public sealed class ModelStandardsResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalRules { get; set; }

    [DataMember(Order = 3)]
    public int PassedCount { get; set; }

    [DataMember(Order = 4)]
    public int FailedCount { get; set; }

    [DataMember(Order = 5)]
    public string OverallGrade { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<StandardsCheckItem> Results { get; set; } = new List<StandardsCheckItem>();
}

[DataContract]
public sealed class PurgeUnusedRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool PurgeViews { get; set; } = true;

    [DataMember(Order = 3)]
    public bool PurgeFamilies { get; set; } = true;

    [DataMember(Order = 4)]
    public bool PurgeMaterials { get; set; }

    [DataMember(Order = 5)]
    public bool PurgeLinePatterns { get; set; }

    [DataMember(Order = 6)]
    public List<string> ExcludeNames { get; set; } = new List<string>();
}

[DataContract]
public sealed class ComplianceReportRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IncludeNaming { get; set; } = true;

    [DataMember(Order = 3)]
    public bool IncludeUnused { get; set; } = true;

    [DataMember(Order = 4)]
    public bool IncludeWarnings { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeStandards { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IncludeDuplicates { get; set; } = true;
}

[DataContract]
public sealed class ComplianceSectionResult
{
    [DataMember(Order = 1)]
    public string Section { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int IssueCount { get; set; }

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Points earned. Used by view_template_compliance for weighted scoring.</summary>
    [DataMember(Order = 5)]
    public int Score { get; set; }

    /// <summary>Maximum possible points for this section.</summary>
    [DataMember(Order = 6)]
    public int MaxPoints { get; set; }
}

[DataContract]
public sealed class ComplianceReportResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string OverallScore { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int TotalIssues { get; set; }

    [DataMember(Order = 4)]
    public List<ComplianceSectionResult> Sections { get; set; } = new List<ComplianceSectionResult>();

    [DataMember(Order = 5)]
    public string Timestamp { get; set; } = string.Empty;
}
