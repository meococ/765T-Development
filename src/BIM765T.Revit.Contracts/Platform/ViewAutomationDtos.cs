using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class ListViewFiltersRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Filter theo tên (substring, case-insensitive). Rỗng = lấy tất cả.</summary>
    [DataMember(Order = 2)]
    public string NameContains { get; set; } = string.Empty;

    /// <summary>Trả thêm danh sách tên categories của từng filter.</summary>
    [DataMember(Order = 3)]
    public bool IncludeCategoryNames { get; set; } = true;

    /// <summary>Trả thêm danh sách rule summary của từng filter.</summary>
    [DataMember(Order = 4)]
    public bool IncludeRuleSummary { get; set; } = true;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class ViewFilterSummary
{
    [DataMember(Order = 1)]
    public int FilterId { get; set; }

    [DataMember(Order = 2)]
    public string FilterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int CategoryCount { get; set; }

    [DataMember(Order = 4)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public int RuleCount { get; set; }

    /// <summary>Mô tả ngắn các rule, ví dụ: "Comments contains EX; Mark equals 01".</summary>
    [DataMember(Order = 6)]
    public string RuleSummary { get; set; } = string.Empty;
}

[DataContract]
public sealed class ListViewFiltersResponse
{
    [DataMember(Order = 1)]
    public List<ViewFilterSummary> Filters { get; set; } = new List<ViewFilterSummary>();

    [DataMember(Order = 2)]
    public int TotalCount { get; set; }
}

// ── Filter Inspect ──────────────────────────────────────────────────────────

[DataContract]
public sealed class InspectFilterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? FilterId { get; set; }

    [DataMember(Order = 3)]
    public string FilterName { get; set; } = string.Empty;

    /// <summary>Scan tất cả views (không phải template) đang apply filter này.</summary>
    [DataMember(Order = 4)]
    public bool IncludeViewUsage { get; set; } = true;

    /// <summary>Scan tất cả view templates đang apply filter này.</summary>
    [DataMember(Order = 5)]
    public bool IncludeTemplateUsage { get; set; } = true;
}

[DataContract]
public sealed class FilterRuleDetail
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>Raw ElementId value — hữu ích khi là built-in param (số âm).</summary>
    [DataMember(Order = 2)]
    public string ParameterIdRaw { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string StorageType { get; set; } = string.Empty;

    /// <summary>Ví dụ: "equals", "contains", "NOT(equals)", "has_value".</summary>
    [DataMember(Order = 4)]
    public string Operator { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Value { get; set; } = string.Empty;
}

[DataContract]
public sealed class FilterUsageEntry
{
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool IsTemplate { get; set; }

    [DataMember(Order = 4)]
    public bool IsVisible { get; set; }

    [DataMember(Order = 5)]
    public bool IsHalftone { get; set; }

    [DataMember(Order = 6)]
    public int Transparency { get; set; }

    /// <summary>"R,G,B" hoặc "default" nếu không set override.</summary>
    [DataMember(Order = 7)]
    public string ProjectionLineColor { get; set; } = "default";
}

[DataContract]
public sealed class FilterInspectResult
{
    [DataMember(Order = 1)]
    public int FilterId { get; set; }

    [DataMember(Order = 2)]
    public string FilterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int CategoryCount { get; set; }

    [DataMember(Order = 4)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<FilterRuleDetail> Rules { get; set; } = new List<FilterRuleDetail>();

    [DataMember(Order = 6)]
    public int TotalViewUsageCount { get; set; }

    [DataMember(Order = 7)]
    public int TotalTemplateUsageCount { get; set; }

    [DataMember(Order = 8)]
    public List<FilterUsageEntry> ViewUsages { get; set; } = new List<FilterUsageEntry>();

    [DataMember(Order = 9)]
    public List<FilterUsageEntry> TemplateUsages { get; set; } = new List<FilterUsageEntry>();
}

// ── Remove filter from view ──────────────────────────────────────────────────

[DataContract]
public sealed class RemoveFilterFromViewRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? ViewId { get; set; }

    [DataMember(Order = 3)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? FilterId { get; set; }

    [DataMember(Order = 5)]
    public string FilterName { get; set; } = string.Empty;
}

// ── Delete filter element ────────────────────────────────────────────────────

[DataContract]
public sealed class DeleteFilterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? FilterId { get; set; }

    [DataMember(Order = 3)]
    public string FilterName { get; set; } = string.Empty;

    /// <summary>
    /// false (default): preview fail nếu filter đang được dùng ở view/template nào.
    /// true: tự động remove khỏi tất cả views/templates trước khi xoá.
    /// </summary>
    [DataMember(Order = 4)]
    public bool ForceRemoveFromAllViews { get; set; }
}

// ── 3D View ──────────────────────────────────────────────────────────────────

[DataContract]
public sealed class Create3DViewRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool UseActive3DOrientationWhenPossible { get; set; } = true;

    [DataMember(Order = 4)]
    public bool CopySectionBoxFromActive3D { get; set; } = true;

    [DataMember(Order = 5)]
    public int? ViewTemplateId { get; set; }

    [DataMember(Order = 6)]
    public string ViewTemplateName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool FailIfExists { get; set; } = true;

    [DataMember(Order = 8)]
    public bool DuplicateIfExists { get; set; }

    [DataMember(Order = 9)]
    public bool ActivateViewAfterCreate { get; set; }
}

[DataContract]
public sealed class ViewFilterRuleRequest
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Operator { get; set; } = "equals";

    [DataMember(Order = 3)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool CaseSensitive { get; set; }
}

[DataContract]
public sealed class CreateOrUpdateViewFilterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FilterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<int> CategoryIds { get; set; } = new List<int>();

    [DataMember(Order = 4)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<ViewFilterRuleRequest> Rules { get; set; } = new List<ViewFilterRuleRequest>();

    [DataMember(Order = 6)]
    public bool OverwriteIfExists { get; set; } = true;

    [DataMember(Order = 7)]
    public bool InferCategoriesFromSelectionWhenEmpty { get; set; } = true;
}

[DataContract]
public sealed class ApplyViewFilterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? ViewId { get; set; }

    [DataMember(Order = 3)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? FilterId { get; set; }

    [DataMember(Order = 5)]
    public string FilterName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public bool? Visible { get; set; }

    [DataMember(Order = 7)]
    public bool? Halftone { get; set; }

    [DataMember(Order = 8)]
    public int? Transparency { get; set; }

    [DataMember(Order = 9)]
    public int? ProjectionLineColorRed { get; set; }

    [DataMember(Order = 10)]
    public int? ProjectionLineColorGreen { get; set; }

    [DataMember(Order = 11)]
    public int? ProjectionLineColorBlue { get; set; }
}
