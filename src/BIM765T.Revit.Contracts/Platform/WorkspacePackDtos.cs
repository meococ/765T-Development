using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class PackDependency
{
    [DataMember(Order = 1)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string VersionRange { get; set; } = string.Empty;
}

[DataContract]
public sealed class PackCompatibility
{
    [DataMember(Order = 1)]
    public List<string> SupportedRevitYears { get; set; } = new List<string>();

    [DataMember(Order = 2)]
    public List<string> SupportedHosts { get; set; } = new List<string>();
}

[DataContract]
public sealed class PackExport
{
    [DataMember(Order = 1)]
    public string ExportKind { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RelativePath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ExportId { get; set; } = string.Empty;
}

[DataContract]
public sealed class PackManifest
{
    [DataMember(Order = 1)]
    public string PackType { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Version { get; set; } = "1.0.0";

    [DataMember(Order = 4)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Owner { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<PackDependency> Dependencies { get; set; } = new List<PackDependency>();

    [DataMember(Order = 8)]
    public PackCompatibility Compatibility { get; set; } = new PackCompatibility();

    [DataMember(Order = 9)]
    public List<PackExport> Exports { get; set; } = new List<PackExport>();

    [DataMember(Order = 10)]
    public bool EnabledByDefault { get; set; } = true;

    [DataMember(Order = 11)]
    public List<string> CapabilityDomains { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public List<string> IssueKinds { get; set; } = new List<string>();

    [DataMember(Order = 14)]
    public List<string> VerificationModes { get; set; } = new List<string>();

    [DataMember(Order = 15)]
    public List<string> LegacyPackIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class PackCatalogEntry
{
    [DataMember(Order = 1)]
    public PackManifest Manifest { get; set; } = new PackManifest();

    [DataMember(Order = 2)]
    public string RootPath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SourcePath { get; set; } = string.Empty;
}

[DataContract]
public sealed class PackListRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PackType { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool EnabledOnly { get; set; }
}

[DataContract]
public sealed class PackListResponse
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<PackCatalogEntry> Packs { get; set; } = new List<PackCatalogEntry>();
}

[DataContract]
public sealed class WorkspaceRuntimePolicy
{
    [DataMember(Order = 1)]
    public bool CommitMostState { get; set; } = true;

    [DataMember(Order = 2)]
    public bool UseGitLfsForLargeState { get; set; } = true;

    [DataMember(Order = 3)]
    public string ScratchRelativePath { get; set; } = "scratch";
}

[DataContract]
public sealed class WorkspaceModelProviderConfig
{
    [DataMember(Order = 1)]
    public string DefaultModel { get; set; } = "gpt-5.4";

    [DataMember(Order = 2)]
    public string Provider { get; set; } = "openai";
}

[DataContract]
public sealed class WorkspaceManifest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = "default";

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<string> EnabledPacks { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public List<string> PreferredStandardsPacks { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> PreferredPlaybookPacks { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> AllowedAgents { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> AllowedSpecialists { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public WorkspaceModelProviderConfig ModelProvider { get; set; } = new WorkspaceModelProviderConfig();

    [DataMember(Order = 9)]
    public WorkspaceRuntimePolicy RuntimePolicy { get; set; } = new WorkspaceRuntimePolicy();
}

[DataContract]
public sealed class WorkspaceGetManifestRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkspaceManifestResponse
{
    [DataMember(Order = 1)]
    public WorkspaceManifest Workspace { get; set; } = new WorkspaceManifest();

    [DataMember(Order = 2)]
    public string RootPath { get; set; } = string.Empty;
}

[DataContract]
public sealed class StandardsResolutionRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StandardKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> RequestedKeys { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> PreferredPackIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class StandardsResolvedValue
{
    [DataMember(Order = 1)]
    public string RequestedKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SourcePackId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SourceFile { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool Matched { get; set; }
}

[DataContract]
public sealed class StandardsResolvedFile
{
    [DataMember(Order = 1)]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SourcePackId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RelativePath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ContentJson { get; set; } = string.Empty;
}

[DataContract]
public sealed class StandardsResolution
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StandardKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Discipline { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> CandidatePackIds { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<StandardsResolvedFile> Files { get; set; } = new List<StandardsResolvedFile>();

    [DataMember(Order = 7)]
    public List<StandardsResolvedValue> Values { get; set; } = new List<StandardsResolvedValue>();
}

[DataContract]
public sealed class PlaybookMatchRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentContext { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 5;

    [DataMember(Order = 5)]
    public string PreferredCapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Discipline { get; set; } = string.Empty;
}

[DataContract]
public sealed class PlaybookMatchResponse
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public PlaybookRecommendation RecommendedPlaybook { get; set; } = new PlaybookRecommendation();

    [DataMember(Order = 4)]
    public List<PlaybookRecommendation> Matches { get; set; } = new List<PlaybookRecommendation>();
}

[DataContract]
public sealed class PlaybookPreviewRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Query { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string DocumentContext { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string PreferredCapabilityDomain { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Discipline { get; set; } = string.Empty;
}

[DataContract]
public sealed class PlaybookPreviewStep
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
    public string ParametersJson { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string OutputKey { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string StepId { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string StepKind { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string LoopOver { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public List<string> RequiredStandardsRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class PlaybookPreviewResponse
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<PlaybookPreviewStep> Steps { get; set; } = new List<PlaybookPreviewStep>();

    [DataMember(Order = 5)]
    public List<string> RequiredInputs { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> StandardsRefs { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public StandardsResolution Standards { get; set; } = new StandardsResolution();

    [DataMember(Order = 8)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 9)]
    public string DeterminismLevel { get; set; } = ToolDeterminismLevels.PolicyBacked;

    [DataMember(Order = 10)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 11)]
    public List<string> RecommendedSpecialists { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> PolicyPackIds { get; set; } = new List<string>();
}
