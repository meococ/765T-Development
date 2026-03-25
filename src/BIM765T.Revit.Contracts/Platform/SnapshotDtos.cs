using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class SnapshotElementState
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
}

[DataContract]
public sealed class ModelSnapshotSummary
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<SnapshotElementState> Elements { get; set; } = new List<SnapshotElementState>();

    [DataMember(Order = 3)]
    public int WarningCount { get; set; }
}

[DataContract]
public sealed class CaptureSnapshotRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Scope { get; set; } = "active_view";

    [DataMember(Order = 3)]
    public int? ViewId { get; set; }

    [DataMember(Order = 4)]
    public int? SheetId { get; set; }

    [DataMember(Order = 5)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SheetName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 8)]
    public bool IncludeParameters { get; set; }

    [DataMember(Order = 9)]
    public List<string> ParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public int MaxElements { get; set; } = 100;

    [DataMember(Order = 11)]
    public bool ExportImage { get; set; }

    [DataMember(Order = 12)]
    public string ImageOutputPath { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public int ImagePixelSize { get; set; } = 2048;
}

[DataContract]
public sealed class SnapshotCaptureResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Scope { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SummaryLabel { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int ElementCount { get; set; }

    [DataMember(Order = 6)]
    public ModelSnapshotSummary Snapshot { get; set; } = new ModelSnapshotSummary();

    [DataMember(Order = 7)]
    public List<ElementSummaryDto> Elements { get; set; } = new List<ElementSummaryDto>();

    [DataMember(Order = 8)]
    public ReviewReport Review { get; set; } = new ReviewReport();

    [DataMember(Order = 9)]
    public List<string> ArtifactPaths { get; set; } = new List<string>();
}
