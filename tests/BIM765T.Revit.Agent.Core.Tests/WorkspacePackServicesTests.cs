using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class WorkspacePackServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BIM765T-WorkspacePackTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Pack_And_Workspace_Catalogs_Load_Default_Topology()
    {
        CreateRepoSkeleton();
        WritePackManifest("packs/standards/core/pack.json", new PackManifest
        {
            PackType = "standards-pack",
            PackId = "bim765t.standards.core",
            DisplayName = "Core standards",
            Exports = new List<PackExport>
            {
                new PackExport { ExportKind = "standard", RelativePath = "assets/templates.json", ExportId = "templates" }
            }
        });
        Directory.CreateDirectory(Path.Combine(_root, "packs", "standards", "core", "assets"));
        File.WriteAllText(Path.Combine(_root, "packs", "standards", "core", "assets", "templates.json"), "{\"view_templates\":{\"architectural_plan\":\"BIM765T_Arch_Plan_v2\"}}");
        WriteWorkspaceManifest();

        var packs = new PackCatalogService(PackCatalogService.LoadAll(_root));
        var workspaces = new WorkspaceCatalogService(WorkspaceCatalogService.LoadAll(_root));

        Assert.True(packs.TryGet("bim765t.standards.core", out var pack));
        Assert.Equal("standards-pack", pack.Manifest.PackType);
        Assert.Equal("default", workspaces.GetManifest("default").Workspace.WorkspaceId);
        Assert.Contains("bim765t.standards.core", workspaces.GetManifest("default").Workspace.EnabledPacks);
    }

    [Fact]
    public void StandardsCatalogService_Resolves_Requested_Keys_From_Enabled_Packs()
    {
        CreateRepoSkeleton();
        WritePackManifest("packs/standards/core/pack.json", new PackManifest
        {
            PackType = "standards-pack",
            PackId = "bim765t.standards.core",
            DisplayName = "Core standards",
            Exports = new List<PackExport>
            {
                new PackExport { ExportKind = "standard", RelativePath = "assets/templates.json", ExportId = "templates" },
                new PackExport { ExportKind = "standard", RelativePath = "assets/qc_rules.json", ExportId = "qc_rules" }
            }
        });
        Directory.CreateDirectory(Path.Combine(_root, "packs", "standards", "core", "assets"));
        File.WriteAllText(Path.Combine(_root, "packs", "standards", "core", "assets", "templates.json"), "{\"view_templates\":{\"architectural_plan\":\"BIM765T_Arch_Plan_v2\"}}", System.Text.Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "packs", "standards", "core", "assets", "qc_rules.json"), "{\"sheet_qc\":{\"views_placed\":{\"min\":1}}}", System.Text.Encoding.UTF8);
        WriteWorkspaceManifest();

        var packs = new PackCatalogService(PackCatalogService.LoadAll(_root));
        var workspaces = new WorkspaceCatalogService(WorkspaceCatalogService.LoadAll(_root));
        var standards = new StandardsCatalogService(packs, workspaces);

        var resolved = standards.Resolve(new StandardsResolutionRequest
        {
            WorkspaceId = "default",
            StandardKind = "sheet",
            RequestedKeys = new List<string>
            {
                "templates.json#view_templates.architectural_plan",
                "qc_rules.json#sheet_qc.views_placed.min"
            }
        });

        Assert.Equal("default", resolved.WorkspaceId);
        Assert.Equal(2, resolved.Values.Count);
        Assert.Contains(resolved.Values, x => x.Matched && x.Value.Contains("BIM765T_Arch_Plan_v2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved.Values, x => x.Matched && x.Value == "1");
    }

    [Fact]
    public void PlaybookOrchestrationService_Matches_And_Previews_Workspace_Playbook()
    {
        CreateRepoSkeleton();
        WritePackManifest("packs/standards/core/pack.json", new PackManifest
        {
            PackType = "standards-pack",
            PackId = "bim765t.standards.core",
            DisplayName = "Core standards",
            Exports = new List<PackExport>
            {
                new PackExport { ExportKind = "standard", RelativePath = "assets/naming.json", ExportId = "naming" },
                new PackExport { ExportKind = "standard", RelativePath = "assets/templates.json", ExportId = "templates" }
            }
        });
        Directory.CreateDirectory(Path.Combine(_root, "packs", "standards", "core", "assets"));
        File.WriteAllText(Path.Combine(_root, "packs", "standards", "core", "assets", "naming.json"), "{\"sheet\":{\"architectural\":{\"format\":\"A{floor:02d}{seq:02d}\"}}}", System.Text.Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "packs", "standards", "core", "assets", "templates.json"), "{\"view_templates\":{\"architectural_plan\":\"BIM765T_Arch_Plan_v2\"}}", System.Text.Encoding.UTF8);

        WritePackManifest("packs/playbooks/core/pack.json", new PackManifest
        {
            PackType = "playbook-pack",
            PackId = "bim765t.playbooks.core",
            DisplayName = "Core playbooks",
            Exports = new List<PackExport>
            {
                new PackExport { ExportKind = "playbook", RelativePath = "assets/sheet_create_arch_package.v1.json", ExportId = "sheet_create_arch_package.v1" }
            }
        });
        Directory.CreateDirectory(Path.Combine(_root, "packs", "playbooks", "core", "assets"));
        File.WriteAllText(
            Path.Combine(_root, "packs", "playbooks", "core", "assets", "sheet_create_arch_package.v1.json"),
            JsonUtil.Serialize(new PlaybookDefinition
            {
                PlaybookId = "sheet_create_arch_package.v1",
                Description = "Create arch sheet package.",
                RequiredContext = "project",
                PackId = "bim765t.playbooks.core",
                TriggerPhrases = new List<string> { "tao sheet a", "create sheet a" },
                StandardsRefs = new List<string> { "naming.json#sheet.architectural.format", "templates.json#view_templates.architectural_plan" },
                RequiredInputs = new List<string> { "sheet_name", "levels" },
                Steps = new List<PlaybookStepDefinition>
                {
                    new PlaybookStepDefinition { StepName = "Resolve workspace", Tool = ToolNames.WorkspaceGetManifest, StepId = "workspace", StepKind = "context" },
                    new PlaybookStepDefinition { StepName = "Resolve standards", Tool = ToolNames.StandardsResolve, StepId = "standards", StepKind = "standards", OutputKey = "standards" },
                    new PlaybookStepDefinition { StepName = "Create sheet", Tool = ToolNames.SheetCreateSafe, StepId = "create_sheet", StepKind = "tool" }
                }
            }),
            System.Text.Encoding.UTF8);
        WriteWorkspaceManifest();

        var loader = new PlaybookLoaderService(PlaybookLoaderService.LoadAll(_root));
        var packs = new PackCatalogService(PackCatalogService.LoadAll(_root));
        var workspaces = new WorkspaceCatalogService(WorkspaceCatalogService.LoadAll(_root));
        var standards = new StandardsCatalogService(packs, workspaces);
        var orchestration = new PlaybookOrchestrationService(loader, packs, workspaces, standards);
        var manifests = new[]
        {
            new ToolManifest { ToolName = ToolNames.WorkspaceGetManifest },
            new ToolManifest { ToolName = ToolNames.StandardsResolve },
            new ToolManifest { ToolName = ToolNames.SheetCreateSafe }
        };

        var match = orchestration.Match(manifests, new PlaybookMatchRequest
        {
            WorkspaceId = "default",
            Query = "tao sheet A tang 1-5",
            DocumentContext = "project",
            MaxResults = 3
        });
        var preview = orchestration.Preview(manifests, new PlaybookPreviewRequest
        {
            WorkspaceId = "default",
            PlaybookId = "sheet_create_arch_package.v1",
            Query = "tao sheet A tang 1-5",
            DocumentContext = "project"
        });

        Assert.Equal("sheet_create_arch_package.v1", match.RecommendedPlaybook.PlaybookId);
        Assert.Equal("bim765t.playbooks.core", match.RecommendedPlaybook.PackId);
        Assert.NotEmpty(preview.Steps);
        Assert.Contains(preview.Standards.Values, x => x.Matched);
        Assert.Contains("sheet_create_arch_package.v1", preview.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private void CreateRepoSkeleton()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "BIM765T.Revit.Agent.sln"), string.Empty);
    }

    private void WritePackManifest(string relativePath, PackManifest manifest)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonUtil.Serialize(manifest), System.Text.Encoding.UTF8);
    }

    private void WriteWorkspaceManifest()
    {
        var fullPath = Path.Combine(_root, "workspaces", "default", "workspace.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonUtil.Serialize(new WorkspaceManifest
        {
            WorkspaceId = "default",
            DisplayName = "Default",
            EnabledPacks = new List<string> { "bim765t.standards.core", "bim765t.playbooks.core" },
            PreferredStandardsPacks = new List<string> { "bim765t.standards.core" },
            PreferredPlaybookPacks = new List<string> { "bim765t.playbooks.core" },
            AllowedAgents = new List<string> { "bim765t.agents.orchestrator" },
            AllowedSpecialists = new List<string> { "bim765t.agents.specialist.sheet" }
        }), System.Text.Encoding.UTF8);
    }
}
