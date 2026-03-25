using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Common;

[DataContract]
public enum DiagnosticSeverity
{
    [EnumMember] Info = 0,
    [EnumMember] Warning = 1,
    [EnumMember] Error = 2
}
