using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class LocalPluginManifest
{
    [DataMember(Order = 1)]
    public string PluginId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Version { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string CapabilityPack { get; set; } = WorkerCapabilityPacks.AutomationLab;

    [DataMember(Order = 6)]
    public string EntryPoint { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> AllowedSurfaces { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> ArtifactKinds { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public bool SupportsCallbacks { get; set; }

    [DataMember(Order = 10)]
    public bool Enabled { get; set; } = true;
}

[DataContract]
public sealed class PluginCatalogResponse
{
    [DataMember(Order = 1)]
    public List<LocalPluginManifest> Plugins { get; set; } = new List<LocalPluginManifest>();
}

[DataContract]
public sealed class MemoryPromotionPolicy
{
    [DataMember(Order = 1)]
    public bool RequiresVerifiedRun { get; set; } = true;

    [DataMember(Order = 2)]
    public List<string> AllowedKinds { get; set; } = new List<string> { "lesson", "playbook", "verified_run", "evidence_bundle" };

    [DataMember(Order = 3)]
    public List<string> SemanticEligibleKinds { get; set; } = new List<string> { "lesson", "playbook", "verified_run" };

    [DataMember(Order = 4)]
    public int MaxArtifactRefs { get; set; } = 25;
}

[DataContract]
public sealed class ScriptSourceManifest
{
    [DataMember(Order = 1)]
    public string ScriptId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Version { get; set; } = "1.0.0";

    [DataMember(Order = 4)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SourceKind { get; set; } = CommandSourceKinds.Internal;

    [DataMember(Order = 6)]
    public string SourceRef { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string EntryPoint { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 9)]
    public List<string> SupportedDisciplines { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<string> AllowedSurfaces { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public string SafetyClass { get; set; } = CommandSafetyClasses.PreviewedMutation;

    [DataMember(Order = 12)]
    public ApprovalRequirement ApprovalRequirement { get; set; } = ApprovalRequirement.ConfirmToken;

    [DataMember(Order = 13)]
    public string VerificationMode { get; set; } = ToolVerificationModes.ReportOnly;

    [DataMember(Order = 14)]
    public string InputSchemaHint { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 16)]
    public bool Approved { get; set; }

    [DataMember(Order = 17)]
    public string VerificationRecipe { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public string PrimaryPersona { get; set; } = ToolPrimaryPersonas.ProductionBimer;

    [DataMember(Order = 19)]
    public string UserValueClass { get; set; } = ToolUserValueClasses.SmartValue;

    [DataMember(Order = 20)]
    public string RepeatabilityClass { get; set; } = ToolRepeatabilityClasses.Teachable;

    [DataMember(Order = 21)]
    public string AutomationStage { get; set; } = ToolAutomationStages.ArtifactFallback;

    [DataMember(Order = 22)]
    public List<string> FallbackArtifactKinds { get; set; } = new List<string>();

    [DataMember(Order = 23)]
    public string CommercialTier { get; set; } = CommercialTiers.PersonalPro;

    [DataMember(Order = 24)]
    public string CacheValueClass { get; set; } = CacheValueClasses.ArtifactReuse;

    [DataMember(Order = 25)]
    public string ImportMode { get; set; } = SourceImportModes.WrapperOnly;

    [DataMember(Order = 26)]
    public List<string> SourceLogicIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class ScriptSourceVerifyRequest
{
    [DataMember(Order = 1)]
    public ScriptSourceManifest Manifest { get; set; } = new ScriptSourceManifest();
}

[DataContract]
public sealed class ScriptSourceVerifyResponse
{
    [DataMember(Order = 1)]
    public bool IsValid { get; set; }

    [DataMember(Order = 2)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<string> Errors { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public List<string> Warnings { get; set; } = new List<string>();
}

[DataContract]
public sealed class ScriptImportManifestRequest
{
    [DataMember(Order = 1)]
    public ScriptSourceManifest Manifest { get; set; } = new ScriptSourceManifest();

    [DataMember(Order = 2)]
    public bool OverwriteExisting { get; set; }
}

[DataContract]
public sealed class ScriptImportManifestResponse
{
    [DataMember(Order = 1)]
    public bool Imported { get; set; }

    [DataMember(Order = 2)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ManifestPath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public ScriptSourceVerifyResponse Verification { get; set; } = new ScriptSourceVerifyResponse();
}

[DataContract]
public sealed class ScriptInstallPackRequest
{
    [DataMember(Order = 1)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<ScriptSourceManifest> Scripts { get; set; } = new List<ScriptSourceManifest>();
}

[DataContract]
public sealed class ScriptInstallPackResponse
{
    [DataMember(Order = 1)]
    public string PackId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int InstalledCount { get; set; }

    [DataMember(Order = 3)]
    public List<string> ManifestPaths { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;
}
