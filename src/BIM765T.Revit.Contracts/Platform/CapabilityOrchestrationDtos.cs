using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

public static class CapabilityDomains
{
    public const string General = "general";
    public const string Governance = "governance";
    public const string Annotation = "annotation";
    public const string FamilyQa = "family_qa";
    public const string Coordination = "coordination";
    public const string Systems = "systems";
    public const string Intent = "intent";
    public const string Integration = "integration";
}

public static class ToolDeterminismLevels
{
    public const string Deterministic = "deterministic";
    public const string PolicyBacked = "policy_backed";
    public const string Assisted = "assisted";
    public const string Experimental = "experimental";
    public const string Scaffold = "scaffold";
}

public static class ToolVerificationModes
{
    public const string None = "none";
    public const string ReadBack = "read_back";
    public const string PolicyCheck = "policy_check";
    public const string GeometryCheck = "geometry_check";
    public const string SystemConsistency = "system_consistency";
    public const string ReportOnly = "report_only";
}

public static class CapabilityDisciplines
{
    public const string Common = "common";
    public const string Architecture = "architecture";
    public const string Structure = "structure";
    public const string Mep = "mep";
    public const string Mechanical = "mechanical";
    public const string Electrical = "electrical";
    public const string Plumbing = "plumbing";
    public const string FireProtection = "fire_protection";
}

public static class CapabilityIssueKinds
{
    public const string NamingConvention = "naming_convention";
    public const string SheetPackage = "sheet_package";
    public const string TagOverlap = "tag_overlap";
    public const string DimensionCollision = "dimension_collision";
    public const string ModelCleanup = "model_cleanup";
    public const string ParameterPopulation = "parameter_population";
    public const string RoomFinishGeneration = "room_finish_generation";
    public const string WarningTriage = "warning_triage";
    public const string FamilyQa = "family_qa";
    public const string HardClash = "hard_clash";
    public const string ClearanceSoftClash = "clearance_soft_clash";
    public const string LodLoiCompliance = "lod_loi_compliance";
    public const string DisconnectedSystem = "disconnected_system";
    public const string SlopeContinuity = "slope_continuity";
    public const string BasicRouting = "basic_routing";
    public const string IntentCompile = "intent_compile";
    public const string ExternalSync = "external_sync";
    public const string SystemSizing = "system_sizing";
    public const string LargeModelSplit = "large_model_split";
    public const string ScanToBim = "scan_to_bim";
}

[DataContract]
public sealed class PolicyPackManifest
{
    [DataMember(Order = 1)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> CapabilityDomains { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> VerificationModes { get; set; } = new List<string>();
}

[DataContract]
public sealed class PolicyResolutionRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> PreferredPackIds { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> RequestedStandardKinds { get; set; } = new List<string>();
}

[DataContract]
public sealed class PolicyResolution
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> CandidatePackIds { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> ResolvedPackIds { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<PolicyPackManifest> ResolvedPacks { get; set; } = new List<PolicyPackManifest>();

    [DataMember(Order = 8)]
    public List<StandardsResolvedFile> Files { get; set; } = new List<StandardsResolvedFile>();

    [DataMember(Order = 9)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class CapabilitySpecialistDescriptor
{
    [DataMember(Order = 1)]
    public string SpecialistId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> CapabilityDomains { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public int Score { get; set; }
}

[DataContract]
public sealed class CapabilitySpecialistRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> IssueKinds { get; set; } = new List<string>();
}

[DataContract]
public sealed class CapabilitySpecialistResponse
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<CapabilitySpecialistDescriptor> Specialists { get; set; } = new List<CapabilitySpecialistDescriptor>();

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class IssueRecord
{
    [DataMember(Order = 1)]
    public string IssueId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string IssueKind { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Severity { get; set; } = "warning";

    [DataMember(Order = 6)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string SourceToolName { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> EvidenceRefs { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public List<string> CandidateFixToolNames { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 11)]
    public bool RequiresApproval { get; set; }
}

[DataContract]
public sealed class FixProposal
{
    [DataMember(Order = 1)]
    public string ProposalId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string IssueId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string DeterminismLevel { get; set; } = ToolDeterminismLevels.PolicyBacked;

    [DataMember(Order = 7)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 8)]
    public string PolicyPackId { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 10)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class FixPlan
{
    [DataMember(Order = 1)]
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string IssueKind { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<IssueRecord> Issues { get; set; } = new List<IssueRecord>();

    [DataMember(Order = 7)]
    public List<FixProposal> Proposals { get; set; } = new List<FixProposal>();

    [DataMember(Order = 8)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 9)]
    public List<string> ResidualHints { get; set; } = new List<string>();
}

[DataContract]
public sealed class VerificationPacket
{
    [DataMember(Order = 1)]
    public string PacketId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 3)]
    public string Status { get; set; } = "pending";

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> CheckedIssueIds { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> ResidualIssueIds { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class SpatialContextSnapshot
{
    [DataMember(Order = 1)]
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.Coordination;

    [DataMember(Order = 4)]
    public string Discipline { get; set; } = CapabilityDisciplines.Common;

    [DataMember(Order = 5)]
    public List<string> ZoneHints { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> ElementRefs { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class SystemGraphNode
{
    [DataMember(Order = 1)]
    public string NodeId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string NodeKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SystemName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsOpenEnd { get; set; }
}

[DataContract]
public sealed class SystemGraphEdge
{
    [DataMember(Order = 1)]
    public string EdgeId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FromNodeId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ToNodeId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string EdgeKind { get; set; } = string.Empty;
}

[DataContract]
public sealed class SystemGraphRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> SystemNames { get; set; } = new List<string>();
}

[DataContract]
public sealed class SystemGraphSnapshot
{
    [DataMember(Order = 1)]
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<SystemGraphNode> Nodes { get; set; } = new List<SystemGraphNode>();

    [DataMember(Order = 6)]
    public List<SystemGraphEdge> Edges { get; set; } = new List<SystemGraphEdge>();

    [DataMember(Order = 7)]
    public List<string> OpenNodeIds { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class IntentTask
{
    [DataMember(Order = 1)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentContext { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string RequestedOutcome { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> Constraints { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> IssueKinds { get; set; } = new List<string>();
}

[DataContract]
public sealed class IntentCompileRequest
{
    [DataMember(Order = 1)]
    public IntentTask Task { get; set; } = new IntentTask();

    [DataMember(Order = 2)]
    public string PreferredCapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool RequireDeterministicPlan { get; set; } = true;
}

[DataContract]
public sealed class IntentValidateRequest
{
    [DataMember(Order = 1)]
    public CompiledTaskPlan Plan { get; set; } = new CompiledTaskPlan();
}

[DataContract]
public sealed class IntentValidationResponse
{
    [DataMember(Order = 1)]
    public bool IsValid { get; set; }

    [DataMember(Order = 2)]
    public List<string> Errors { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public List<string> Warnings { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class CompiledTaskPlan
{
    [DataMember(Order = 1)]
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public IntentTask Task { get; set; } = new IntentTask();

    [DataMember(Order = 3)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string DeterminismLevel { get; set; } = ToolDeterminismLevels.PolicyBacked;

    [DataMember(Order = 5)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 6)]
    public PolicyResolution PolicyResolution { get; set; } = new PolicyResolution();

    [DataMember(Order = 7)]
    public PlaybookRecommendation RecommendedPlaybook { get; set; } = new PlaybookRecommendation();

    [DataMember(Order = 8)]
    public List<CapabilitySpecialistDescriptor> RecommendedSpecialists { get; set; } = new List<CapabilitySpecialistDescriptor>();

    [DataMember(Order = 9)]
    public List<string> CandidateToolNames { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<string> IssueScanTools { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public List<string> FixTools { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> VerifyTools { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class SystemFixPlanRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string IssueKind { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Query { get; set; } = string.Empty;
}

[DataContract]
public sealed class IntegrationPreviewSyncRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ExternalSystem { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string EntityKind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> SourceArtifactRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class ExternalSyncDelta
{
    [DataMember(Order = 1)]
    public string ExternalSystem { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string EntityKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int AddedCount { get; set; }

    [DataMember(Order = 6)]
    public int UpdatedCount { get; set; }

    [DataMember(Order = 7)]
    public int RemovedCount { get; set; }

    [DataMember(Order = 8)]
    public List<string> RiskNotes { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public string Summary { get; set; } = string.Empty;
}

[DataContract]
public sealed class ProjectScorecard
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public double HealthScore { get; set; }

    [DataMember(Order = 4)]
    public List<CountByNameDto> IssueCounts { get; set; } = new List<CountByNameDto>();

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;
}
