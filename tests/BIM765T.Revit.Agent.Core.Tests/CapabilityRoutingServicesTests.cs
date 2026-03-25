using System;
using System.Collections.Generic;
using System.IO;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class CapabilityRoutingServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BIM765T-CapabilityRoutingTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Compiler_Combines_Playbook_Policy_And_Specialists()
    {
        CreateRepoSkeleton();
        WriteStandardPack();
        WritePlaybookPack();
        WriteSpecialistPack();
        WriteWorkspaceManifest();

        var manifests = new[]
        {
            new ToolManifest
            {
                ToolName = ToolNames.PolicyResolve,
                CapabilityDomain = CapabilityDomains.Coordination,
                DeterminismLevel = ToolDeterminismLevels.PolicyBacked,
                VerificationMode = ToolVerificationModes.GeometryCheck,
                SupportedDisciplines = new List<string> { CapabilityDisciplines.Mep },
                IssueKinds = new List<string> { CapabilityIssueKinds.HardClash }
            },
            new ToolManifest
            {
                ToolName = ToolNames.SpatialClashDetect,
                CapabilityDomain = CapabilityDomains.Coordination,
                DeterminismLevel = ToolDeterminismLevels.PolicyBacked,
                VerificationMode = ToolVerificationModes.GeometryCheck,
                SupportedDisciplines = new List<string> { CapabilityDisciplines.Mep, CapabilityDisciplines.Structure },
                IssueKinds = new List<string> { CapabilityIssueKinds.HardClash }
            },
            new ToolManifest
            {
                ToolName = ToolNames.IntentCompile,
                CapabilityDomain = CapabilityDomains.Intent,
                DeterminismLevel = ToolDeterminismLevels.PolicyBacked,
                VerificationMode = ToolVerificationModes.PolicyCheck,
                IssueKinds = new List<string> { CapabilityIssueKinds.IntentCompile }
            }
        };

        var packs = new PackCatalogService(PackCatalogService.LoadAll(_root));
        var workspaces = new WorkspaceCatalogService(WorkspaceCatalogService.LoadAll(_root));
        var standards = new StandardsCatalogService(packs, workspaces);
        var playbooks = new PlaybookOrchestrationService(new PlaybookLoaderService(PlaybookLoaderService.LoadAll(_root)), packs, workspaces, standards);
        var policies = new PolicyResolutionService(packs, workspaces);
        var specialists = new SpecialistRegistryService(packs, workspaces);
        var compiler = new CapabilityTaskCompilerService(new ToolCapabilitySearchService(), playbooks, policies, specialists);

        var compiled = compiler.Compile(manifests, new IntentCompileRequest
        {
            PreferredCapabilityDomain = CapabilityDomains.Coordination,
            Discipline = CapabilityDisciplines.Mep,
            Task = new IntentTask
            {
                Query = "resolve hard clash between pipe and beam",
                WorkspaceId = "default",
                DocumentContext = "project",
                IssueKinds = new List<string> { CapabilityIssueKinds.HardClash }
            }
        });

        Assert.Equal(CapabilityDomains.Coordination, compiled.CapabilityDomain);
        Assert.Equal("coordination_clash_resolution.v1", compiled.RecommendedPlaybook.PlaybookId);
        Assert.Contains("bim765t.standards.mep.clearance", compiled.PolicyResolution.ResolvedPackIds);
        Assert.Contains(ToolNames.SpatialClashDetect, compiled.CandidateToolNames);
        Assert.Single(compiled.RecommendedSpecialists);
        Assert.Equal("bim765t.agents.specialist.coordination", compiled.RecommendedSpecialists[0].PackId);
    }

    private void CreateRepoSkeleton()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "BIM765T.Revit.Agent.sln"), string.Empty);
    }

    private void WriteStandardPack()
    {
        WriteJson("packs/standards/mep-clearance/pack.json", new PackManifest
        {
            PackType = "standards-pack",
            PackId = "bim765t.standards.mep.clearance",
            DisplayName = "MEP Clearance",
            CapabilityDomains = new List<string> { CapabilityDomains.Coordination },
            SupportedDisciplines = new List<string> { CapabilityDisciplines.Mep, CapabilityDisciplines.Structure },
            IssueKinds = new List<string> { CapabilityIssueKinds.HardClash },
            VerificationModes = new List<string> { ToolVerificationModes.GeometryCheck },
            Exports = new List<PackExport>
            {
                new PackExport { ExportKind = "standard", RelativePath = "assets/clearance.json", ExportId = "clearance" }
            }
        });
        Directory.CreateDirectory(Path.Combine(_root, "packs", "standards", "mep-clearance", "assets"));
        File.WriteAllText(Path.Combine(_root, "packs", "standards", "mep-clearance", "assets", "clearance.json"), "{\"clearance\":{\"AHU_front_mm\":1200}}");
    }

    private void WritePlaybookPack()
    {
        WriteJson("packs/playbooks/coordination/pack.json", new PackManifest
        {
            PackType = "playbook-pack",
            PackId = "bim765t.playbooks.coordination",
            DisplayName = "Coordination playbooks",
            CapabilityDomains = new List<string> { CapabilityDomains.Coordination },
            SupportedDisciplines = new List<string> { CapabilityDisciplines.Mep },
            IssueKinds = new List<string> { CapabilityIssueKinds.HardClash },
            VerificationModes = new List<string> { ToolVerificationModes.GeometryCheck },
            Exports = new List<PackExport>
            {
                new PackExport { ExportKind = "playbook", RelativePath = "assets/coordination_clash_resolution.v1.json", ExportId = "coordination_clash_resolution.v1" }
            }
        });
        Directory.CreateDirectory(Path.Combine(_root, "packs", "playbooks", "coordination", "assets"));
        File.WriteAllText(
            Path.Combine(_root, "packs", "playbooks", "coordination", "assets", "coordination_clash_resolution.v1.json"),
            JsonUtil.Serialize(new PlaybookDefinition
            {
                PlaybookId = "coordination_clash_resolution.v1",
                Description = "Resolve hard clash.",
                RequiredContext = "project",
                PackId = "bim765t.playbooks.coordination",
                TriggerPhrases = new List<string> { "hard clash", "resolve clash" },
                RecommendedSpecialists = new List<string> { "bim765t.agents.specialist.coordination" },
                CapabilityDomain = CapabilityDomains.Coordination,
                DeterminismLevel = ToolDeterminismLevels.PolicyBacked,
                VerificationMode = ToolVerificationModes.GeometryCheck,
                SupportedDisciplines = new List<string> { CapabilityDisciplines.Mep },
                IssueKinds = new List<string> { CapabilityIssueKinds.HardClash },
                PolicyPackIds = new List<string> { "bim765t.standards.mep.clearance" },
                Steps = new List<PlaybookStepDefinition>
                {
                    new PlaybookStepDefinition { StepName = "Policy", Tool = ToolNames.PolicyResolve, StepId = "policy", StepKind = "policy" },
                    new PlaybookStepDefinition { StepName = "Detect", Tool = ToolNames.SpatialClashDetect, StepId = "detect", StepKind = "qc" }
                }
            }));
    }

    private void WriteSpecialistPack()
    {
        WriteJson("packs/agents/specialists/coordination/pack.json", new PackManifest
        {
            PackType = "agent-pack",
            PackId = "bim765t.agents.specialist.coordination",
            DisplayName = "Coordination Specialist",
            Description = "Coordination specialist",
            CapabilityDomains = new List<string> { CapabilityDomains.Coordination },
            SupportedDisciplines = new List<string> { CapabilityDisciplines.Mep },
            IssueKinds = new List<string> { CapabilityIssueKinds.HardClash },
            VerificationModes = new List<string> { ToolVerificationModes.GeometryCheck },
            Exports = new List<PackExport>
            {
                new PackExport { ExportKind = "agent", RelativePath = "assets/README.md", ExportId = "coordination-specialist" }
            }
        });
        Directory.CreateDirectory(Path.Combine(_root, "packs", "agents", "specialists", "coordination", "assets"));
        File.WriteAllText(Path.Combine(_root, "packs", "agents", "specialists", "coordination", "assets", "README.md"), "# Coordination Specialist");
    }

    private void WriteWorkspaceManifest()
    {
        WriteJson("workspaces/default/workspace.json", new WorkspaceManifest
        {
            WorkspaceId = "default",
            DisplayName = "Default",
            EnabledPacks = new List<string>
            {
                "bim765t.standards.mep.clearance",
                "bim765t.playbooks.coordination",
                "bim765t.agents.specialist.coordination"
            },
            PreferredStandardsPacks = new List<string> { "bim765t.standards.mep.clearance" },
            PreferredPlaybookPacks = new List<string> { "bim765t.playbooks.coordination" },
            AllowedAgents = new List<string> { "bim765t.agents.orchestrator" },
            AllowedSpecialists = new List<string> { "bim765t.agents.specialist.coordination" }
        });
    }

    private void WriteJson<T>(string relativePath, T value)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonUtil.Serialize(value));
    }
}
