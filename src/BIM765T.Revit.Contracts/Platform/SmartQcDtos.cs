using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class SmartQcRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RulesetName { get; set; } = "base-rules";

    [DataMember(Order = 3)]
    public string NamingPattern { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<int> SheetIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public List<string> SheetNumbers { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> RequiredParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public int MaxFindings { get; set; } = 100;

    [DataMember(Order = 8)]
    public int MaxSheets { get; set; } = 20;

    [DataMember(Order = 9)]
    public int MaxNamingViolations { get; set; } = 25;

    [DataMember(Order = 10)]
    public double DuplicateToleranceMm { get; set; } = 1.0;
}

[DataContract]
public sealed class SmartQcFinding
{
    [DataMember(Order = 1)]
    public string RuleId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Warning;

    [DataMember(Order = 5)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SourceTool { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string EvidenceRef { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string SuggestedAction { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string StandardRef { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public int? ElementId { get; set; }

    [DataMember(Order = 11)]
    public int? SheetId { get; set; }
}

[DataContract]
public sealed class SmartQcResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RulesetName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RulesetDescription { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RulesetPath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int ExecutedCheckCount { get; set; }

    [DataMember(Order = 6)]
    public int FindingCount { get; set; }

    [DataMember(Order = 7)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> ExecutedChecks { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public List<string> RulesEvaluated { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<SmartQcFinding> Findings { get; set; } = new List<SmartQcFinding>();
}
