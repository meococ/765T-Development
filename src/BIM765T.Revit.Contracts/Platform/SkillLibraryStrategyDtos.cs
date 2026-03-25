using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

public static class ToolPrimaryPersonas
{
    public const string ProductionBimer = "production_bimer";
    public const string QaManager = "qa_manager";
    public const string MepSpecialist = "mep_specialist";
    public const string PlatformAuthor = "platform_author";
}

public static class ToolUserValueClasses
{
    public const string DailyRoi = "daily_roi";
    public const string SmartValue = "smart_value";
    public const string TemplateGeneration = "template_generation";
    public const string Autopilot = "autopilot";
}

public static class ToolRepeatabilityClasses
{
    public const string OneOff = "one_off";
    public const string Repeatable = "repeatable";
    public const string Teachable = "teachable";
    public const string Watchable = "watchable";
}

public static class ToolAutomationStages
{
    public const string CoreSkill = "core_skill";
    public const string PlaybookReady = "playbook_ready";
    public const string ArtifactFallback = "artifact_fallback";
    public const string TemplateSynthesis = "template_synthesis";
    public const string ProactiveWatch = "proactive_watch";
}

public static class CommercialTiers
{
    public const string Free = "free";
    public const string PersonalPro = "personal_pro";
    public const string StudioAutopilot = "studio_autopilot";
    public const string Internal = "internal";
}

public static class CacheValueClasses
{
    public const string None = "none";
    public const string IntentToolchain = "intent_toolchain";
    public const string ArtifactReuse = "artifact_reuse";
    public const string TeachBack = "teach_back";
}

public static class FallbackArtifactKinds
{
    public const string Playbook = "playbook";
    public const string CsvMapping = "csv_mapping";
    public const string OpenXmlRecipe = "openxml_recipe";
    public const string ExportProfile = "export_profile";
    public const string DynamoTemplate = "dynamo_template";
    public const string ExternalWrapper = "external_wrapper";
}

public static class SourceImportModes
{
    public const string BehaviorOnly = "behavior_only";
    public const string WrapperOnly = "wrapper_only";
    public const string CodeReuseAllowed = "code_reuse_allowed";
}

public static class SourceLogicVerificationStatuses
{
    public const string Discovered = "discovered";
    public const string Reviewed = "reviewed";
    public const string Validated = "validated";
    public const string Shipped = "shipped";
}

public static class WatchTriggerKinds
{
    public const string ReopenSummary = "reopen_summary";
    public const string DeltaScan = "delta_scan";
    public const string DeliverableDrift = "deliverable_drift";
    public const string WarningDelta = "warning_delta";
}

[DataContract]
public sealed class FallbackArtifactRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Reason { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PrimaryPersona { get; set; } = ToolPrimaryPersonas.ProductionBimer;

    [DataMember(Order = 5)]
    public List<string> RequestedKinds { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> CandidateToolNames { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> CandidatePlaybookIds { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> ExistingArtifactRefs { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public string InputSummary { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string CommercialTier { get; set; } = CommercialTiers.PersonalPro;
}

[DataContract]
public sealed class FallbackArtifactProposal
{
    [DataMember(Order = 1)]
    public string ProposalId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Reason { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string PreviewSummary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string VerificationRecipe { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public ApprovalRequirement ApprovalRequirement { get; set; } = ApprovalRequirement.ConfirmToken;

    [DataMember(Order = 9)]
    public List<string> ArtifactKinds { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<string> ArtifactPaths { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public string CandidateBuiltInToolName { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string CandidatePlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public bool RequiresHumanReview { get; set; } = true;

    [DataMember(Order = 14)]
    public bool CanSaveToCache { get; set; } = true;

    [DataMember(Order = 15)]
    public List<string> SourceLogicIds { get; set; } = new List<string>();

    [DataMember(Order = 16)]
    public List<string> InputsUsed { get; set; } = new List<string>();

    [DataMember(Order = 17)]
    public string CommercialTier { get; set; } = CommercialTiers.PersonalPro;

    [DataMember(Order = 18)]
    public string CacheValueClass { get; set; } = CacheValueClasses.ArtifactReuse;
}

[DataContract]
public sealed class FallbackArtifactResult
{
    [DataMember(Order = 1)]
    public FallbackArtifactProposal Proposal { get; set; } = new FallbackArtifactProposal();

    [DataMember(Order = 2)]
    public bool Accepted { get; set; }

    [DataMember(Order = 3)]
    public bool SavedToCache { get; set; }

    [DataMember(Order = 4)]
    public List<string> ArtifactPaths { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class SkillCaptureProposal
{
    [DataMember(Order = 1)]
    public string CaptureId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string SourceRunId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string CandidateSkillId { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string PlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public string CacheValueClass { get; set; } = CacheValueClasses.TeachBack;

    [DataMember(Order = 9)]
    public bool CanPromoteToFreeReplay { get; set; }

    [DataMember(Order = 10)]
    public string CommercialTier { get; set; } = CommercialTiers.PersonalPro;

    [DataMember(Order = 11)]
    public double Confidence { get; set; }
}

[DataContract]
public sealed class ProjectPatternSnapshot
{
    [DataMember(Order = 1)]
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = CapabilityDisciplines.Common;

    [DataMember(Order = 4)]
    public List<string> RecommendedPlaybooks { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> RecommendedToolNames { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> ParameterMappingRefs { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> ExportProfileRefs { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public double Confidence { get; set; }

    [DataMember(Order = 10)]
    public string SourceWorkspaceId { get; set; } = string.Empty;
}

[DataContract]
public sealed class TemplateSynthesisProposal
{
    [DataMember(Order = 1)]
    public string ProposalId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SourceProjectWorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public double Confidence { get; set; }

    [DataMember(Order = 6)]
    public string ProposedWorkspacePackId { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> ProposedArtifactPaths { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public string VerificationRecipe { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public bool RequiresApproval { get; set; } = true;

    [DataMember(Order = 10)]
    public ProjectPatternSnapshot Snapshot { get; set; } = new ProjectPatternSnapshot();

    [DataMember(Order = 11)]
    public string CommercialTier { get; set; } = CommercialTiers.StudioAutopilot;
}

[DataContract]
public sealed class WatchRule
{
    [DataMember(Order = 1)]
    public string RuleId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TriggerKind { get; set; } = WatchTriggerKinds.DeltaScan;

    [DataMember(Order = 4)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 5)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> CandidateToolNames { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string QueryHint { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public bool NotifyOnly { get; set; } = true;

    [DataMember(Order = 9)]
    public bool Enabled { get; set; } = true;

    [DataMember(Order = 10)]
    public string CommercialTier { get; set; } = CommercialTiers.StudioAutopilot;
}

[DataContract]
public sealed class DeltaSuggestion
{
    [DataMember(Order = 1)]
    public string SuggestionId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Reason { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Stage { get; set; } = WorkerFlowStages.Thinking;

    [DataMember(Order = 6)]
    public List<string> CandidateToolNames { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string CandidatePlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 9)]
    public double Confidence { get; set; }

    [DataMember(Order = 10)]
    public string WatchRuleId { get; set; } = string.Empty;
}

[DataContract]
public sealed class SourceLogicManifest
{
    [DataMember(Order = 1)]
    public string SourceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SourceUrl { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SourceLabel { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string License { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ImportMode { get; set; } = SourceImportModes.BehaviorOnly;

    [DataMember(Order = 6)]
    public string TargetSkill { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string VerificationStatus { get; set; } = SourceLogicVerificationStatuses.Discovered;

    [DataMember(Order = 8)]
    public string Notes { get; set; } = string.Empty;
}
