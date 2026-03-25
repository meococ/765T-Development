using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class CommandAtlasServiceTests : IDisposable
{
    private readonly string _curatedRoot = Path.Combine(Path.GetTempPath(), "BIM765T.CommandAtlasTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_curatedRoot))
        {
            Directory.Delete(_curatedRoot, recursive: true);
        }
    }

    [Fact]
    public void PlanQuickAction_Uses_CommandPackEntry_For_Create3D_View()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));

        var response = service.PlanQuickAction(Array.Empty<ToolManifest>(), new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "create 3d view"
        });

        Assert.Equal("revit.view.create_3d", response.MatchedEntry.CommandId);
        Assert.Equal(ToolNames.ViewCreate3dSafe, response.PlannedToolName);
        Assert.False(response.RequiresClarification);
        Assert.Equal("preview", response.ExecutionDisposition);

        var payload = JsonUtil.DeserializeRequired<Create3DViewRequest>(response.ResolvedPayloadJson);
        Assert.True(payload.ActivateViewAfterCreate);
    }

    [Fact]
    public void PlanQuickAction_RenumberSheet_Uses_CurrentSheet_Context()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));

        var response = service.PlanQuickAction(Array.Empty<ToolManifest>(), new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "renumber sheet A101 to A201",
            CurrentSheetId = 77,
            CurrentSheetNumber = "A101",
            DocumentContext = "project"
        });

        Assert.Equal("revit.sheet.renumber", response.MatchedEntry.CommandId);
        Assert.Equal(ToolNames.SheetRenumberSafe, response.PlannedToolName);
        Assert.False(response.RequiresClarification);
        Assert.Equal("preview", response.ExecutionDisposition);

        var payload = JsonUtil.DeserializeRequired<RenumberSheetRequest>(response.ResolvedPayloadJson);
        Assert.Equal(77, payload.SheetId);
        Assert.Equal("A101", payload.OldSheetNumber);
        Assert.Equal("A201", payload.NewSheetNumber);
    }

    [Fact]
    public void PlanQuickAction_CreateSheet_Uses_Preview_Disposition()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));

        var response = service.PlanQuickAction(Array.Empty<ToolManifest>(), new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "create sheet A101"
        });

        Assert.Equal("revit.sheet.create", response.MatchedEntry.CommandId);
        Assert.Equal(ToolNames.SheetCreateSafe, response.PlannedToolName);
        Assert.False(response.RequiresClarification);
        Assert.Equal("preview", response.ExecutionDisposition);
    }

    [Fact]
    public void PlanQuickAction_DuplicateView_Uses_Preview_Disposition()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));

        var response = service.PlanQuickAction(Array.Empty<ToolManifest>(), new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "duplicate active view dependent",
            ActiveViewId = 42,
            DocumentContext = "project"
        });

        Assert.Equal("revit.view.duplicate", response.MatchedEntry.CommandId);
        Assert.Equal(ToolNames.ViewDuplicateSafe, response.PlannedToolName);
        Assert.False(response.RequiresClarification);
        Assert.Equal("preview", response.ExecutionDisposition);
    }

    [Fact]
    public void PlanQuickAction_Hides_NonMvp_Command()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));

        var response = service.PlanQuickAction(Array.Empty<ToolManifest>(), new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "zoom all"
        });

        Assert.True(string.IsNullOrWhiteSpace(response.MatchedEntry.CommandId));
        Assert.True(response.RequiresClarification);
        Assert.True(string.IsNullOrWhiteSpace(response.PlannedToolName));
    }

    [Fact]
    public void PlanQuickAction_AtlasMiss_Returns_FallbackProposal()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));

        var response = service.PlanQuickAction(Array.Empty<ToolManifest>(), new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "xylophone banana quantum workflow"
        });

        Assert.True(response.RequiresClarification);
        Assert.True(string.IsNullOrWhiteSpace(response.MatchedEntry.CommandId));
        Assert.Contains(FallbackArtifactKinds.Playbook, response.FallbackProposal.ArtifactKinds);
        Assert.Equal("atlas_search -> fallback_artifact", response.StrategySummary);
    }

    [Fact]
    public void PlanQuickAction_DataAtlasMiss_Returns_DataFallbackKinds()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));

        var response = service.PlanQuickAction(Array.Empty<ToolManifest>(), new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "import schedule parameters from excel csv"
        });

        Assert.Contains(FallbackArtifactKinds.Playbook, response.FallbackProposal.ArtifactKinds);
        Assert.Contains(FallbackArtifactKinds.CsvMapping, response.FallbackProposal.ArtifactKinds);
        Assert.Contains(FallbackArtifactKinds.OpenXmlRecipe, response.FallbackProposal.ArtifactKinds);
    }

    [Fact]
    public void PlanQuickAction_MappedOnly_Returns_FallbackProposal()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));
        var manifests = new[]
        {
            CreateManifest(
                ToolNames.ReviewSmartQc,
                "Review smart qc through native lane only.",
                executionMode: CommandExecutionModes.Native,
                primaryPersona: ToolPrimaryPersonas.ProductionBimer,
                userValueClass: ToolUserValueClasses.DailyRoi,
                fallbackKinds: new[] { FallbackArtifactKinds.Playbook },
                recommendedPlaybooks: new[] { "warning_triage_safe.v1" })
        };

        var response = service.PlanQuickAction(manifests, new QuickActionRequest
        {
            WorkspaceId = "default",
            Query = "smart qc review"
        });

        Assert.Equal(ToolNames.ReviewSmartQc, response.MatchedEntry.CommandId);
        Assert.Equal("mapped_only", response.ExecutionDisposition);
        Assert.Contains(FallbackArtifactKinds.Playbook, response.FallbackProposal.ArtifactKinds);
        Assert.Equal("tool -> playbook -> fallback_artifact (mapped_only)", response.StrategySummary);
    }

    [Fact]
    public void Search_Prefers_DailyRoi_ProductionBimer_When_TextScore_Is_Similar()
    {
        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), new CuratedScriptRegistryService(_curatedRoot));
        var manifests = new[]
        {
            CreateManifest(
                ToolNames.SheetCreateSafe,
                "Sheet package pattern for daily production.",
                primaryPersona: ToolPrimaryPersonas.ProductionBimer,
                userValueClass: ToolUserValueClasses.DailyRoi,
                canTeachBack: true,
                commercialTier: CommercialTiers.PersonalPro),
            CreateManifest(
                ToolNames.ReviewSheetSummary,
                "Sheet package pattern for daily production.",
                permissionLevel: PermissionLevel.Review,
                primaryPersona: ToolPrimaryPersonas.QaManager,
                userValueClass: ToolUserValueClasses.SmartValue,
                canTeachBack: false,
                commercialTier: CommercialTiers.Free)
        };

        var response = service.Search(manifests, new CommandAtlasSearchRequest
        {
            WorkspaceId = "default",
            Query = "sheet package pattern",
            MaxResults = 5
        });

        Assert.NotEmpty(response.Matches);
        Assert.Equal(ToolNames.SheetCreateSafe, response.Matches[0].Entry.CommandId);
    }

    [Fact]
    public void BuildAtlas_Filters_Runtime_Curated_Scripts_Outside_Mvp_Surface()
    {
        var curated = new CuratedScriptRegistryService(_curatedRoot);
        var import = curated.Import(new ScriptImportManifestRequest
        {
            OverwriteExisting = true,
            Manifest = new ScriptSourceManifest
            {
                ScriptId = "builtin.audit_family_parameters.runtime",
                DisplayName = "Runtime Family Audit",
                Description = "Runtime curated registry test entry.",
                SourceKind = CommandSourceKinds.Internal,
                SourceRef = "builtin",
                EntryPoint = "builtin.audit_family_parameters.runtime",
                CapabilityDomain = CapabilityDomains.FamilyQa,
                SupportedDisciplines = new List<string> { CapabilityDisciplines.Common },
                AllowedSurfaces = new List<string> { "worker" },
                SafetyClass = CommandSafetyClasses.ReadOnly,
                ApprovalRequirement = ApprovalRequirement.None,
                VerificationMode = ToolVerificationModes.ReportOnly,
                Tags = new List<string> { "family", "runtime" },
                Approved = true,
                VerificationRecipe = "read_back"
            }
        });

        Assert.True(import.Imported);

        var service = new CommandAtlasService(new PackCatalogService(), new WorkspaceCatalogService(), curated);
        var search = service.Search(Array.Empty<ToolManifest>(), new CommandAtlasSearchRequest
        {
            WorkspaceId = "default",
            Query = "runtime family audit",
            MaxResults = 5
        });

        Assert.Empty(search.Matches);
    }

    private static ToolManifest CreateManifest(
        string toolName,
        string description,
        PermissionLevel permissionLevel = PermissionLevel.Read,
        string executionMode = CommandExecutionModes.Tool,
        string? primaryPersona = null,
        string? userValueClass = null,
        bool canTeachBack = false,
        string? commercialTier = null,
        IEnumerable<string>? fallbackKinds = null,
        IEnumerable<string>? recommendedPlaybooks = null)
    {
        return new ToolManifest
        {
            ToolName = toolName,
            Description = description,
            PermissionLevel = permissionLevel,
            Enabled = true,
            CommandFamily = "test",
            ExecutionMode = executionMode,
            SourceKind = CommandSourceKinds.Internal,
            SourceRef = toolName,
            CapabilityDomain = CapabilityDomains.Governance,
            SupportedDisciplines = new List<string> { CapabilityDisciplines.Common },
            CanPreview = permissionLevel != PermissionLevel.Read,
            CanAutoExecute = permissionLevel == PermissionLevel.Read,
            PrimaryPersona = primaryPersona ?? ToolPrimaryPersonas.ProductionBimer,
            UserValueClass = userValueClass ?? ToolUserValueClasses.DailyRoi,
            RepeatabilityClass = canTeachBack ? ToolRepeatabilityClasses.Teachable : ToolRepeatabilityClasses.Repeatable,
            AutomationStage = ToolAutomationStages.CoreSkill,
            CanTeachBack = canTeachBack,
            FallbackArtifactKinds = fallbackKinds?.ToList() ?? new List<string>(),
            CommercialTier = commercialTier ?? CommercialTiers.PersonalPro,
            CacheValueClass = canTeachBack ? CacheValueClasses.TeachBack : CacheValueClasses.IntentToolchain,
            RecommendedPlaybooks = recommendedPlaybooks?.ToList() ?? new List<string>(),
            VerificationMode = permissionLevel == PermissionLevel.Read ? ToolVerificationModes.ReportOnly : ToolVerificationModes.PolicyCheck
        };
    }
}
