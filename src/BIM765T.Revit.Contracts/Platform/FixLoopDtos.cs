using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class FixLoopPlanRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ScenarioName { get; set; } = "parameter_hygiene";

    [DataMember(Order = 3)]
    public string PlaybookName { get; set; } = "default.fix_loop_v1";

    [DataMember(Order = 4)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> RequiredParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public bool UseCurrentSelectionWhenEmpty { get; set; } = true;

    [DataMember(Order = 8)]
    public int? ViewId { get; set; }

    [DataMember(Order = 9)]
    public int? SheetId { get; set; }

    [DataMember(Order = 10)]
    public int MaxIssues { get; set; } = 200;

    [DataMember(Order = 11)]
    public int MaxActions { get; set; } = 25;

    [DataMember(Order = 12)]
    public string ImportFilePath { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string MatchParameterName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FixLoopApplyRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<string> ActionIds { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public bool AllowMutations { get; set; } = true;
}

[DataContract]
public sealed class FixLoopVerifyRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int MaxResidualIssues { get; set; } = 200;
}

[DataContract]
public sealed class FixLoopIssue
{
    [DataMember(Order = 1)]
    public string IssueId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string IssueClass { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Code { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;

    [DataMember(Order = 5)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int? ElementId { get; set; }

    [DataMember(Order = 7)]
    public double Confidence { get; set; } = 1.0;

    [DataMember(Order = 8)]
    public string Fixability { get; set; } = "review";

    [DataMember(Order = 9)]
    public string RuleId { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string SuggestedAction { get; set; } = string.Empty;
}

[DataContract]
public sealed class FixLoopVerificationCriteria
{
    [DataMember(Order = 1)]
    public string ReviewToolName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int ExpectedIssueDelta { get; set; }

    [DataMember(Order = 4)]
    public int ExpectedRemainingMax { get; set; }

    [DataMember(Order = 5)]
    public string VerificationPayloadJson { get; set; } = string.Empty;
}

[DataContract]
public sealed class FixLoopCandidateAction
{
    [DataMember(Order = 1)]
    public string ActionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RiskLevel { get; set; } = "medium";

    [DataMember(Order = 5)]
    public bool RequiresApproval { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IsExecutable { get; set; } = true;

    [DataMember(Order = 7)]
    public string BlockedReason { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string PayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 10)]
    public FixLoopVerificationCriteria Verification { get; set; } = new FixLoopVerificationCriteria();

    [DataMember(Order = 11)]
    public string DecisionReason { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public int Priority { get; set; }

    [DataMember(Order = 13)]
    public bool IsRecommended { get; set; } = true;
}

[DataContract]
public sealed class FixLoopVerificationResult
{
    [DataMember(Order = 1)]
    public string Status { get; set; } = "pending";

    [DataMember(Order = 2)]
    public string ReviewToolName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public DateTime? VerifiedUtc { get; set; }

    [DataMember(Order = 4)]
    public int ExpectedIssueCount { get; set; }

    [DataMember(Order = 5)]
    public int ActualIssueCount { get; set; }

    [DataMember(Order = 6)]
    public int ExpectedIssueDelta { get; set; }

    [DataMember(Order = 7)]
    public int ActualIssueDelta { get; set; }

    [DataMember(Order = 8)]
    public List<FixLoopIssue> ResidualIssues { get; set; } = new List<FixLoopIssue>();

    [DataMember(Order = 9)]
    public List<string> BlockedReasons { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
}

[DataContract]
public sealed class FixLoopEvidenceBundle
{
    [DataMember(Order = 1)]
    public string PlanSummary { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int IssueCount { get; set; }

    [DataMember(Order = 3)]
    public int ProposedActionCount { get; set; }

    [DataMember(Order = 4)]
    public int AppliedActionCount { get; set; }

    [DataMember(Order = 5)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> ResultPayloads { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> RecommendedActionIds { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> SelectedActionIds { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public int ExpectedIssueDelta { get; set; }

    [DataMember(Order = 10)]
    public int ActualIssueDelta { get; set; }

    [DataMember(Order = 11)]
    public string VerificationStatus { get; set; } = string.Empty;
}

[DataContract]
public sealed class FixLoopRun
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ScenarioName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PlaybookName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PlaybookSource { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Status { get; set; } = "planned";

    [DataMember(Order = 6)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string InputJson { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string ExpectedContextJson { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public List<FixLoopIssue> Issues { get; set; } = new List<FixLoopIssue>();

    [DataMember(Order = 10)]
    public List<FixLoopCandidateAction> CandidateActions { get; set; } = new List<FixLoopCandidateAction>();

    [DataMember(Order = 11)]
    public List<string> AppliedActionIds { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> BlockedReasons { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public FixLoopVerificationResult Verification { get; set; } = new FixLoopVerificationResult();

    [DataMember(Order = 14)]
    public FixLoopEvidenceBundle Evidence { get; set; } = new FixLoopEvidenceBundle();

    [DataMember(Order = 15)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();

    [DataMember(Order = 16)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 17)]
    public DateTime PlannedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 18)]
    public DateTime? AppliedUtc { get; set; }

    [DataMember(Order = 19)]
    public DateTime? VerifiedUtc { get; set; }

    [DataMember(Order = 20)]
    public List<string> RecommendedActionIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class FixCandidateReviewResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ScenarioName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PlaybookName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PlaybookSource { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<FixLoopIssue> Issues { get; set; } = new List<FixLoopIssue>();

    [DataMember(Order = 6)]
    public List<FixLoopCandidateAction> CandidateActions { get; set; } = new List<FixLoopCandidateAction>();

    [DataMember(Order = 7)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();

    [DataMember(Order = 8)]
    public List<string> RecommendedActionIds { get; set; } = new List<string>();
}
