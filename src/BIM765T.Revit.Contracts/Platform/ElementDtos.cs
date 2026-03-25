using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class BoundingBoxDto
{
    [DataMember(Order = 1)]
    public double MinX { get; set; }

    [DataMember(Order = 2)]
    public double MinY { get; set; }

    [DataMember(Order = 3)]
    public double MinZ { get; set; }

    [DataMember(Order = 4)]
    public double MaxX { get; set; }

    [DataMember(Order = 5)]
    public double MaxY { get; set; }

    [DataMember(Order = 6)]
    public double MaxZ { get; set; }
}

[DataContract]
public sealed class ParameterValueDto
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StorageType { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsReadOnly { get; set; }
}

[DataContract]
public sealed class ElementSummaryDto
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string UniqueId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ClassName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int TypeId { get; set; }

    [DataMember(Order = 8)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 10, EmitDefaultValue = false)]
    public int? LevelId { get; set; }

    [DataMember(Order = 11)]
    public string LevelName { get; set; } = string.Empty;

    [DataMember(Order = 12, EmitDefaultValue = false)]
    public BoundingBoxDto? BoundingBox { get; set; }

    [DataMember(Order = 13)]
    public List<ParameterValueDto> Parameters { get; set; } = new List<ParameterValueDto>();

    [DataMember(Order = 14)]
    public string FamilyPlacementType { get; set; } = string.Empty;

    [DataMember(Order = 15, EmitDefaultValue = false)]
    public int? WorksetId { get; set; }

    [DataMember(Order = 16)]
    public string WorksetName { get; set; } = string.Empty;

    [DataMember(Order = 17, EmitDefaultValue = false)]
    public AxisVectorDto? LocationPoint { get; set; }

    [DataMember(Order = 18, EmitDefaultValue = false)]
    public AxisVectorDto? LocationCurveStart { get; set; }

    [DataMember(Order = 19, EmitDefaultValue = false)]
    public AxisVectorDto? LocationCurveEnd { get; set; }

    [DataMember(Order = 20, EmitDefaultValue = false)]
    public double? LocationCurveLength { get; set; }
}

[DataContract]
public sealed class ElementQueryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool ViewScopeOnly { get; set; } = true;

    [DataMember(Order = 3)]
    public bool SelectedOnly { get; set; }

    [DataMember(Order = 4)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public string ClassName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int MaxResults { get; set; } = 200;

    [DataMember(Order = 8)]
    public bool IncludeParameters { get; set; }
}

[DataContract]
public sealed class ElementQueryResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public List<ElementSummaryDto> Items { get; set; } = new List<ElementSummaryDto>();
}
