using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── View Set Crop Region ──────────────────────────────────────────────────────

/// <summary>Request to set the crop region box on a view.</summary>
[DataContract]
public sealed class ViewSetCropRegionRequest
{
    /// <summary>Document key identifying the target document.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>ElementId of the view to modify.</summary>
    [DataMember(Order = 2)]
    public int ViewId { get; set; }

    /// <summary>Minimum X coordinate of the crop rectangle in Revit internal units (feet).</summary>
    [DataMember(Order = 3)]
    public double MinX { get; set; }

    /// <summary>Minimum Y coordinate of the crop rectangle in Revit internal units (feet).</summary>
    [DataMember(Order = 4)]
    public double MinY { get; set; }

    /// <summary>Maximum X coordinate of the crop rectangle in Revit internal units (feet).</summary>
    [DataMember(Order = 5)]
    public double MaxX { get; set; }

    /// <summary>Maximum Y coordinate of the crop rectangle in Revit internal units (feet).</summary>
    [DataMember(Order = 6)]
    public double MaxY { get; set; }

    /// <summary>Whether to enable crop view after applying the region. Defaults to true.</summary>
    [DataMember(Order = 7)]
    public bool EnableCrop { get; set; } = true;
}

/// <summary>Result of a set crop region operation.</summary>
[DataContract]
public sealed class ViewSetCropRegionResponse
{
    /// <summary>ElementId of the view that was modified.</summary>
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    /// <summary>True when the crop region was successfully applied.</summary>
    [DataMember(Order = 2)]
    public bool Applied { get; set; }

    /// <summary>The crop-enabled state of the view before this operation was applied.</summary>
    [DataMember(Order = 3)]
    public bool PreviousCropEnabled { get; set; }
}

// ── View Set View Range ───────────────────────────────────────────────────────

/// <summary>Request to set the view range (cut plane, top, bottom, view depth) on a plan view.</summary>
[DataContract]
public sealed class ViewSetViewRangeRequest
{
    /// <summary>Document key identifying the target document.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>ElementId of the plan view to modify.</summary>
    [DataMember(Order = 2)]
    public int ViewId { get; set; }

    /// <summary>
    /// Offset of the cut plane from the associated level in Revit internal units (feet).
    /// Typical value: 4 ft (1.2 m).
    /// </summary>
    [DataMember(Order = 3)]
    public double CutPlaneOffset { get; set; }

    /// <summary>Offset of the top clip plane from the associated level in Revit internal units (feet).</summary>
    [DataMember(Order = 4)]
    public double TopOffset { get; set; }

    /// <summary>Offset of the bottom clip plane from the associated level in Revit internal units (feet).</summary>
    [DataMember(Order = 5)]
    public double BottomOffset { get; set; }

    /// <summary>Offset of the view depth plane from the associated level in Revit internal units (feet). Must be &lt;= BottomOffset.</summary>
    [DataMember(Order = 6)]
    public double ViewDepthOffset { get; set; }
}

/// <summary>Result of a set view range operation.</summary>
[DataContract]
public sealed class ViewSetViewRangeResponse
{
    /// <summary>ElementId of the view that was modified.</summary>
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    /// <summary>True when the view range was successfully applied.</summary>
    [DataMember(Order = 2)]
    public bool Applied { get; set; }
}
