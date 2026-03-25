using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class ElementTypeQueryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public string ClassName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string NameContains { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool IncludeParameters { get; set; }

    [DataMember(Order = 6)]
    public bool OnlyInUse { get; set; }

    [DataMember(Order = 7)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class ElementTypeCatalogResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public List<ElementTypeSummaryDto> Items { get; set; } = new List<ElementTypeSummaryDto>();
}

[DataContract]
public sealed class ElementTypeSummaryDto
{
    [DataMember(Order = 1)]
    public int TypeId { get; set; }

    [DataMember(Order = 2)]
    public string UniqueId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ClassName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public int UsageCount { get; set; }

    [DataMember(Order = 9)]
    public bool IsInUse { get; set; }

    [DataMember(Order = 10)]
    public List<ParameterValueDto> Parameters { get; set; } = new List<ParameterValueDto>();
}

[DataContract]
public sealed class TextNoteTypeUsageRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string NameContains { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool OnlyInUse { get; set; } = true;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 200;

    [DataMember(Order = 5)]
    public int MaxSampleTextNotesPerType { get; set; } = 10;
}

[DataContract]
public sealed class TextNoteTypeUsageResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public List<TextNoteTypeUsageDto> Items { get; set; } = new List<TextNoteTypeUsageDto>();
}

[DataContract]
public sealed class TextNoteTypeUsageDto
{
    [DataMember(Order = 1)]
    public int TypeId { get; set; }

    [DataMember(Order = 2)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int UsageCount { get; set; }

    [DataMember(Order = 5)]
    public string TextSize { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int ColorValue { get; set; }

    [DataMember(Order = 7)]
    public string Font { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<int> SampleTextNoteIds { get; set; } = new List<int>();
}
