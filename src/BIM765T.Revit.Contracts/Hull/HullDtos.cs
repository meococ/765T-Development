using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Hull;

[DataContract]
public sealed class CounterValue
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }
}

[DataContract]
public sealed class HullSourceInfo
{
    [DataMember(Order = 1)]
    public int SourceId { get; set; }

    [DataMember(Order = 2)]
    public string UniqueId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SourceKind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 6, EmitDefaultValue = false)]
    public int? LevelId { get; set; }

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public string? LevelName { get; set; }

    [DataMember(Order = 8, EmitDefaultValue = false)]
    public string? CassetteId { get; set; }

    [DataMember(Order = 9, EmitDefaultValue = false)]
    public string? PodId { get; set; }

    [DataMember(Order = 10, EmitDefaultValue = false)]
    public string? Comments { get; set; }

    [DataMember(Order = 11, EmitDefaultValue = false)]
    public double? StructureThicknessInch { get; set; }

    [DataMember(Order = 12)]
    public bool Eligible { get; set; }

    [DataMember(Order = 13)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
}

[DataContract]
public sealed class HullCollectionResponse
{
    [DataMember(Order = 1)]
    public string DocumentName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int TotalScanned { get; set; }

    [DataMember(Order = 4)]
    public int EligibleCount { get; set; }

    [DataMember(Order = 5)]
    public List<HullSourceInfo> Sources { get; set; } = new List<HullSourceInfo>();
}

[DataContract]
public sealed class HullDryRunRequest
{
    [DataMember(Order = 1)]
    public double InOffsetInch { get; set; } = -0.25;

    [DataMember(Order = 2)]
    public double ExCutInch { get; set; } = 0.59375;

    [DataMember(Order = 3)]
    public double WallHoldbackInch { get; set; } = 1.5;

    [DataMember(Order = 4)]
    public double FCHoldbackInch { get; set; } = 1.5;

    [DataMember(Order = 5)]
    public bool UseInOffset { get; set; } = true;

    [DataMember(Order = 6)]
    public bool UseExCut { get; set; } = true;

    [DataMember(Order = 7)]
    public bool UseWallHoldback { get; set; } = true;

    [DataMember(Order = 8)]
    public bool UseFCHoldback { get; set; } = true;

    [DataMember(Order = 9)]
    public bool IncludeDetails { get; set; } = true;
}

[DataContract]
public sealed class HullPlannedAction
{
    [DataMember(Order = 1)]
    public int SourceId { get; set; }

    [DataMember(Order = 2)]
    public string SourceKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Action { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string FamilyKey { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Classification { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string TraceKey { get; set; } = string.Empty;

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public double? DimDepthInch { get; set; }

    [DataMember(Order = 8, EmitDefaultValue = false)]
    public double? DimLengthInch { get; set; }

    [DataMember(Order = 9, EmitDefaultValue = false)]
    public double? DimHeightOrWidthInch { get; set; }

    [DataMember(Order = 10)]
    public bool SplitTwoPanels { get; set; }

    [DataMember(Order = 11)]
    public double Confidence { get; set; }

    [DataMember(Order = 12)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
}

[DataContract]
public sealed class HullDryRunResponse
{
    [DataMember(Order = 1)]
    public string DocumentName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int EligibleCount { get; set; }

    [DataMember(Order = 4)]
    public int PlannedUpsertCount { get; set; }

    [DataMember(Order = 5)]
    public int SkipCount { get; set; }

    [DataMember(Order = 6)]
    public List<CounterValue> ClassificationCounts { get; set; } = new List<CounterValue>();

    [DataMember(Order = 7)]
    public List<HullPlannedAction> Actions { get; set; } = new List<HullPlannedAction>();

    [DataMember(Order = 8)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
}
