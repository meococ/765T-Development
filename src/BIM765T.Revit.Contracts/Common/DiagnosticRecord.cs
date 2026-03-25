using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Common;

[DataContract]
public sealed class DiagnosticRecord
{
    [DataMember(Order = 1)]
    public string Code { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;

    [DataMember(Order = 3)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 4, EmitDefaultValue = false)]
    public int? SourceId { get; set; }

    [DataMember(Order = 5, EmitDefaultValue = false)]
    public string? DetailsJson { get; set; }

    public static DiagnosticRecord Create(string code, DiagnosticSeverity severity, string message, int? sourceId = null, string? detailsJson = null)
    {
        return new DiagnosticRecord
        {
            Code = code,
            Severity = severity,
            Message = message,
            SourceId = sourceId,
            DetailsJson = detailsJson
        };
    }
}
