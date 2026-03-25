using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class ScopeDescriptor
{
    [DataMember(Order = 1)]
    public string ScopeKind { get; set; } = "active";

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 200;
}

[DataContract]
public sealed class ContextFingerprint
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int SelectionCount { get; set; }

    [DataMember(Order = 4)]
    public string SelectionHash { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<int> SelectedElementIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public long ActiveDocEpoch { get; set; }
}

[DataContract]
public sealed class DocumentSummaryDto
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PathName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsActive { get; set; }

    [DataMember(Order = 5)]
    public bool IsModified { get; set; }

    [DataMember(Order = 6)]
    public bool IsWorkshared { get; set; }

    [DataMember(Order = 7)]
    public bool IsLinked { get; set; }

    [DataMember(Order = 8)]
    public bool IsFamilyDocument { get; set; }

    [DataMember(Order = 9)]
    public bool CanSave { get; set; }

    [DataMember(Order = 10)]
    public bool CanSynchronize { get; set; }
}

[DataContract]
public sealed class DocumentListResponse
{
    [DataMember(Order = 1)]
    public List<DocumentSummaryDto> Documents { get; set; } = new List<DocumentSummaryDto>();
}

[DataContract]
public sealed class ViewSummaryDto
{
    [DataMember(Order = 1)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ViewId { get; set; }

    [DataMember(Order = 3)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string LevelName { get; set; } = string.Empty;

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public int? LevelId { get; set; }

    [DataMember(Order = 8)]
    public bool IsTemplate { get; set; }
}

[DataContract]
public sealed class SelectionSummaryDto
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 4)]
    public int Count { get; set; }
}
