using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public enum PermissionLevel
{
    [EnumMember] Read = 0,
    [EnumMember] Review = 1,
    [EnumMember] Mutate = 2,
    [EnumMember] FileLifecycle = 3,
    [EnumMember] Admin = 4
}

[DataContract]
public enum ApprovalRequirement
{
    [EnumMember] None = 0,
    [EnumMember] ConfirmToken = 1,
    [EnumMember] HighRiskToken = 2
}

public static class ToolRiskTiers
{
    public const string Tier0 = "tier0_read";
    public const string Tier1 = "tier1_mutate_low_risk";
    public const string Tier2 = "tier2_destructive";
}

public static class ToolLatencyClasses
{
    public const string Interactive = "interactive";
    public const string Standard = "standard";
    public const string LongRunning = "long_running";
    public const string Batch = "batch";
}

public static class ToolUiSurfaces
{
    public const string WorkerHome = "worker_home";
    public const string Queue = "queue_progress";
    public const string Approvals = "actions_approvals";
    public const string Evidence = "evidence";
    public const string ExpertLab = "expert_lab";
}

public static class ToolProgressModes
{
    public const string None = "none";
    public const string StageOnly = "stage_only";
    public const string Heartbeat = "heartbeat";
    public const string Determinate = "determinate";
}

public static class ToolQueuePriorities
{
    public const string High = "high";
    public const string Normal = "normal";
    public const string Low = "low";
}

[DataContract]
public sealed class ToolManifest
{
    [DataMember(Order = 1)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public PermissionLevel PermissionLevel { get; set; } = PermissionLevel.Read;

    [DataMember(Order = 4)]
    public ApprovalRequirement ApprovalRequirement { get; set; } = ApprovalRequirement.None;

    [DataMember(Order = 5)]
    public bool SupportsDryRun { get; set; }

    [DataMember(Order = 6)]
    public bool Enabled { get; set; } = true;

    [DataMember(Order = 7)]
    public string InputSchemaHint { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> RequiredContext { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public bool MutatesModel { get; set; }

    [DataMember(Order = 10)]
    public bool TouchesActiveView { get; set; }

    [DataMember(Order = 11)]
    public bool RequiresExpectedContext { get; set; }

    [DataMember(Order = 12)]
    public string BatchMode { get; set; } = "none";

    [DataMember(Order = 13)]
    public string Idempotency { get; set; } = "unknown";

    [DataMember(Order = 14)]
    public List<string> PreviewArtifacts { get; set; } = new List<string>();

    [DataMember(Order = 15)]
    public List<string> RiskTags { get; set; } = new List<string>();

    [DataMember(Order = 16)]
    public List<string> RulePackTags { get; set; } = new List<string>();

    [DataMember(Order = 17)]
    public string InputSchemaJson { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public int ExecutionTimeoutMs { get; set; }

    [DataMember(Order = 19)]
    public string CapabilityPack { get; set; } = WorkerCapabilityPacks.CoreWorker;

    [DataMember(Order = 20)]
    public string SkillGroup { get; set; } = WorkerSkillGroups.RevitOps;

    [DataMember(Order = 21)]
    public string Audience { get; set; } = WorkerAudience.Commercial;

    [DataMember(Order = 22)]
    public string Visibility { get; set; } = WorkerVisibility.Visible;

    [DataMember(Order = 23)]
    public string RiskTier { get; set; } = ToolRiskTiers.Tier0;

    [DataMember(Order = 24)]
    public bool CanAutoExecute { get; set; }

    [DataMember(Order = 25)]
    public string LatencyClass { get; set; } = ToolLatencyClasses.Standard;

    [DataMember(Order = 26)]
    public string UiSurface { get; set; } = ToolUiSurfaces.ExpertLab;

    [DataMember(Order = 27)]
    public string ProgressMode { get; set; } = ToolProgressModes.None;

    [DataMember(Order = 28)]
    public List<string> RecommendedNextTools { get; set; } = new List<string>();

    [DataMember(Order = 29)]
    public string DomainGroup { get; set; } = string.Empty;

    [DataMember(Order = 30)]
    public string TaskFamily { get; set; } = string.Empty;

    [DataMember(Order = 31)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 32)]
    public List<string> RecommendedPlaybooks { get; set; } = new List<string>();

    [DataMember(Order = 33)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 34)]
    public string DeterminismLevel { get; set; } = ToolDeterminismLevels.Deterministic;

    [DataMember(Order = 35)]
    public bool RequiresPolicyPack { get; set; }

    [DataMember(Order = 36)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 37)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 38)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 39)]
    public string CommandFamily { get; set; } = string.Empty;

    [DataMember(Order = 40)]
    public string ExecutionMode { get; set; } = CommandExecutionModes.Tool;

    [DataMember(Order = 41)]
    public string NativeCommandId { get; set; } = string.Empty;

    [DataMember(Order = 42)]
    public string SourceKind { get; set; } = CommandSourceKinds.Repo;

    [DataMember(Order = 43)]
    public string SourceRef { get; set; } = string.Empty;

    [DataMember(Order = 44)]
    public string SafetyClass { get; set; } = CommandSafetyClasses.ReadOnly;

    [DataMember(Order = 45)]
    public bool CanPreview { get; set; }

    [DataMember(Order = 46)]
    public string CoverageTier { get; set; } = CommandCoverageTiers.Baseline;

    [DataMember(Order = 47)]
    public List<string> FallbackEntryIds { get; set; } = new List<string>();

    [DataMember(Order = 48)]
    public string PrimaryPersona { get; set; } = ToolPrimaryPersonas.ProductionBimer;

    [DataMember(Order = 49)]
    public string UserValueClass { get; set; } = ToolUserValueClasses.DailyRoi;

    [DataMember(Order = 50)]
    public string RepeatabilityClass { get; set; } = ToolRepeatabilityClasses.Repeatable;

    [DataMember(Order = 51)]
    public string AutomationStage { get; set; } = ToolAutomationStages.CoreSkill;

    [DataMember(Order = 52)]
    public bool CanTeachBack { get; set; }

    [DataMember(Order = 53)]
    public List<string> FallbackArtifactKinds { get; set; } = new List<string>();

    [DataMember(Order = 54)]
    public string CommercialTier { get; set; } = CommercialTiers.Free;

    [DataMember(Order = 55)]
    public string CacheValueClass { get; set; } = CacheValueClasses.IntentToolchain;
}

[DataContract]
public sealed class ToolCatalogResponse
{
    [DataMember(Order = 1)]
    public List<ToolManifest> Tools { get; set; } = new List<ToolManifest>();
}

[DataContract]
public sealed class BridgeCapabilities
{
    [DataMember(Order = 1)]
    public string PlatformName { get; set; } = "765T Revit Bridge";

    [DataMember(Order = 2)]
    public string RevitYear { get; set; } = "2024";

    [DataMember(Order = 3)]
    public bool SupportsDryRun { get; set; } = true;

    [DataMember(Order = 4)]
    public bool SupportsApprovalTokens { get; set; } = true;

    [DataMember(Order = 5)]
    public bool SupportsBackgroundRead { get; set; }

    [DataMember(Order = 6)]
    public bool AllowWriteTools { get; set; }

    [DataMember(Order = 7)]
    public bool AllowSaveTools { get; set; }

    [DataMember(Order = 8)]
    public bool AllowSyncTools { get; set; }

    [DataMember(Order = 9)]
    public List<ToolManifest> Tools { get; set; } = new List<ToolManifest>();

    [DataMember(Order = 10)]
    public bool SupportsMcpHost { get; set; } = true;

    [DataMember(Order = 11)]
    public bool SupportsWorkflowRuntime { get; set; } = true;

    [DataMember(Order = 12)]
    public bool SupportsInspectorLane { get; set; } = true;

    [DataMember(Order = 13)]
    public string BridgeProtocolVersion { get; set; } = BIM765T.Revit.Contracts.Common.BridgeProtocol.PipeV1;

    [DataMember(Order = 14)]
    public string McpProtocolVersion { get; set; } = BIM765T.Revit.Contracts.Common.BridgeConstants.McpDefaultProtocolVersion;

    [DataMember(Order = 15)]
    public List<string> EnabledCapabilityPacks { get; set; } = new List<string>();

    [DataMember(Order = 16)]
    public WorkerProfile DefaultWorkerProfile { get; set; } = new WorkerProfile();

    [DataMember(Order = 17)]
    public string VisibleShellMode { get; set; } = WorkerShellModes.Worker;
}
