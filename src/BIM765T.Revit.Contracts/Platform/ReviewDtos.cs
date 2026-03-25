using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class ReviewIssue
{
    [DataMember(Order = 1)]
    public string Code { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;

    [DataMember(Order = 3)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? ElementId { get; set; }
}

[DataContract]
public sealed class ReviewReport
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int IssueCount { get; set; }

    [DataMember(Order = 5)]
    public List<ReviewIssue> Issues { get; set; } = new List<ReviewIssue>();
}

[DataContract]
public sealed class ReviewParameterCompletenessRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public List<string> RequiredParameterNames { get; set; } = new List<string>();
}

[DataContract]
public sealed class ReviewRuleSetRunRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RuleSetName { get; set; } = "document_health_v1";

    [DataMember(Order = 3)]
    public int? ViewId { get; set; }

    [DataMember(Order = 4)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public List<string> RequiredParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public bool UseCurrentSelectionWhenEmpty { get; set; } = true;

    [DataMember(Order = 7)]
    public int MaxIssues { get; set; } = 100;

    [DataMember(Order = 8)]
    public int? SheetId { get; set; }

    [DataMember(Order = 9)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string SheetName { get; set; } = string.Empty;
}

[DataContract]
public sealed class ReviewRuleSetResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RuleSetName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int AppliedRuleCount { get; set; }

    [DataMember(Order = 5)]
    public int TriggeredRuleCount { get; set; }

    [DataMember(Order = 6)]
    public List<string> AppliedRules { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class ActiveViewSummaryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? ViewId { get; set; }

    [DataMember(Order = 3)]
    public int MaxCategoryCount { get; set; } = 20;

    [DataMember(Order = 4)]
    public int MaxClassCount { get; set; } = 20;
}

[DataContract]
public sealed class CountByNameDto
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }
}

[DataContract]
public sealed class ActiveViewSummaryResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int ViewId { get; set; }

    [DataMember(Order = 4)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string LevelName { get; set; } = string.Empty;

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public int? LevelId { get; set; }

    [DataMember(Order = 8)]
    public int TotalVisibleElements { get; set; }

    [DataMember(Order = 9)]
    public int SelectedCount { get; set; }

    [DataMember(Order = 10)]
    public int WarningCount { get; set; }

    [DataMember(Order = 11)]
    public List<CountByNameDto> CategoryCounts { get; set; } = new List<CountByNameDto>();

    [DataMember(Order = 12)]
    public List<CountByNameDto> ClassCounts { get; set; } = new List<CountByNameDto>();
}

[DataContract]
public sealed class LinkStatusDto
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsLoaded { get; set; }

    [DataMember(Order = 5)]
    public string LinkedDocumentTitle { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string LinkedDocumentPath { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string AttachmentType { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string InstanceName { get; set; } = string.Empty;
}

[DataContract]
public sealed class LinksStatusResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalLinks { get; set; }

    [DataMember(Order = 3)]
    public int LoadedLinks { get; set; }

    [DataMember(Order = 4)]
    public List<LinkStatusDto> Links { get; set; } = new List<LinkStatusDto>();
}

[DataContract]
public sealed class ModelHealthResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool HasPath { get; set; }

    [DataMember(Order = 4)]
    public bool IsModified { get; set; }

    [DataMember(Order = 5)]
    public bool IsWorkshared { get; set; }

    [DataMember(Order = 6)]
    public int TotalWarnings { get; set; }

    [DataMember(Order = 7)]
    public int TotalLinks { get; set; }

    [DataMember(Order = 8)]
    public int LoadedLinks { get; set; }

    [DataMember(Order = 9)]
    public int RecentChangeEvents { get; set; }

    [DataMember(Order = 10)]
    public int RecentSaveEvents { get; set; }

    [DataMember(Order = 11)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class WorksetSummaryDto
{
    [DataMember(Order = 1)]
    public int WorksetId { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsOpen { get; set; }

    [DataMember(Order = 5)]
    public bool IsEditable { get; set; }

    [DataMember(Order = 6)]
    public int SelectionCount { get; set; }
}

[DataContract]
public sealed class WorksetHealthResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IsWorkshared { get; set; }

    [DataMember(Order = 3)]
    public int? ActiveWorksetId { get; set; }

    [DataMember(Order = 4)]
    public string ActiveWorksetName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int TotalWorksets { get; set; }

    [DataMember(Order = 6)]
    public int OpenWorksets { get; set; }

    [DataMember(Order = 7)]
    public List<WorksetSummaryDto> Worksets { get; set; } = new List<WorksetSummaryDto>();

    [DataMember(Order = 8)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class SheetSummaryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? SheetId { get; set; }

    [DataMember(Order = 3)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SheetName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int MaxPlacedViews { get; set; } = 20;

    [DataMember(Order = 6)]
    public List<string> RequiredParameterNames { get; set; } = new List<string>();
}

[DataContract]
public sealed class PlacedViewInfoDto
{
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? ViewportId { get; set; }
}

[DataContract]
public sealed class SheetSummaryResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetId { get; set; }

    [DataMember(Order = 3)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SheetName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool IsPlaceholder { get; set; }

    [DataMember(Order = 6)]
    public int TitleBlockCount { get; set; }

    [DataMember(Order = 7)]
    public int ViewportCount { get; set; }

    [DataMember(Order = 8)]
    public int ScheduleInstanceCount { get; set; }

    [DataMember(Order = 9)]
    public List<PlacedViewInfoDto> PlacedViews { get; set; } = new List<PlacedViewInfoDto>();

    [DataMember(Order = 10)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}
