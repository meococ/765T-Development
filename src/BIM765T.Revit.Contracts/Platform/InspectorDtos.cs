using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class ElementExplainRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ElementId { get; set; }

    [DataMember(Order = 3)]
    public bool IncludeParameters { get; set; } = true;

    [DataMember(Order = 4)]
    public List<string> ParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public bool IncludeDependents { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IncludeHostRelations { get; set; } = true;
}

[DataContract]
public sealed class ElementExplainResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public ElementSummaryDto Element { get; set; } = new ElementSummaryDto();

    [DataMember(Order = 3)]
    public string OwnerViewKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? OwnerViewId { get; set; }

    [DataMember(Order = 5)]
    public int? HostElementId { get; set; }

    [DataMember(Order = 6)]
    public string HostCategoryName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<int> DependentElementIds { get; set; } = new List<int>();

    [DataMember(Order = 8)]
    public int? SuperComponentElementId { get; set; }

    [DataMember(Order = 9)]
    public string SuperComponentCategoryName { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public List<string> Explanations { get; set; } = new List<string>();
}

[DataContract]
public sealed class ElementGraphRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public int MaxDepth { get; set; } = 1;

    [DataMember(Order = 4)]
    public bool IncludeDependents { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeHost { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IncludeType { get; set; } = true;

    [DataMember(Order = 7)]
    public bool IncludeOwnerView { get; set; } = true;
}

[DataContract]
public sealed class GraphNodeDto
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string Label { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Kind { get; set; } = string.Empty;
}

[DataContract]
public sealed class GraphEdgeDto
{
    [DataMember(Order = 1)]
    public int FromElementId { get; set; }

    [DataMember(Order = 2)]
    public int ToElementId { get; set; }

    [DataMember(Order = 3)]
    public string Relation { get; set; } = string.Empty;
}

[DataContract]
public sealed class ElementGraphResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<GraphNodeDto> Nodes { get; set; } = new List<GraphNodeDto>();

    [DataMember(Order = 3)]
    public List<GraphEdgeDto> Edges { get; set; } = new List<GraphEdgeDto>();
}

[DataContract]
public sealed class ParameterTraceRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 4)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 200;

    [DataMember(Order = 6)]
    public bool IncludeEmptyValues { get; set; }
}

[DataContract]
public sealed class ParameterTraceItem
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ElementName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool IsReadOnly { get; set; }

    [DataMember(Order = 6)]
    public string StorageType { get; set; } = string.Empty;
}

[DataContract]
public sealed class ParameterTraceResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int Count { get; set; }

    [DataMember(Order = 4)]
    public List<ParameterTraceItem> Items { get; set; } = new List<ParameterTraceItem>();
}

[DataContract]
public sealed class ViewUsageRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? ViewId { get; set; }

    [DataMember(Order = 3)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IncludeSheets { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeFilters { get; set; } = true;

    [DataMember(Order = 6)]
    public int MaxSamples { get; set; } = 20;
}

[DataContract]
public sealed class ViewUsageResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public ViewSummaryDto View { get; set; } = new ViewSummaryDto();

    [DataMember(Order = 3)]
    public int VisibleElementCountEstimate { get; set; }

    [DataMember(Order = 4)]
    public List<string> AppliedFilters { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> PlacedOnSheets { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<int> SampleElementIds { get; set; } = new List<int>();
}

[DataContract]
public sealed class SheetDependenciesRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? SheetId { get; set; }

    [DataMember(Order = 3)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IncludeSchedules { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeViewports { get; set; } = true;
}

[DataContract]
public sealed class SheetDependencyItem
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Name { get; set; } = string.Empty;
}

[DataContract]
public sealed class SheetDependenciesResponse
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
    public List<SheetDependencyItem> Dependencies { get; set; } = new List<SheetDependencyItem>();
}
