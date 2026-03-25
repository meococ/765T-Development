using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ══════════════════════════════════════════
// Phase B: Spatial Intelligence DTOs
// Clash detection, raycast, proximity, geometry extraction,
// zone analysis, opening detection, DirectShape
// ══════════════════════════════════════════

// ── B-1: Clash Detection ──

[DataContract]
public sealed class ClashDetectRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Source category/element IDs for clash checking</summary>
    [DataMember(Order = 2)]
    public List<string> SourceCategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public List<int> SourceElementIds { get; set; } = new List<int>();

    /// <summary>Target category/element IDs to check against</summary>
    [DataMember(Order = 4)]
    public List<string> TargetCategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<int> TargetElementIds { get; set; } = new List<int>();

    /// <summary>Soft clash tolerance in feet (0 = hard clash only)</summary>
    [DataMember(Order = 6)]
    public double Tolerance { get; set; }

    [DataMember(Order = 7)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class ClashResult
{
    [DataMember(Order = 1)]
    public int SourceElementId { get; set; }

    [DataMember(Order = 2)]
    public string SourceCategory { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int TargetElementId { get; set; }

    [DataMember(Order = 4)]
    public string TargetCategory { get; set; } = string.Empty;

    /// <summary>hard | soft | duplicate</summary>
    [DataMember(Order = 5)]
    public string ClashType { get; set; } = "hard";

    /// <summary>Approximate distance in feet (0 for hard clash)</summary>
    [DataMember(Order = 6)]
    public double ApproxDistance { get; set; }

    /// <summary>Clash intersection point [X, Y, Z]</summary>
    [DataMember(Order = 7)]
    public List<double> IntersectionPoint { get; set; } = new List<double>();
}

[DataContract]
public sealed class ClashDetectResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SourceCount { get; set; }

    [DataMember(Order = 3)]
    public int TargetCount { get; set; }

    [DataMember(Order = 4)]
    public int ClashCount { get; set; }

    [DataMember(Order = 5)]
    public List<ClashResult> Clashes { get; set; } = new List<ClashResult>();

    [DataMember(Order = 6)]
    public int HardClashCount { get; set; }

    [DataMember(Order = 7)]
    public int SoftClashCount { get; set; }

    [DataMember(Order = 8)]
    public double TimingMs { get; set; }
}

// ── B-2: Proximity Search ──

[DataContract]
public sealed class ProximitySearchRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> SourceElementIds { get; set; } = new List<int>();

    /// <summary>Search radius in feet</summary>
    [DataMember(Order = 3)]
    public double Radius { get; set; } = 3.0;

    [DataMember(Order = 4)]
    public List<string> TargetCategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class ProximityResult
{
    [DataMember(Order = 1)]
    public int SourceElementId { get; set; }

    [DataMember(Order = 2)]
    public int NearbyElementId { get; set; }

    [DataMember(Order = 3)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public double ApproxDistance { get; set; }
}

[DataContract]
public sealed class ProximitySearchResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int NearbyCount { get; set; }

    [DataMember(Order = 3)]
    public List<ProximityResult> Results { get; set; } = new List<ProximityResult>();

    [DataMember(Order = 4)]
    public double TimingMs { get; set; }
}

// ── B-3: Raycast ──

[DataContract]
public sealed class RaycastRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Ray origin [X, Y, Z] in feet</summary>
    [DataMember(Order = 2)]
    public List<double> Origin { get; set; } = new List<double>();

    /// <summary>Ray direction [X, Y, Z] (will be normalized)</summary>
    [DataMember(Order = 3)]
    public List<double> Direction { get; set; } = new List<double>();

    /// <summary>3D view ID to use for raycast context</summary>
    [DataMember(Order = 4)]
    public int View3DId { get; set; }

    [DataMember(Order = 5)]
    public bool FindReferencesInLinks { get; set; }

    [DataMember(Order = 6)]
    public int MaxHits { get; set; } = 20;
}

[DataContract]
public sealed class RaycastHit
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public double Distance { get; set; }

    /// <summary>Hit point [X, Y, Z]</summary>
    [DataMember(Order = 4)]
    public List<double> HitPoint { get; set; } = new List<double>();

    [DataMember(Order = 5)]
    public bool IsLinkedElement { get; set; }

    [DataMember(Order = 6)]
    public string LinkName { get; set; } = string.Empty;
}

[DataContract]
public sealed class RaycastResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int HitCount { get; set; }

    [DataMember(Order = 3)]
    public List<RaycastHit> Hits { get; set; } = new List<RaycastHit>();

    [DataMember(Order = 4)]
    public double TimingMs { get; set; }
}

// ── B-4: Geometry Extraction ──

[DataContract]
public sealed class GeometryExtractRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public bool IncludeVolume { get; set; } = true;

    [DataMember(Order = 4)]
    public bool IncludeSurfaceArea { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeFaceCount { get; set; }

    [DataMember(Order = 6)]
    public bool IncludeCentroid { get; set; } = true;
}

[DataContract]
public sealed class GeometryInfo
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>BoundingBox min/max</summary>
    [DataMember(Order = 3)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    [DataMember(Order = 4)]
    public List<double> BBoxMax { get; set; } = new List<double>();

    /// <summary>Total solid volume in cubic feet</summary>
    [DataMember(Order = 5)]
    public double Volume { get; set; }

    /// <summary>Total surface area in square feet</summary>
    [DataMember(Order = 6)]
    public double SurfaceArea { get; set; }

    [DataMember(Order = 7)]
    public int FaceCount { get; set; }

    /// <summary>Centroid [X, Y, Z]</summary>
    [DataMember(Order = 8)]
    public List<double> Centroid { get; set; } = new List<double>();

    [DataMember(Order = 9)]
    public int SolidCount { get; set; }
}

[DataContract]
public sealed class GeometryExtractResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int RequestedCount { get; set; }

    [DataMember(Order = 3)]
    public int ResolvedCount { get; set; }

    [DataMember(Order = 4)]
    public List<GeometryInfo> Elements { get; set; } = new List<GeometryInfo>();

    [DataMember(Order = 5)]
    public double TimingMs { get; set; }
}

// ── B-5: Smart Section Box ──

[DataContract]
public sealed class SectionBoxFromElementsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    /// <summary>Padding around bounding box in feet</summary>
    [DataMember(Order = 3)]
    public double Padding { get; set; } = 3.0;

    /// <summary>If > 0, apply section box to this 3D view. Otherwise, just compute the box.</summary>
    [DataMember(Order = 4)]
    public int TargetView3DId { get; set; }
}

[DataContract]
public sealed class SectionBoxFromElementsResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    [DataMember(Order = 3)]
    public List<double> BBoxMax { get; set; } = new List<double>();

    [DataMember(Order = 4)]
    public int ElementsCovered { get; set; }

    [DataMember(Order = 5)]
    public bool AppliedToView { get; set; }

    [DataMember(Order = 6)]
    public double TimingMs { get; set; }
}

// ── B-6: Zone Summary ──

[DataContract]
public sealed class ZoneSummaryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>level | grid | custom_bbox</summary>
    [DataMember(Order = 2)]
    public string ZoneMode { get; set; } = "level";

    [DataMember(Order = 3)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public int MaxZones { get; set; } = 50;
}

[DataContract]
public sealed class ZoneInfo
{
    [DataMember(Order = 1)]
    public string ZoneName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ElementCount { get; set; }

    [DataMember(Order = 3)]
    public List<CategoryCountItem> CategoryBreakdown { get; set; } = new List<CategoryCountItem>();

    [DataMember(Order = 4)]
    public List<double> BBoxMin { get; set; } = new List<double>();

    [DataMember(Order = 5)]
    public List<double> BBoxMax { get; set; } = new List<double>();
}

[DataContract]
public sealed class ZoneSummaryResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ZoneCount { get; set; }

    [DataMember(Order = 3)]
    public int TotalElements { get; set; }

    [DataMember(Order = 4)]
    public List<ZoneInfo> Zones { get; set; } = new List<ZoneInfo>();

    [DataMember(Order = 5)]
    public double TimingMs { get; set; }
}

// ── B-7: Element Distance Matrix ──

[DataContract]
public sealed class ElementDistanceRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    /// <summary>centroid | bbox_min_distance</summary>
    [DataMember(Order = 3)]
    public string DistanceMode { get; set; } = "centroid";

    /// <summary>Max pairs to return (matrix can be huge)</summary>
    [DataMember(Order = 4)]
    public int MaxPairs { get; set; } = 200;
}

[DataContract]
public sealed class DistancePair
{
    [DataMember(Order = 1)]
    public int ElementIdA { get; set; }

    [DataMember(Order = 2)]
    public int ElementIdB { get; set; }

    [DataMember(Order = 3)]
    public double Distance { get; set; }
}

[DataContract]
public sealed class ElementDistanceResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int PairCount { get; set; }

    [DataMember(Order = 3)]
    public List<DistancePair> Pairs { get; set; } = new List<DistancePair>();

    [DataMember(Order = 4)]
    public double MinDistance { get; set; }

    [DataMember(Order = 5)]
    public double MaxDistance { get; set; }

    [DataMember(Order = 6)]
    public double AvgDistance { get; set; }

    [DataMember(Order = 7)]
    public double TimingMs { get; set; }
}

// ── B-8: Level Zone Analysis ──

[DataContract]
public sealed class LevelZoneAnalysisRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public bool IncludeBreakdown { get; set; } = true;
}

[DataContract]
public sealed class LevelZoneInfo
{
    [DataMember(Order = 1)]
    public string LevelName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public double Elevation { get; set; }

    [DataMember(Order = 3)]
    public int ElementCount { get; set; }

    [DataMember(Order = 4)]
    public List<CategoryCountItem> CategoryBreakdown { get; set; } = new List<CategoryCountItem>();
}

[DataContract]
public sealed class LevelZoneAnalysisResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int LevelCount { get; set; }

    [DataMember(Order = 3)]
    public int TotalElements { get; set; }

    [DataMember(Order = 4)]
    public List<LevelZoneInfo> Levels { get; set; } = new List<LevelZoneInfo>();

    [DataMember(Order = 5)]
    public double TimingMs { get; set; }
}

// ── B-9: Opening Detection ──

[DataContract]
public sealed class OpeningDetectRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> HostElementIds { get; set; } = new List<int>();

    /// <summary>Optional: filter by host category (Walls, Floors, Ceilings)</summary>
    [DataMember(Order = 3)]
    public List<string> HostCategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class OpeningInfo
{
    [DataMember(Order = 1)]
    public int HostElementId { get; set; }

    [DataMember(Order = 2)]
    public string HostCategory { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int InsertElementId { get; set; }

    /// <summary>door | window | opening | penetration | void | other</summary>
    [DataMember(Order = 4)]
    public string InsertType { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string InsertFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string InsertTypeName { get; set; } = string.Empty;
}

[DataContract]
public sealed class OpeningDetectResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int HostCount { get; set; }

    [DataMember(Order = 3)]
    public int OpeningCount { get; set; }

    [DataMember(Order = 4)]
    public List<OpeningInfo> Openings { get; set; } = new List<OpeningInfo>();

    [DataMember(Order = 5)]
    public double TimingMs { get; set; }
}

// ── B-10: DirectShape Create ──

[DataContract]
public sealed class DirectShapeCreateRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Shape type: box | sphere_approx | line_marker</summary>
    [DataMember(Order = 2)]
    public string ShapeType { get; set; } = "box";

    /// <summary>For box: min [X,Y,Z]</summary>
    [DataMember(Order = 3)]
    public List<double> Min { get; set; } = new List<double>();

    /// <summary>For box: max [X,Y,Z]. For sphere: center [X,Y,Z]</summary>
    [DataMember(Order = 4)]
    public List<double> Max { get; set; } = new List<double>();

    /// <summary>For sphere: radius in feet</summary>
    [DataMember(Order = 5)]
    public double Radius { get; set; } = 1.0;

    [DataMember(Order = 6)]
    public string Name { get; set; } = "BIM765T_Marker";

    /// <summary>Category for the DirectShape (default: GenericModel)</summary>
    [DataMember(Order = 7)]
    public string CategoryName { get; set; } = "Generic Models";
}

[DataContract]
public sealed class DirectShapeCreateResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int CreatedElementId { get; set; }

    [DataMember(Order = 3)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public double TimingMs { get; set; }
}
