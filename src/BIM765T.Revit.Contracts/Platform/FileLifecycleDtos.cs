using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class SaveDocumentRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;
}

[DataContract]
public sealed class OpenBackgroundDocumentRequest
{
    [DataMember(Order = 1)]
    public string FilePath { get; set; } = string.Empty;
}

[DataContract]
public sealed class CloseDocumentRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool SaveModified { get; set; }
}

[DataContract]
public sealed class SaveAsDocumentRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool OverwriteExisting { get; set; }
}

[DataContract]
public sealed class SynchronizeRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Comment { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool RelinquishAllAfterSync { get; set; }

    [DataMember(Order = 4)]
    public bool CompactCentral { get; set; }
}
