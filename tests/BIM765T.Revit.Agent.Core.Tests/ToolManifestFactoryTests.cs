using System.Linq;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ToolManifestFactoryTests
{
    private static readonly AgentSettings Settings = new AgentSettings
    {
        RequestTimeoutSeconds = 60
    };

    [Fact]
    public void ToolManifestFactory_ReadTool_Uses_Explicit_Metadata()
    {
        var metadata = ToolManifestPresets.Review("document", "view")
            .WithTouchesActiveView()
            .WithRiskTags("qc")
            .WithRulePackTags("rule_set", "document_health_v1");

        var manifest = ToolManifestFactory.Create(
            ToolNames.ReviewRunRuleSet,
            "Run rule set.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            true,
            "{}",
            "{\"type\":\"object\"}",
            metadata,
            Settings);

        Assert.Equal(new[] { "document", "view" }, manifest.RequiredContext);
        Assert.True(manifest.TouchesActiveView);
        Assert.Equal("read_only", manifest.Idempotency);
        Assert.Contains("qc", manifest.RiskTags);
        Assert.Contains("rule_set", manifest.RulePackTags);
        Assert.DoesNotContain("mutation", manifest.RiskTags);
        Assert.DoesNotContain("high_risk", manifest.RiskTags);
        Assert.Equal(WorkerCapabilityPacks.CoreWorker, manifest.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.QualityControl, manifest.SkillGroup);
        Assert.Equal(ToolRiskTiers.Tier0, manifest.RiskTier);
        Assert.True(manifest.CanAutoExecute);
        Assert.Equal(ToolUiSurfaces.Evidence, manifest.UiSurface);
        Assert.Equal("review", manifest.DomainGroup);
        Assert.Equal("audit_qc", manifest.TaskFamily);
        Assert.Equal("bim765t.agents.specialist.audit", manifest.PackId);
        Assert.Contains("sheet_review_team_standard.v1", manifest.RecommendedPlaybooks);
        Assert.Equal(CapabilityDomains.Governance, manifest.CapabilityDomain);
        Assert.Equal(ToolDeterminismLevels.PolicyBacked, manifest.DeterminismLevel);
        Assert.True(manifest.RequiresPolicyPack);
        Assert.Equal(ToolVerificationModes.ReadBack, manifest.VerificationMode);
        Assert.Contains(CapabilityDisciplines.FireProtection, manifest.SupportedDisciplines);
        Assert.Contains(CapabilityIssueKinds.WarningTriage, manifest.IssueKinds);
        Assert.Equal(ToolPrimaryPersonas.QaManager, manifest.PrimaryPersona);
        Assert.Equal(ToolUserValueClasses.SmartValue, manifest.UserValueClass);
        Assert.Equal(ToolAutomationStages.CoreSkill, manifest.AutomationStage);
        Assert.Equal(CommercialTiers.Free, manifest.CommercialTier);
    }

    [Fact]
    public void ToolManifestFactory_MutationTool_Merges_Invariant_RiskTags_And_OverrideTimeout()
    {
        var metadata = ToolManifestPresets.Mutation("document")
            .WithRiskTags("delete")
            .WithExecutionTimeoutMs(12_345);

        var manifest = ToolManifestFactory.Create(
            ToolNames.ElementDeleteSafe,
            "Delete safely.",
            PermissionLevel.Mutate,
            ApprovalRequirement.HighRiskToken,
            true,
            true,
            string.Empty,
            "{\"type\":\"object\"}",
            metadata,
            Settings);

        Assert.True(manifest.MutatesModel);
        Assert.True(manifest.RequiresExpectedContext);
        Assert.Equal("non_idempotent", manifest.Idempotency);
        Assert.Contains("execution_result", manifest.PreviewArtifacts);
        Assert.Contains("diff_summary", manifest.PreviewArtifacts);
        Assert.Contains("delete", manifest.RiskTags);
        Assert.Contains("mutation", manifest.RiskTags);
        Assert.Contains("high_risk", manifest.RiskTags);
        Assert.Equal(12_345, manifest.ExecutionTimeoutMs);
        Assert.Equal(WorkerCapabilityPacks.CoreWorker, manifest.CapabilityPack);
        Assert.Equal(ToolRiskTiers.Tier2, manifest.RiskTier);
        Assert.False(manifest.CanAutoExecute);
        Assert.Equal(ToolUiSurfaces.Approvals, manifest.UiSurface);
        Assert.Equal("element", manifest.DomainGroup);
        Assert.Equal("revit_ops", manifest.TaskFamily);
        Assert.Equal(CapabilityDomains.Governance, manifest.CapabilityDomain);
        Assert.Equal(ToolDeterminismLevels.PolicyBacked, manifest.DeterminismLevel);
        Assert.Equal(ToolPrimaryPersonas.ProductionBimer, manifest.PrimaryPersona);
        Assert.Equal(ToolRepeatabilityClasses.Teachable, manifest.RepeatabilityClass);
        Assert.True(manifest.CanTeachBack);
        Assert.Equal(CommercialTiers.PersonalPro, manifest.CommercialTier);
    }

    [Fact]
    public void ToolManifestFactory_FileLifecycle_Uses_TimeoutFallback()
    {
        var metadata = ToolManifestPresets.FileLifecycle("document").WithRiskTags("save");

        var manifest = ToolManifestFactory.Create(
            ToolNames.ExportIfcSafe,
            "Export IFC safely.",
            PermissionLevel.FileLifecycle,
            ApprovalRequirement.HighRiskToken,
            true,
            true,
            string.Empty,
            "{\"type\":\"object\"}",
            metadata,
            Settings);

        Assert.Equal(300_000, manifest.ExecutionTimeoutMs);
        Assert.Contains("mutation", manifest.RiskTags);
        Assert.Contains("high_risk", manifest.RiskTags);
        Assert.Contains("save", manifest.RiskTags);
        Assert.Equal(ToolLatencyClasses.LongRunning, manifest.LatencyClass);
        Assert.Equal(ToolProgressModes.Heartbeat, manifest.ProgressMode);
        Assert.Equal("export", manifest.DomainGroup);
        Assert.Equal("delivery_data", manifest.TaskFamily);
    }

    [Fact]
    public void ToolManifestFactory_WorkflowTool_Can_Be_Checkpointed()
    {
        var metadata = ToolManifestPresets.WorkflowMutate("document", "workflow_run");

        var manifest = ToolManifestFactory.Create(
            ToolNames.WorkflowApply,
            "Apply workflow.",
            PermissionLevel.Mutate,
            ApprovalRequirement.ConfirmToken,
            false,
            true,
            string.Empty,
            "{\"type\":\"object\"}",
            metadata,
            Settings);

        Assert.Equal("checkpointed", manifest.Idempotency);
        Assert.Equal("chunked", manifest.BatchMode);
        Assert.Contains("workflow", manifest.RiskTags);
        Assert.Contains("workflow_evidence", manifest.PreviewArtifacts);
        Assert.Equal(new[] { "document", "workflow_run" }, manifest.RequiredContext);
        Assert.Equal(WorkerSkillGroups.Orchestration, manifest.SkillGroup);
        Assert.Equal(ToolLatencyClasses.Batch, manifest.LatencyClass);
        Assert.Equal(ToolUiSurfaces.Queue, manifest.UiSurface);
        Assert.Equal("workflow", manifest.DomainGroup);
        Assert.Equal("orchestration", manifest.TaskFamily);
        Assert.Equal(CapabilityDomains.General, manifest.CapabilityDomain);
        Assert.False(manifest.RequiresPolicyPack);
    }

    [Fact]
    public void ToolManifestFactory_AutomationLab_Metadata_Is_Carried_To_Manifest()
    {
        var metadata = ToolManifestPresets.Mutation("family_document")
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal);

        var manifest = ToolManifestFactory.Create(
            ToolNames.FamilyAddParameterSafe,
            "Add family parameter safely.",
            PermissionLevel.Mutate,
            ApprovalRequirement.ConfirmToken,
            true,
            false,
            string.Empty,
            "{\"type\":\"object\"}",
            metadata,
            Settings);

        Assert.Equal(WorkerCapabilityPacks.AutomationLab, manifest.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.Automation, manifest.SkillGroup);
        Assert.Equal(WorkerAudience.Internal, manifest.Audience);
        Assert.Equal(WorkerVisibility.BetaInternal, manifest.Visibility);
        Assert.False(manifest.Enabled);
        Assert.Equal(ToolRiskTiers.Tier1, manifest.RiskTier);
        Assert.Equal("family", manifest.DomainGroup);
        Assert.Equal("family_authoring", manifest.TaskFamily);
        Assert.Contains("family_benchmark_servicebox.v1", manifest.RecommendedPlaybooks);
        Assert.Equal(CapabilityDomains.FamilyQa, manifest.CapabilityDomain);
        Assert.Contains(CapabilityIssueKinds.FamilyQa, manifest.IssueKinds);
        Assert.Equal(CommercialTiers.Internal, manifest.CommercialTier);
        Assert.Equal(CacheValueClasses.TeachBack, manifest.CacheValueClass);
    }

    [Fact]
    public void ToolManifestFactory_DataTool_Emits_Fallback_Metadata_For_Mapping_Artifacts()
    {
        var manifest = ToolManifestFactory.Create(
            ToolNames.DataExportSchedule,
            "Export schedule data.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            true,
            string.Empty,
            "{\"type\":\"object\"}",
            ToolManifestPresets.Read("document"),
            Settings);

        Assert.Equal(ToolPrimaryPersonas.ProductionBimer, manifest.PrimaryPersona);
        Assert.Equal(ToolUserValueClasses.DailyRoi, manifest.UserValueClass);
        Assert.Equal(ToolRepeatabilityClasses.Repeatable, manifest.RepeatabilityClass);
        Assert.True(manifest.CanTeachBack);
        Assert.Contains(FallbackArtifactKinds.Playbook, manifest.FallbackArtifactKinds);
        Assert.Contains(FallbackArtifactKinds.CsvMapping, manifest.FallbackArtifactKinds);
        Assert.Contains(FallbackArtifactKinds.OpenXmlRecipe, manifest.FallbackArtifactKinds);
        Assert.Equal(CommercialTiers.PersonalPro, manifest.CommercialTier);
    }

    [Fact]
    public void ToolManifestFactory_SystemAndIntentTools_Get_New_Capability_Metadata()
    {
        var systemManifest = ToolManifestFactory.Create(
            ToolNames.SystemCaptureGraph,
            "Capture graph.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            true,
            string.Empty,
            "{\"type\":\"object\"}",
            ToolManifestPresets.Review().WithSkillGroup(WorkerSkillGroups.Systems),
            Settings);

        var intentManifest = ToolManifestFactory.Create(
            ToolNames.IntentCompile,
            "Compile intent.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            true,
            string.Empty,
            "{\"type\":\"object\"}",
            ToolManifestPresets.Review().WithSkillGroup(WorkerSkillGroups.Intent),
            Settings);

        Assert.Equal(CapabilityDomains.Systems, systemManifest.CapabilityDomain);
        Assert.Equal(ToolDeterminismLevels.Scaffold, systemManifest.DeterminismLevel);
        Assert.Equal(ToolVerificationModes.SystemConsistency, systemManifest.VerificationMode);
        Assert.Contains(CapabilityIssueKinds.DisconnectedSystem, systemManifest.IssueKinds);
        Assert.Contains(CapabilityDisciplines.Mechanical, systemManifest.SupportedDisciplines);

        Assert.Equal(CapabilityDomains.Intent, intentManifest.CapabilityDomain);
        Assert.Equal(ToolDeterminismLevels.PolicyBacked, intentManifest.DeterminismLevel);
        Assert.False(intentManifest.RequiresPolicyPack);
        Assert.Contains(CapabilityIssueKinds.IntentCompile, intentManifest.IssueKinds);
    }

    [Fact]
    public void ToolManifestFactory_NativeCommands_Prefer_Native_Command_Metadata()
    {
        var metadata = ToolManifestPresets.Read()
            .WithExecutionMode(CommandExecutionModes.Native)
            .WithNativeCommandId("Revit.Ribbon.View.Create3D")
            .WithCapabilityDomain(CapabilityDomains.Governance)
            .WithCommandFamily("view_command");

        var manifest = ToolManifestFactory.Create(
            "native.view.create_3d",
            "Create native 3D view.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            true,
            string.Empty,
            "{\"type\":\"object\"}",
            metadata,
            Settings);

        Assert.Equal(CommandExecutionModes.Native, manifest.ExecutionMode);
        Assert.Equal(CommandSourceKinds.Revit, manifest.SourceKind);
        Assert.Equal("Revit.Ribbon.View.Create3D", manifest.SourceRef);
        Assert.Equal("view_command", manifest.CommandFamily);
    }
}
