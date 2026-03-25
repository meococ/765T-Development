using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

public static class CommandExecutionModes
{
    public const string Native = "native";
    public const string Tool = "tool";
    public const string Script = "script";
    public const string Workflow = "workflow";
}

public static class CommandSourceKinds
{
    public const string Revit = "revit";
    public const string Repo = "repo";
    public const string Internal = "internal";
    public const string PyRevit = "pyrevit";
    public const string DynamoOrchid = "dynamo_orchid";
    public const string ApprovedVendor = "approved_vendor";
}

public static class CommandSafetyClasses
{
    public const string ReadOnly = "read_only";
    public const string HarmlessUi = "harmless_ui";
    public const string PreviewedMutation = "previewed_mutation";
    public const string HighRiskMutation = "high_risk_mutation";
}

public static class CommandCoverageTiers
{
    public const string Baseline = "baseline";
    public const string Extended = "extended";
    public const string Experimental = "experimental";
}

public static class CommandCoverageStatuses
{
    public const string Mapped = "mapped";
    public const string Executable = "executable";
    public const string Previewable = "previewable";
    public const string Verified = "verified";
}

public static class MemoryNamespaces
{
    public const string AtlasNativeCommands = "atlas-native-commands";
    public const string AtlasCustomTools = "atlas-custom-tools";
    public const string AtlasCuratedScripts = "atlas-curated-scripts";
    public const string PlaybooksPolicies = "playbooks-policies";
    public const string ProjectRuntimeMemory = "project-runtime-memory";
    public const string EvidenceLessons = "evidence-lessons";
}

public static class RetrievalScopes
{
    public const string QuickPath = "quick_path";
    public const string WorkflowPath = "workflow_path";
    public const string DeliveryPath = "delivery_path";
}

[DataContract]
public sealed class CommandContextRequirements
{
    [DataMember(Order = 1)]
    public bool RequiresDocument { get; set; } = true;

    [DataMember(Order = 2)]
    public bool RequiresActiveView { get; set; }

    [DataMember(Order = 3)]
    public bool RequiresCurrentLevel { get; set; }

    [DataMember(Order = 4)]
    public bool RequiresCurrentSheet { get; set; }

    [DataMember(Order = 5)]
    public bool RequiresSelection { get; set; }

    [DataMember(Order = 6)]
    public List<string> RequiredCategories { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> RequiredDocumentKinds { get; set; } = new List<string>();
}

[DataContract]
public sealed class CommandAtlasEntry
{
    [DataMember(Order = 1)]
    public string CommandId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> Aliases { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string CommandFamily { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ExecutionMode { get; set; } = CommandExecutionModes.Tool;

    [DataMember(Order = 7)]
    public string NativeCommandId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string SourceKind { get; set; } = CommandSourceKinds.Repo;

    [DataMember(Order = 9)]
    public string SourceRef { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 11)]
    public string Discipline { get; set; } = CapabilityDisciplines.Common;

    [DataMember(Order = 12)]
    public string SafetyClass { get; set; } = CommandSafetyClasses.ReadOnly;

    [DataMember(Order = 13)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 14)]
    public CommandContextRequirements RequiredContext { get; set; } = new CommandContextRequirements();

    [DataMember(Order = 15)]
    public bool CanPreview { get; set; }

    [DataMember(Order = 16)]
    public bool NeedsApproval { get; set; }

    [DataMember(Order = 17)]
    public bool CanAutoExecute { get; set; }

    [DataMember(Order = 18)]
    public List<string> FallbackEntryIds { get; set; } = new List<string>();

    [DataMember(Order = 19)]
    public string CoverageTier { get; set; } = CommandCoverageTiers.Baseline;

    [DataMember(Order = 20)]
    public string CoverageStatus { get; set; } = CommandCoverageStatuses.Mapped;

    [DataMember(Order = 21)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 22)]
    public string DefaultPayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 23)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 24)]
    public List<string> RecommendedPlaybooks { get; set; } = new List<string>();

    [DataMember(Order = 25)]
    public string PrimaryPersona { get; set; } = ToolPrimaryPersonas.ProductionBimer;

    [DataMember(Order = 26)]
    public string UserValueClass { get; set; } = ToolUserValueClasses.DailyRoi;

    [DataMember(Order = 27)]
    public string RepeatabilityClass { get; set; } = ToolRepeatabilityClasses.Repeatable;

    [DataMember(Order = 28)]
    public string AutomationStage { get; set; } = ToolAutomationStages.CoreSkill;

    [DataMember(Order = 29)]
    public bool CanTeachBack { get; set; }

    [DataMember(Order = 30)]
    public List<string> FallbackArtifactKinds { get; set; } = new List<string>();

    [DataMember(Order = 31)]
    public string CommercialTier { get; set; } = CommercialTiers.Free;

    [DataMember(Order = 32)]
    public string CacheValueClass { get; set; } = CacheValueClasses.IntentToolchain;
}

[DataContract]
public sealed class CommandAtlasSearchRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string DocumentContext { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string CommandFamily { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int MaxResults { get; set; } = 10;
}

[DataContract]
public sealed class CommandAtlasMatch
{
    [DataMember(Order = 1)]
    public CommandAtlasEntry Entry { get; set; } = new CommandAtlasEntry();

    [DataMember(Order = 2)]
    public int Score { get; set; }

    [DataMember(Order = 3)]
    public string Reason { get; set; } = string.Empty;
}

[DataContract]
public sealed class CommandAtlasSearchResponse
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<CommandAtlasMatch> Matches { get; set; } = new List<CommandAtlasMatch>();
}

[DataContract]
public sealed class CommandDescribeRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CommandId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Query { get; set; } = string.Empty;
}

[DataContract]
public sealed class QuickActionRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string DocumentContext { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int? ActiveViewId { get; set; }

    [DataMember(Order = 6)]
    public string ActiveViewName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string ActiveViewType { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public int? CurrentLevelId { get; set; }

    [DataMember(Order = 9)]
    public string CurrentLevelName { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public int? CurrentSheetId { get; set; }

    [DataMember(Order = 11)]
    public string CurrentSheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public int SelectionCount { get; set; }
}

[DataContract]
public sealed class QuickActionResponse
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public CommandAtlasEntry MatchedEntry { get; set; } = new CommandAtlasEntry();

    [DataMember(Order = 4)]
    public string PlannedToolName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ResolvedPayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ExecutionDisposition { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool RequiresClarification { get; set; }

    [DataMember(Order = 8)]
    public List<string> MissingContext { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public double Confidence { get; set; }

    [DataMember(Order = 11)]
    public FallbackArtifactProposal FallbackProposal { get; set; } = new FallbackArtifactProposal();

    [DataMember(Order = 12)]
    public string StrategySummary { get; set; } = string.Empty;
}

[DataContract]
public sealed class CommandExecuteRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CommandId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool AllowAutoExecute { get; set; }

    [DataMember(Order = 6)]
    public string TargetDocument { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string TargetView { get; set; } = string.Empty;
}

[DataContract]
public sealed class CommandExecuteResponse
{
    [DataMember(Order = 1)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RequestPayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public CommandAtlasEntry Entry { get; set; } = new CommandAtlasEntry();

    [DataMember(Order = 6)]
    public bool ConfirmationRequired { get; set; }

    [DataMember(Order = 7)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string ToolResponseJson { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public FallbackArtifactProposal FallbackProposal { get; set; } = new FallbackArtifactProposal();
}

[DataContract]
public sealed class CoverageReportRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CoverageTier { get; set; } = string.Empty;
}

[DataContract]
public sealed class CoverageMatrixRow
{
    [DataMember(Order = 1)]
    public string CommandFamily { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CoverageTier { get; set; } = CommandCoverageTiers.Baseline;

    [DataMember(Order = 3)]
    public int TotalCommands { get; set; }

    [DataMember(Order = 4)]
    public int MappedCommands { get; set; }

    [DataMember(Order = 5)]
    public int ExecutableCommands { get; set; }

    [DataMember(Order = 6)]
    public int PreviewableCommands { get; set; }

    [DataMember(Order = 7)]
    public int VerifiedCommands { get; set; }
}

[DataContract]
public sealed class CoverageReportResponse
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalCommands { get; set; }

    [DataMember(Order = 3)]
    public int MappedCommands { get; set; }

    [DataMember(Order = 4)]
    public int ExecutableCommands { get; set; }

    [DataMember(Order = 5)]
    public int PreviewableCommands { get; set; }

    [DataMember(Order = 6)]
    public int VerifiedCommands { get; set; }

    [DataMember(Order = 7)]
    public List<CoverageMatrixRow> Families { get; set; } = new List<CoverageMatrixRow>();

    [DataMember(Order = 8)]
    public List<CommandAtlasEntry> UncoveredBaselineEntries { get; set; } = new List<CommandAtlasEntry>();
}

[DataContract]
public sealed class MemoryScopedSearchRequest
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RetrievalScope { get; set; } = RetrievalScopes.WorkflowPath;

    [DataMember(Order = 5)]
    public List<string> Namespaces { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string CommandFamily { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string SafetyClass { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string SourceKind { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string IssueKind { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public int MaxResults { get; set; } = 5;
}

[DataContract]
public sealed class ScopedMemoryHit
{
    [DataMember(Order = 1)]
    public string Namespace { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Id { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Snippet { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SourceRef { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string CreatedUtc { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public double Score { get; set; }
}

[DataContract]
public sealed class MemoryScopedSearchResponse
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RetrievalScope { get; set; } = RetrievalScopes.WorkflowPath;

    [DataMember(Order = 3)]
    public List<ScopedMemoryHit> Hits { get; set; } = new List<ScopedMemoryHit>();

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;
}
