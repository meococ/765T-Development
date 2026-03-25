using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class FamilyXrayRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int FamilyId { get; set; }

    [DataMember(Order = 3)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IncludeNestedFamilies { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeConnectors { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IncludeReferencePlanes { get; set; } = true;

    [DataMember(Order = 7)]
    public int MaxNestedFamilies { get; set; } = 25;

    [DataMember(Order = 8)]
    public int MaxParameters { get; set; } = 200;

    [DataMember(Order = 9)]
    public int MaxTypeNames { get; set; } = 25;
}

[DataContract]
public sealed class FamilyNestedFamilyInfo
{
    [DataMember(Order = 1)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool IsShared { get; set; }

    [DataMember(Order = 4)]
    public int Count { get; set; }
}

[DataContract]
public sealed class FamilyFormulaInfo
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Formula { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyConnectorInfo
{
    [DataMember(Order = 1)]
    public int ConnectorId { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Domain { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SystemClassification { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Shape { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string DirectionSummary { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyXrayResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int FamilyId { get; set; }

    [DataMember(Order = 3)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SourceDocumentTitle { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public bool IsFamilyDocument { get; set; }

    [DataMember(Order = 7)]
    public string TemplateHint { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public int TypesCount { get; set; }

    [DataMember(Order = 9)]
    public List<string> TypeNames { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<FamilyNestedFamilyInfo> NestedFamilies { get; set; } = new List<FamilyNestedFamilyInfo>();

    [DataMember(Order = 11)]
    public List<string> InstanceParameters { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> TypeParameters { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public List<FamilyFormulaInfo> FormulaParameters { get; set; } = new List<FamilyFormulaInfo>();

    [DataMember(Order = 14)]
    public List<string> ReferencePlanes { get; set; } = new List<string>();

    [DataMember(Order = 15)]
    public List<FamilyConnectorInfo> Connectors { get; set; } = new List<FamilyConnectorInfo>();

    [DataMember(Order = 16)]
    public List<string> Issues { get; set; } = new List<string>();

    [DataMember(Order = 17)]
    public string Summary { get; set; } = string.Empty;
}
