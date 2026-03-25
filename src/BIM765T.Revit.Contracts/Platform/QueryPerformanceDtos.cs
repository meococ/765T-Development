using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ══════════════════════════════════════════
// Phase A: Query Performance DTOs
// Quick filters, parameter filters, spatial pre-screen, batch inspect, caching
// ══════════════════════════════════════════

// ── A-1: Quick Filter ──

[DataContract]
public sealed class QuickFilterRequest
{
    /// <summary>category | class | multi_category | bbox_intersects | bbox_contains_point | bbox_inside</summary>
    [DataMember(Order = 1)]
    public string FilterType { get; set; } = "category";

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public string ClassName { get; set; } = string.Empty;

    /// <summary>BoundingBox min point [X, Y, Z] in feet</summary>
    [DataMember(Order = 4)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    /// <summary>BoundingBox max point [X, Y, Z] in feet</summary>
    [DataMember(Order = 5)]
    public List<double> BBoxMax { get; set; } = new List<double>();

    /// <summary>Single point [X, Y, Z] in feet for contains_point filter</summary>
    [DataMember(Order = 6)]
    public List<double> Point { get; set; } = new List<double>();

    /// <summary>Optional view scope — filter within this view only</summary>
    [DataMember(Order = 7)]
    public int ViewId { get; set; }

    [DataMember(Order = 8)]
    public bool ExcludeElementTypes { get; set; } = true;

    [DataMember(Order = 9)]
    public int MaxResults { get; set; } = 10000;

    [DataMember(Order = 10)]
    public string DocumentKey { get; set; } = string.Empty;
}

[DataContract]
public sealed class QuickFilterResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FilterType { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int MatchCount { get; set; }

    [DataMember(Order = 4)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public double TimingMs { get; set; }
}

// ── A-2: Parameter Filter ──

[DataContract]
public sealed class ParameterFilterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>equals | not_equals | contains | starts_with | ends_with | greater | less | greater_or_equal | less_or_equal | has_value | has_no_value</summary>
    [DataMember(Order = 3)]
    public string Operator { get; set; } = "equals";

    [DataMember(Order = 4)]
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional: restrict to these categories</summary>
    [DataMember(Order = 5)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public bool ExcludeElementTypes { get; set; } = true;

    [DataMember(Order = 7)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 8)]
    public int ViewId { get; set; }
}

[DataContract]
public sealed class ParameterFilterResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Operator { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MatchCount { get; set; }

    [DataMember(Order = 5)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public double TimingMs { get; set; }
}

// ── A-3: Logical Compound Filter ──

[DataContract]
public sealed class CompoundFilterRule
{
    /// <summary>category | class | parameter | bbox_intersects</summary>
    [DataMember(Order = 1)]
    public string Type { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public string ClassName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Operator { get; set; } = "equals";

    [DataMember(Order = 6)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    [DataMember(Order = 8)]
    public List<double> BBoxMax { get; set; } = new List<double>();

    /// <summary>Set true to negate this rule (NOT)</summary>
    [DataMember(Order = 9)]
    public bool Negate { get; set; }
}

[DataContract]
public sealed class CompoundFilterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>and | or</summary>
    [DataMember(Order = 2)]
    public string Logic { get; set; } = "and";

    [DataMember(Order = 3)]
    public List<CompoundFilterRule> Rules { get; set; } = new List<CompoundFilterRule>();

    [DataMember(Order = 4)]
    public bool ExcludeElementTypes { get; set; } = true;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 6)]
    public int ViewId { get; set; }
}

[DataContract]
public sealed class CompoundFilterResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Logic { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int RuleCount { get; set; }

    [DataMember(Order = 4)]
    public int MatchCount { get; set; }

    [DataMember(Order = 5)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public double TimingMs { get; set; }
}

// ── A-4: Spatial Pre-screen ──

[DataContract]
public sealed class SpatialPrescreenRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>bbox_intersects | bbox_contains_point | bbox_inside | level_range</summary>
    [DataMember(Order = 2)]
    public string Mode { get; set; } = "bbox_intersects";

    [DataMember(Order = 3)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    [DataMember(Order = 4)]
    public List<double> BBoxMax { get; set; } = new List<double>();

    [DataMember(Order = 5)]
    public List<double> Point { get; set; } = new List<double>();

    /// <summary>Level name range for level_range mode</summary>
    [DataMember(Order = 6)]
    public string LevelFrom { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string LevelTo { get; set; } = string.Empty;

    /// <summary>Optional category filter</summary>
    [DataMember(Order = 8)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public int MaxResults { get; set; } = 10000;
}

[DataContract]
public sealed class SpatialPrescreenResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Mode { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int MatchCount { get; set; }

    [DataMember(Order = 4)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public List<SpatialElementSummary> Elements { get; set; } = new List<SpatialElementSummary>();

    [DataMember(Order = 6)]
    public double TimingMs { get; set; }
}

[DataContract]
public sealed class SpatialElementSummary
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>BoundingBox min [X, Y, Z]</summary>
    [DataMember(Order = 5)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    /// <summary>BoundingBox max [X, Y, Z]</summary>
    [DataMember(Order = 6)]
    public List<double> BBoxMax { get; set; } = new List<double>();
}

// ── A-5: Element Index Cache ──

[DataContract]
public sealed class ElementIndexRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>build | query | invalidate | stats</summary>
    [DataMember(Order = 2)]
    public string Action { get; set; } = "build";

    /// <summary>For query: category name to look up</summary>
    [DataMember(Order = 3)]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>For query: parameter name to search</summary>
    [DataMember(Order = 4)]
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>For query: parameter value to match</summary>
    [DataMember(Order = 5)]
    public string ParameterValue { get; set; } = string.Empty;
}

[DataContract]
public sealed class ElementIndexResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Action { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int IndexedCategoryCount { get; set; }

    [DataMember(Order = 4)]
    public int IndexedElementCount { get; set; }

    [DataMember(Order = 5)]
    public List<int> MatchedElementIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public long CacheAgeMs { get; set; }

    [DataMember(Order = 7)]
    public bool CacheValid { get; set; }

    [DataMember(Order = 8)]
    public double TimingMs { get; set; }
}

// ── A-6: Multi-Category Batch Query ──

[DataContract]
public sealed class MultiCategoryQueryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public List<string> ClassNames { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public bool ExcludeElementTypes { get; set; } = true;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 10000;

    [DataMember(Order = 6)]
    public int ViewId { get; set; }

    /// <summary>If true, group results by category in the response</summary>
    [DataMember(Order = 7)]
    public bool GroupByCategory { get; set; } = true;
}

[DataContract]
public sealed class CategoryGroupItem
{
    [DataMember(Order = 1)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public List<int> ElementIds { get; set; } = new List<int>();
}

[DataContract]
public sealed class MultiCategoryQueryResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalCount { get; set; }

    [DataMember(Order = 3)]
    public List<CategoryGroupItem> Groups { get; set; } = new List<CategoryGroupItem>();

    [DataMember(Order = 4)]
    public List<int> AllElementIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public double TimingMs { get; set; }
}

// ── A-7: Fast Element Count ──

[DataContract]
public sealed class ElementCountRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public string ClassName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool ExcludeElementTypes { get; set; } = true;

    [DataMember(Order = 5)]
    public int ViewId { get; set; }

    /// <summary>If true, return per-category counts</summary>
    [DataMember(Order = 6)]
    public bool BreakdownByCategory { get; set; }
}

[DataContract]
public sealed class CategoryCountItem
{
    [DataMember(Order = 1)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }
}

[DataContract]
public sealed class ElementCountResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalCount { get; set; }

    [DataMember(Order = 3)]
    public List<CategoryCountItem> CategoryCounts { get; set; } = new List<CategoryCountItem>();

    [DataMember(Order = 4)]
    public double TimingMs { get; set; }
}

// ── A-8: Batch Element Inspect ──

[DataContract]
public sealed class BatchInspectRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    /// <summary>Specific parameter names to retrieve. Empty = all parameters.</summary>
    [DataMember(Order = 3)]
    public List<string> ParameterNames { get; set; } = new List<string>();

    /// <summary>Include element geometry bounding box info</summary>
    [DataMember(Order = 4)]
    public bool IncludeBoundingBox { get; set; }

    /// <summary>Include dependent element IDs</summary>
    [DataMember(Order = 5)]
    public bool IncludeDependents { get; set; }

    [DataMember(Order = 6)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class BatchInspectElementItem
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

    [DataMember(Order = 7)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    [DataMember(Order = 8)]
    public List<double> BBoxMax { get; set; } = new List<double>();

    [DataMember(Order = 9)]
    public List<int> DependentElementIds { get; set; } = new List<int>();
}

[DataContract]
public sealed class BatchInspectResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int RequestedCount { get; set; }

    [DataMember(Order = 3)]
    public int ResolvedCount { get; set; }

    [DataMember(Order = 4)]
    public List<BatchInspectElementItem> Elements { get; set; } = new List<BatchInspectElementItem>();

    [DataMember(Order = 5)]
    public List<int> NotFoundIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public double TimingMs { get; set; }
}
