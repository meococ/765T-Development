using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class SheetCaptureIntelligenceRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetId { get; set; }

    [DataMember(Order = 3)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IncludeViewportDetails { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeScheduleData { get; set; }

    [DataMember(Order = 6)]
    public int MaxViewports { get; set; } = 20;

    [DataMember(Order = 7)]
    public int MaxSchedules { get; set; } = 10;

    [DataMember(Order = 8)]
    public int MaxSheetTextNotes { get; set; } = 50;

    [DataMember(Order = 9)]
    public int MaxViewportTextNotes { get; set; } = 20;

    [DataMember(Order = 10)]
    public bool WriteArtifacts { get; set; }
}

[DataContract]
public sealed class SheetTitleBlockParameterInfo
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Value { get; set; } = string.Empty;
}

[DataContract]
public sealed class SheetViewportIntelligence
{
    [DataMember(Order = 1)]
    public int ViewportId { get; set; }

    [DataMember(Order = 2)]
    public int ViewId { get; set; }

    [DataMember(Order = 3)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int Scale { get; set; }

    [DataMember(Order = 6)]
    public double CenterX { get; set; }

    [DataMember(Order = 7)]
    public double CenterY { get; set; }

    [DataMember(Order = 8)]
    public double Width { get; set; }

    [DataMember(Order = 9)]
    public double Height { get; set; }

    [DataMember(Order = 10)]
    public int VisibleElementCount { get; set; }

    [DataMember(Order = 11)]
    public int TextNoteCount { get; set; }

    [DataMember(Order = 12)]
    public int TagCount { get; set; }

    [DataMember(Order = 13)]
    public int DimensionCount { get; set; }

    [DataMember(Order = 14)]
    public List<string> TextPreview { get; set; } = new List<string>();
}

[DataContract]
public sealed class SheetScheduleIntelligence
{
    [DataMember(Order = 1)]
    public int ScheduleInstanceId { get; set; }

    [DataMember(Order = 2)]
    public int ScheduleViewId { get; set; }

    [DataMember(Order = 3)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int RowCount { get; set; }

    [DataMember(Order = 5)]
    public int ColumnCount { get; set; }

    [DataMember(Order = 6)]
    public double CenterX { get; set; }

    [DataMember(Order = 7)]
    public double CenterY { get; set; }

    [DataMember(Order = 8)]
    public double Width { get; set; }

    [DataMember(Order = 9)]
    public double Height { get; set; }

    [DataMember(Order = 10)]
    public List<string> Headers { get; set; } = new List<string>();
}

[DataContract]
public sealed class SheetTextNoteIntelligence
{
    [DataMember(Order = 1)]
    public int OwnerViewId { get; set; }

    [DataMember(Order = 2)]
    public string OwnerViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Text { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public double X { get; set; }

    [DataMember(Order = 5)]
    public double Y { get; set; }
}

[DataContract]
public sealed class SheetArtifactReference
{
    [DataMember(Order = 1)]
    public string ArtifactType { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Path { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;
}

[DataContract]
public sealed class SheetCaptureIntelligenceResponse
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
    public string TitleBlockName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string CurrentRevision { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<SheetTitleBlockParameterInfo> TitleBlockParameters { get; set; } = new List<SheetTitleBlockParameterInfo>();

    [DataMember(Order = 8)]
    public List<SheetViewportIntelligence> Viewports { get; set; } = new List<SheetViewportIntelligence>();

    [DataMember(Order = 9)]
    public List<SheetScheduleIntelligence> Schedules { get; set; } = new List<SheetScheduleIntelligence>();

    [DataMember(Order = 10)]
    public List<SheetTextNoteIntelligence> SheetTextNotes { get; set; } = new List<SheetTextNoteIntelligence>();

    [DataMember(Order = 11)]
    public List<CountByNameDto> AnnotationCounts { get; set; } = new List<CountByNameDto>();

    [DataMember(Order = 12)]
    public string LayoutMap { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public List<SheetArtifactReference> Artifacts { get; set; } = new List<SheetArtifactReference>();

    [DataMember(Order = 14)]
    public string Summary { get; set; } = string.Empty;
}
