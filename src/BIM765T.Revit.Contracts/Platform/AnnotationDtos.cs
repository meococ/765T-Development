using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class AddTextNoteRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? ViewId { get; set; }

    [DataMember(Order = 3)]
    public int? TextNoteTypeId { get; set; }

    [DataMember(Order = 4)]
    public string Text { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public double X { get; set; }

    [DataMember(Order = 6)]
    public double Y { get; set; }

    [DataMember(Order = 7)]
    public double Z { get; set; }

    [DataMember(Order = 8)]
    public bool UseViewCenterWhenPossible { get; set; } = true;
}

[DataContract]
public sealed class UpdateTextNoteStyleRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TextNoteId { get; set; }

    [DataMember(Order = 3)]
    public string TargetTypeName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TextSizeValue { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int? Red { get; set; }

    [DataMember(Order = 6)]
    public int? Green { get; set; }

    [DataMember(Order = 7)]
    public int? Blue { get; set; }

    [DataMember(Order = 8)]
    public bool DuplicateCurrentTypeIfNeeded { get; set; } = true;

    [DataMember(Order = 9)]
    public bool ReuseMatchingExistingType { get; set; } = true;
}

[DataContract]
public sealed class UpdateTextNoteContentRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TextNoteId { get; set; }

    [DataMember(Order = 3)]
    public string NewText { get; set; } = string.Empty;
}
