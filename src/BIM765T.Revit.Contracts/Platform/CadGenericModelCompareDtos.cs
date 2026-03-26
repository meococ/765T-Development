using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class CadGenericModelOverlapRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? ViewId { get; set; }

    [DataMember(Order = 3)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ImportNameContains { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string GenericModelNameContains { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string GenericModelFamilyNameContains { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public double ToleranceFeet { get; set; } = 0.00328084;

    [DataMember(Order = 8)]
    public double SamplingStepFeet { get; set; } = 0.0328084;

    [DataMember(Order = 9)]
    public int MaxElementsPerSide { get; set; } = 500;

    [DataMember(Order = 10)]
    public int MaxPreviewPoints { get; set; } = 20;

    [DataMember(Order = 11)]
    public int MaxSamplePointsPerSide { get; set; } = 150000;
}

[DataContract]
public sealed class ProjectedBoundsDto
{
    [DataMember(Order = 1)]
    public double MinU { get; set; }

    [DataMember(Order = 2)]
    public double MinV { get; set; }

    [DataMember(Order = 3)]
    public double MaxU { get; set; }

    [DataMember(Order = 4)]
    public double MaxV { get; set; }

    [DataMember(Order = 5)]
    public double Width { get; set; }

    [DataMember(Order = 6)]
    public double Height { get; set; }
}

[DataContract]
public sealed class ProjectedPoint2dDto
{
    [DataMember(Order = 1)]
    public double U { get; set; }

    [DataMember(Order = 2)]
    public double V { get; set; }
}

[DataContract]
public sealed class CadGenericElementDigestDto
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ClassName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int SamplePointCount { get; set; }

    [DataMember(Order = 8, EmitDefaultValue = false)]
    public ProjectedBoundsDto? ProjectedBounds { get; set; }
}

[DataContract]
public sealed class CadGenericModelOverlapResponse
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
    public string ComparisonMode { get; set; } = "projected_point_cloud";

    [DataMember(Order = 6)]
    public double ToleranceFeet { get; set; }

    [DataMember(Order = 7)]
    public double SamplingStepFeet { get; set; }

    [DataMember(Order = 8)]
    public int ImportCadCount { get; set; }

    [DataMember(Order = 9)]
    public int GenericModelCount { get; set; }

    [DataMember(Order = 10)]
    public int ProcessedImportCadCount { get; set; }

    [DataMember(Order = 11)]
    public int ProcessedGenericModelCount { get; set; }

    [DataMember(Order = 12)]
    public bool ImportCadScopeTruncated { get; set; }

    [DataMember(Order = 13)]
    public bool GenericModelScopeTruncated { get; set; }

    [DataMember(Order = 14)]
    public bool CadSampleLimitHit { get; set; }

    [DataMember(Order = 15)]
    public bool GenericModelSampleLimitHit { get; set; }

    [DataMember(Order = 16)]
    public int CadSamplePointCount { get; set; }

    [DataMember(Order = 17)]
    public int GenericModelSamplePointCount { get; set; }

    [DataMember(Order = 18)]
    public int SharedSamplePointCount { get; set; }

    [DataMember(Order = 19)]
    public int CadOnlySamplePointCount { get; set; }

    [DataMember(Order = 20)]
    public int GenericModelOnlySamplePointCount { get; set; }

    [DataMember(Order = 21)]
    public double OverlapRatio { get; set; }

    [DataMember(Order = 22)]
    public double CadCoverageRatio { get; set; }

    [DataMember(Order = 23)]
    public double GenericModelCoverageRatio { get; set; }

    [DataMember(Order = 24)]
    public bool IsExactMatch { get; set; }

    [DataMember(Order = 25)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 26)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 27, EmitDefaultValue = false)]
    public ProjectedBoundsDto? CadProjectedBounds { get; set; }

    [DataMember(Order = 28, EmitDefaultValue = false)]
    public ProjectedBoundsDto? GenericModelProjectedBounds { get; set; }

    [DataMember(Order = 29)]
    public List<CadGenericElementDigestDto> CadElements { get; set; } = new List<CadGenericElementDigestDto>();

    [DataMember(Order = 30)]
    public List<CadGenericElementDigestDto> GenericModelElements { get; set; } = new List<CadGenericElementDigestDto>();

    [DataMember(Order = 31)]
    public List<ProjectedPoint2dDto> CadOnlyPreviewPoints { get; set; } = new List<ProjectedPoint2dDto>();

    [DataMember(Order = 32)]
    public List<ProjectedPoint2dDto> GenericModelOnlyPreviewPoints { get; set; } = new List<ProjectedPoint2dDto>();

    [DataMember(Order = 33)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}
