using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── Playbook DTOs ──

[DataContract]
public sealed class PlaybookDefinition
{
    [DataMember(Order = 1)]
    public string PlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Version { get; set; } = "1.0";

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Lane { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string RequiredContext { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public PlaybookDecisionGate DecisionGate { get; set; } = new PlaybookDecisionGate();

    [DataMember(Order = 7)]
    public List<PlaybookStepDefinition> Steps { get; set; } = new List<PlaybookStepDefinition>();

    [DataMember(Order = 8)]
    public PlaybookQcStep QcStep { get; set; } = new PlaybookQcStep();

    [DataMember(Order = 9)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public List<string> TriggerPhrases { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public List<string> StandardsRefs { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> RequiredInputs { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public string ReportTemplate { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public List<string> RecommendedSpecialists { get; set; } = new List<string>();

    [DataMember(Order = 15)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 16)]
    public string DeterminismLevel { get; set; } = ToolDeterminismLevels.PolicyBacked;

    [DataMember(Order = 17)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 18)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 19)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 20)]
    public List<string> PolicyPackIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class PlaybookDecisionGate
{
    [DataMember(Name = "Use_When", Order = 1)]
    public List<string> UseWhen { get; set; } = new List<string>();

    [DataMember(Name = "Dont_Use_When", Order = 2)]
    public List<string> DontUseWhen { get; set; } = new List<string>();

    [DataMember(Name = "Prefer_Over", Order = 3)]
    public string PreferOver { get; set; } = string.Empty;
}

[DataContract]
public sealed class PlaybookStepDefinition
{
    [DataMember(Order = 1)]
    public string StepName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Tool { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Purpose { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Condition { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Verify { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string StepId { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string StepKind { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string ParametersJson { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string OutputKey { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string LoopOver { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public List<string> RequiredStandardsRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class PlaybookQcStep
{
    [DataMember(Order = 1)]
    public string Tool { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ExpectedOutcome { get; set; } = string.Empty;
}

[DataContract]
public sealed class PlaybookRecommendation
{
    [DataMember(Order = 1)]
    public string PlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public double Confidence { get; set; }

    [DataMember(Order = 4)]
    public List<PlaybookStepSummary> Steps { get; set; } = new List<PlaybookStepSummary>();

    [DataMember(Order = 5)]
    public string DontUseReason { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<string> AlternativePlaybooks { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> StandardsRefs { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public List<string> RequiredInputs { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<string> RecommendedSpecialists { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 12)]
    public string DeterminismLevel { get; set; } = ToolDeterminismLevels.PolicyBacked;

    [DataMember(Order = 13)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 14)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 15)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 16)]
    public List<string> PolicyPackIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class PlaybookStepSummary
{
    [DataMember(Order = 1)]
    public string StepName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Tool { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Purpose { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Condition { get; set; } = string.Empty;
}

// ── Knowledge Update DTOs ──

[DataContract]
public sealed class KnowledgeUpdateResult
{
    [DataMember(Order = 1)]
    public bool Updated { get; set; }

    [DataMember(Order = 2)]
    public string FilePath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SectionKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Reason { get; set; } = string.Empty;
}
