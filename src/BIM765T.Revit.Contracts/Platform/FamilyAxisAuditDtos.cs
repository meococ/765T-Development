using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class FamilyAxisAlignmentRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? ViewId { get; set; }

    [DataMember(Order = 3)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public double AngleToleranceDegrees { get; set; } = 5.0;

    [DataMember(Order = 6)]
    public bool TreatMirroredAsMismatch { get; set; } = true;

    [DataMember(Order = 7)]
    public bool TreatAntiParallelAsMismatch { get; set; }

    [DataMember(Order = 8)]
    public bool HighlightInUi { get; set; } = true;

    [DataMember(Order = 9)]
    public bool IncludeAlignedItems { get; set; }

    [DataMember(Order = 10)]
    public int MaxElements { get; set; } = 2000;

    [DataMember(Order = 11)]
    public int MaxIssues { get; set; } = 200;

    [DataMember(Order = 12)]
    public bool ZoomToHighlighted { get; set; }

    [DataMember(Order = 13)]
    public bool AnalyzeNestedFamilies { get; set; } = true;

    [DataMember(Order = 14)]
    public int MaxFamilyDefinitionsToInspect { get; set; } = 150;

    [DataMember(Order = 15)]
    public int MaxNestedInstancesPerFamily { get; set; } = 200;

    [DataMember(Order = 16)]
    public int MaxNestedFindingsPerFamily { get; set; } = 20;

    [DataMember(Order = 17)]
    public bool TreatNonSharedNestedAsRisk { get; set; } = true;

    [DataMember(Order = 18)]
    public bool TreatNestedMirroredAsRisk { get; set; } = true;

    [DataMember(Order = 19)]
    public bool TreatNestedRotatedAsRisk { get; set; } = true;

    [DataMember(Order = 20)]
    public bool TreatNestedTiltedAsRisk { get; set; } = true;

    [DataMember(Order = 21)]
    public bool IncludeNestedFindings { get; set; }

    [DataMember(Order = 22)]
    public bool UseActiveViewOnly { get; set; } = true;
}

[DataContract]
public sealed class AxisVectorDto
{
    [DataMember(Order = 1)]
    public double X { get; set; }

    [DataMember(Order = 2)]
    public double Y { get; set; }

    [DataMember(Order = 3)]
    public double Z { get; set; }
}

[DataContract]
public sealed class FamilyAxisAlignmentItemDto
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string UniqueId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string InstanceName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool Mirrored { get; set; }

    [DataMember(Order = 8)]
    public bool MatchesProjectAxes { get; set; }

    [DataMember(Order = 9)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string Reason { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public double AngleXDegrees { get; set; }

    [DataMember(Order = 12)]
    public double AngleYDegrees { get; set; }

    [DataMember(Order = 13)]
    public double AngleZDegrees { get; set; }

    [DataMember(Order = 14)]
    public double RotationAroundProjectZDegrees { get; set; }

    [DataMember(Order = 15)]
    public AxisVectorDto Origin { get; set; } = new AxisVectorDto();

    [DataMember(Order = 16)]
    public AxisVectorDto BasisX { get; set; } = new AxisVectorDto();

    [DataMember(Order = 17)]
    public AxisVectorDto BasisY { get; set; } = new AxisVectorDto();

    [DataMember(Order = 18)]
    public AxisVectorDto BasisZ { get; set; } = new AxisVectorDto();

    [DataMember(Order = 19)]
    public string ProjectAxisStatus { get; set; } = string.Empty;

    [DataMember(Order = 20)]
    public string ProjectAxisReason { get; set; } = string.Empty;

    [DataMember(Order = 21)]
    public bool NeedsReview { get; set; }

    [DataMember(Order = 22)]
    public bool HasNestedTransformRisk { get; set; }

    [DataMember(Order = 23)]
    public bool HasNestedIfcRisk { get; set; }

    [DataMember(Order = 24)]
    public bool HasNonSharedNestedFamilies { get; set; }

    [DataMember(Order = 25)]
    public bool HasSharedNestedFamilies { get; set; }

    [DataMember(Order = 26)]
    public bool HostFamilyContainsVoidForms { get; set; }

    [DataMember(Order = 27)]
    public int NestedRiskCount { get; set; }

    [DataMember(Order = 28)]
    public string NestedRiskSummary { get; set; } = string.Empty;

    [DataMember(Order = 29)]
    public List<FamilyNestedRiskDto> NestedFindings { get; set; } = new List<FamilyNestedRiskDto>();
}

[DataContract]
public sealed class FamilyNestedRiskDto
{
    [DataMember(Order = 1)]
    public string NestedFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string NestedTypeName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool Shared { get; set; }

    [DataMember(Order = 4)]
    public bool Mirrored { get; set; }

    [DataMember(Order = 5)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Reason { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public double AngleXDegrees { get; set; }

    [DataMember(Order = 8)]
    public double AngleYDegrees { get; set; }

    [DataMember(Order = 9)]
    public double AngleZDegrees { get; set; }

    [DataMember(Order = 10)]
    public double RotationInHostDegrees { get; set; }

    [DataMember(Order = 11)]
    public bool HasIfcTransformRisk { get; set; }
}

[DataContract]
public sealed class FamilyAxisAlignmentResponse
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
    public double AngleToleranceDegrees { get; set; }

    [DataMember(Order = 6)]
    public int TotalFamilyInstances { get; set; }

    [DataMember(Order = 7)]
    public int CheckedCount { get; set; }

    [DataMember(Order = 8)]
    public int AlignedCount { get; set; }

    [DataMember(Order = 9)]
    public int MismatchCount { get; set; }

    [DataMember(Order = 10)]
    public bool HighlightRequested { get; set; }

    [DataMember(Order = 11)]
    public bool HighlightApplied { get; set; }

    [DataMember(Order = 12)]
    public int HighlightedCount { get; set; }

    [DataMember(Order = 13)]
    public bool Truncated { get; set; }

    [DataMember(Order = 14)]
    public List<FamilyAxisAlignmentItemDto> Items { get; set; } = new List<FamilyAxisAlignmentItemDto>();

    [DataMember(Order = 15)]
    public ReviewReport Review { get; set; } = new ReviewReport();

    [DataMember(Order = 16)]
    public int DistinctFamilyDefinitions { get; set; }

    [DataMember(Order = 17)]
    public int AnalyzedFamilyDefinitions { get; set; }

    [DataMember(Order = 18)]
    public int NestedRiskHostCount { get; set; }

    [DataMember(Order = 19)]
    public bool NestedAnalysisTruncated { get; set; }
}
