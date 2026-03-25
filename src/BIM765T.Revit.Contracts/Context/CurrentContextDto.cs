using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Context;

[DataContract]
public sealed class CurrentContextDto
{
    [DataMember(Order = 1)]
    public string DocumentName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4, EmitDefaultValue = false)]
    public string? LevelName { get; set; }

    [DataMember(Order = 5, EmitDefaultValue = false)]
    public int? LevelId { get; set; }

    [DataMember(Order = 6, EmitDefaultValue = false)]
    public double? LevelElevation { get; set; }

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public double? CameraZ { get; set; }

    [DataMember(Order = 8)]
    public string LevelMode { get; set; } = "UNKNOWN";

    [DataMember(Order = 9)]
    public string Confidence { get; set; } = "LOW";

    [DataMember(Order = 10)]
    public List<int> SelectedElementIds { get; set; } = new List<int>();

    [DataMember(Order = 11)]
    public List<string> Notes { get; set; } = new List<string>();
}
