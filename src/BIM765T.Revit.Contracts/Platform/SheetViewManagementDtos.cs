using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── Sheet & View Management DTOs ──

[DataContract]
public sealed class SheetListRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SheetNumberContains { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SheetNameContains { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IncludeViewports { get; set; }

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class SheetItem
{
    [DataMember(Order = 1)]
    public int Id { get; set; }

    [DataMember(Order = 2)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SheetName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string IssuedBy { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string IssuedTo { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<ViewportItem> Viewports { get; set; } = new List<ViewportItem>();

    /// <summary>Always populated — number of viewports, even when IncludeViewports is false.</summary>
    [DataMember(Order = 7)]
    public int ViewportCount { get; set; }

    /// <summary>Title block family name on this sheet, empty if none.</summary>
    [DataMember(Order = 8)]
    public string TitleBlockName { get; set; } = string.Empty;

    /// <summary>Current revision value from built-in parameter, empty if not set.</summary>
    [DataMember(Order = 9)]
    public string CurrentRevision { get; set; } = string.Empty;
}

[DataContract]
public sealed class ViewportItem
{
    [DataMember(Order = 1)]
    public int ViewportId { get; set; }

    [DataMember(Order = 2)]
    public int ViewId { get; set; }

    [DataMember(Order = 3)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public double CenterX { get; set; }

    [DataMember(Order = 5)]
    public double CenterY { get; set; }
}

[DataContract]
public sealed class SheetListResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public List<SheetItem> Sheets { get; set; } = new List<SheetItem>();
}

[DataContract]
public sealed class ViewportLayoutRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetId { get; set; }

    [DataMember(Order = 3)]
    public string SheetNumber { get; set; } = string.Empty;
}

[DataContract]
public sealed class ViewportLayoutResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetId { get; set; }

    [DataMember(Order = 3)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<ViewportItem> Viewports { get; set; } = new List<ViewportItem>();
}

[DataContract]
public sealed class CreateSheetRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SheetName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? TitleBlockTypeId { get; set; }

    [DataMember(Order = 5)]
    public string TitleBlockTypeName { get; set; } = string.Empty;
}

[DataContract]
public sealed class RenumberSheetRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetId { get; set; }

    [DataMember(Order = 3)]
    public string OldSheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string NewSheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string NewSheetName { get; set; } = string.Empty;
}

[DataContract]
public sealed class PlaceViewsOnSheetRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetId { get; set; }

    [DataMember(Order = 3)]
    public List<ViewPlacementItem> Views { get; set; } = new List<ViewPlacementItem>();
}

[DataContract]
public sealed class ViewPlacementItem
{
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    [DataMember(Order = 2)]
    public double CenterX { get; set; }

    [DataMember(Order = 3)]
    public double CenterY { get; set; }
}

[DataContract]
public sealed class DuplicateViewRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ViewId { get; set; }

    [DataMember(Order = 3)]
    public string NewName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string DuplicateMode { get; set; } = "Duplicate";

    [DataMember(Order = 5)]
    public bool ActivateAfterCreate { get; set; }
}

[DataContract]
public sealed class CreateProjectViewRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewKind { get; set; } = "floor_plan";

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4, EmitDefaultValue = false)]
    public int? LevelId { get; set; }

    [DataMember(Order = 5)]
    public string LevelName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int? TemplateId { get; set; }

    [DataMember(Order = 8)]
    public string TemplateName { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public int? ScaleValue { get; set; }

    [DataMember(Order = 10)]
    public string ScaleText { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public bool ActivateAfterCreate { get; set; }
}

[DataContract]
public sealed class SetViewTemplateRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ViewId { get; set; }

    [DataMember(Order = 3)]
    public int? TemplateId { get; set; }

    [DataMember(Order = 4)]
    public string TemplateName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool RemoveTemplate { get; set; }
}

[DataContract]
public sealed class ViewTemplateItem
{
    [DataMember(Order = 1)]
    public int Id { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int UsageCount { get; set; }

    /// <summary>Architectural, Structural, Mechanical, Electrical, Coordination, or empty if Undefined.</summary>
    [DataMember(Order = 5)]
    public string Discipline { get; set; } = string.Empty;

    /// <summary>Number of view filters applied to this template.</summary>
    [DataMember(Order = 6)]
    public int FilterCount { get; set; }

    /// <summary>Number of view parameters controlled (locked) by this template.</summary>
    [DataMember(Order = 7)]
    public int ControlledParameterCount { get; set; }

    /// <summary>Coarse, Medium, or Fine.</summary>
    [DataMember(Order = 8)]
    public string DetailLevel { get; set; } = string.Empty;
}

[DataContract]
public sealed class ViewTemplateListResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public List<ViewTemplateItem> Templates { get; set; } = new List<ViewTemplateItem>();
}

[DataContract]
public sealed class ViewTemplateListRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string NameContains { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class AlignViewportsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetId { get; set; }

    [DataMember(Order = 3)]
    public string AlignMode { get; set; } = "CenterVertical";

    [DataMember(Order = 4)]
    public List<int> ViewportIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public double? TargetX { get; set; }

    [DataMember(Order = 6)]
    public double? TargetY { get; set; }
}
