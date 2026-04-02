using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using Xunit;

namespace BIM765T.Revit.Architecture.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Contracts_Assembly_Does_Not_Reference_Agent_Or_Revit()
    {
        var assembly = LoadAssembly("BIM765T.Revit.Contracts.dll");
        var references = assembly.GetReferencedAssemblies().Select(x => x.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(references, x => x.StartsWith("BIM765T.Revit.Agent", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, x => x.StartsWith("Autodesk.Revit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContractsProto_Assembly_Does_Not_Reference_Agent_Or_Revit()
    {
        var assembly = LoadAssembly("BIM765T.Revit.Contracts.Proto.dll");
        var references = assembly.GetReferencedAssemblies().Select(x => x.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(references, x => x.StartsWith("BIM765T.Revit.Agent", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, x => x.StartsWith("Autodesk.Revit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CopilotCore_Does_Not_Reference_VendorAi_Or_Onnx()
    {
        var assembly = LoadAssembly("BIM765T.Revit.Copilot.Core.dll");
        var references = assembly.GetReferencedAssemblies().Select(x => x.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(references, x => x.IndexOf("Anthropic", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(references, x => x.IndexOf("OpenAI", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(references, x => x.IndexOf("OnnxRuntime", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(references, x => x.IndexOf("Microsoft.ML", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Theory]
    [InlineData("BIM765T.Revit.Agent.Core.dll")]
    [InlineData("BIM765T.Revit.Bridge.dll")]
    [InlineData("BIM765T.Revit.Copilot.Core.dll")]
    [InlineData("BIM765T.Revit.McpHost.dll")]
    [InlineData("BIM765T.Revit.WorkerHost.dll")]
    [InlineData("BIM765T.Revit.Contracts.Proto.dll")]
    public void RunnerCapable_Assemblies_Do_Not_Reference_RevitApi(string assemblyFileName)
    {
        var assembly = LoadAssembly(assemblyFileName);
        var references = assembly.GetReferencedAssemblies().Select(x => x.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(references, x => x.StartsWith("Autodesk.Revit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contracts_Project_Does_Not_ProjectReference_Agent()
    {
        var repoRoot = FindRepoRoot();
        var doc = XDocument.Load(Path.Combine(repoRoot, "src", "BIM765T.Revit.Contracts", "BIM765T.Revit.Contracts.csproj"));
        var projectReferences = doc.Descendants().Where(x => x.Name.LocalName == "ProjectReference").Select(x => (string?)x.Attribute("Include") ?? string.Empty).ToList();

        Assert.DoesNotContain(projectReferences, x => x.IndexOf("BIM765T.Revit.Agent", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void ToolModules_Stay_At_Registration_And_Orchestration_Level()
    {
        var repoRoot = FindRepoRoot();
        var bridgeServices = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Services", "Bridge");
        var moduleFiles = Directory.GetFiles(bridgeServices, "*ToolModule.cs", SearchOption.TopDirectoryOnly)
            .Where(x => !string.Equals(Path.GetFileName(x), "IToolModule.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(moduleFiles);

        foreach (var file in moduleFiles)
        {
            var content = File.ReadAllText(file);
            Assert.True(
                content.Contains("registry.Register(", StringComparison.Ordinal)
                || content.Contains("registry.RegisterMutationTool(", StringComparison.Ordinal),
                Path.GetFileName(file) + " should register tools explicitly.");

            Assert.DoesNotContain("new Transaction(", content, StringComparison.Ordinal);
            Assert.DoesNotContain("SubTransaction(", content, StringComparison.Ordinal);
            Assert.DoesNotContain("EnsureInTransaction(", content, StringComparison.Ordinal);
            Assert.DoesNotContain("TransactionManager.Instance", content, StringComparison.Ordinal);
            Assert.DoesNotContain("FilteredElementCollector(", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductRepo_Does_Not_Ship_Internal_Guidance_Files()
    {
        var repoRoot = FindRepoRoot();
        var disallowedPaths = new[]
        {
            "AGENTS.md",
            "ASSISTANT.md",
            "CLAUDE.md",
            ".assistant",
            ".claude",
            "docs/agent",
            "docs/architecture",
            "docs/ba",
            "docs/archive",
            "docs/assets",
            "docs/assistant",
            "docs/765T_BLUEPRINT.md",
            "docs/765T_CRITICAL_REVIEW.md",
            "docs/765T_PRODUCT_VISION.md",
            "docs/765T_SYSTEM_DIAGRAMS.md",
            "docs/765T_TECHNICAL_RESEARCH.md",
            "docs/765T_TOOL_LIBRARY_BLUEPRINT.md",
            "docs/ARCHITECTURE.md",
            "docs/PATTERNS.md",
            "docs/INDEX.md",
            "docs/PRODUCT_REVIEW.md",
            "README.BIM765T.Revit.Agent.md"
        };

        foreach (var relativePath in disallowedPaths)
        {
            var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.False(File.Exists(fullPath) || Directory.Exists(fullPath), $"Internal product-external path should not ship: {relativePath}");
        }
    }

    [Fact]
    public void ProductDocs_Stay_In_Whitelisted_Categories()
    {
        var repoRoot = FindRepoRoot();
        var docsRoot = Path.Combine(repoRoot, "docs");
        var allowedTopLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "integration",
            "reference",
            "troubleshooting",
            "release"
        };

        Assert.True(File.Exists(Path.Combine(docsRoot, "README.md")));

        foreach (var file in Directory.GetFiles(docsRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(docsRoot, file).Replace('\\', '/');
            if (string.Equals(relative, "README.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var topLevel = relative.Split('/')[0];
            Assert.Contains(topLevel, allowedTopLevel);
        }
    }

    [Fact]
    public void ProductDocs_Do_Not_Contain_Internal_Paths_Or_Build_Workflow()
    {
        var repoRoot = FindRepoRoot();
        var files = new List<string>
        {
            Path.Combine(repoRoot, "README.md"),
            Path.Combine(repoRoot, "README.en.md"),
            Path.Combine(repoRoot, "tools", "README.md")
        };
        files.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "docs"), "*.md", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(Path.Combine(repoRoot, "packs", "agents", "external-broker", "assets"), "*.md", SearchOption.AllDirectories));

        var bannedPatterns = new[]
        {
            "AGENTS.md",
            "ASSISTANT.md",
            "CLAUDE.md",
            "docs/assistant/",
            "docs/ARCHITECTURE.md",
            "docs/PATTERNS.md",
            "docs/INDEX.md",
            "git clone",
            "dotnet build",
            ".assistant/context/_session_state.json"
        };

        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var content = File.ReadAllText(file);
            foreach (var pattern in bannedPatterns)
            {
                Assert.DoesNotContain(pattern, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ProductExamples_Do_Not_Contain_AbsoluteMachinePaths_Or_Secrets()
    {
        var repoRoot = FindRepoRoot();
        var exampleFiles = Directory.GetFiles(Path.Combine(repoRoot, "docs", "integration", "examples"), "*", SearchOption.AllDirectories);
        Assert.NotEmpty(exampleFiles);

        var bannedPatterns = new[]
        {
            @"C:\Users\",
            @"D:\",
            "sk-",
            "GITHUB_PERSONAL_ACCESS_TOKEN",
            "<REPLACE_WITH_"
        };

        foreach (var file in exampleFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var pattern in bannedPatterns)
            {
                Assert.DoesNotContain(pattern, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Root_Mcp_Config_Is_Not_Shipped_But_Product_Example_Exists()
    {
        var repoRoot = FindRepoRoot();
        Assert.False(File.Exists(Path.Combine(repoRoot, ".mcp.json")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "integration", "examples", "mcp.json.example")));
    }

    [Fact]
    public void Monorepo_Packs_And_Default_Workspace_Exist_With_Valid_Manifests()
    {
        var repoRoot = FindRepoRoot();
        var packsRoot = Path.Combine(repoRoot, "packs");
        var workspacePath = Path.Combine(repoRoot, "workspaces", "default", "workspace.json");
        var catalogRoot = Path.Combine(repoRoot, "catalog");
        var distRoot = Path.Combine(repoRoot, "dist");
        var exportScript = Path.Combine(repoRoot, "tools", "dev", "export-pack-catalog.ps1");

        Assert.True(Directory.Exists(packsRoot), "Missing packs/ root.");
        Assert.True(File.Exists(workspacePath), "Missing default workspace manifest.");
        Assert.True(Directory.Exists(catalogRoot), "Missing catalog/ root.");
        Assert.True(Directory.Exists(distRoot), "Missing dist/ root.");
        Assert.True(File.Exists(exportScript), "Missing pack catalog export script.");

        var packFiles = Directory.GetFiles(packsRoot, "pack.json", SearchOption.AllDirectories);
        Assert.True(packFiles.Length >= 6, "Expected monorepo pack manifests.");
        foreach (var file in packFiles)
        {
            var manifest = JsonUtil.DeserializeRequired<PackManifest>(File.ReadAllText(file));
            Assert.False(string.IsNullOrWhiteSpace(manifest.PackId));
            Assert.False(string.IsNullOrWhiteSpace(manifest.PackType));
        }

        var workspace = JsonUtil.DeserializeRequired<WorkspaceManifest>(File.ReadAllText(workspacePath));
        Assert.Equal("default", workspace.WorkspaceId);
        Assert.Contains("bim765t.agents.orchestrator", workspace.EnabledPacks);
        Assert.Contains("bim765t.playbooks.core", workspace.PreferredPlaybookPacks);
    }

    [Fact]
    public void PipeServerHostedService_Uses_Injected_Request_Processing_Dependencies()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Infrastructure", "Bridge", "PipeServerHostedService.cs");
        var content = File.ReadAllText(file);

        Assert.DoesNotContain("new PipeRequestProcessor(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("new WindowsPipeCallerAuthorizer(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("new ExternalEventPipeRequestScheduler(", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRegistry_Delegates_Mutation_Orchestration_To_Dedicated_Pipeline()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Services", "Bridge", "ToolRegistry.cs");
        var content = File.ReadAllText(file);

        Assert.Contains("_mutationPipeline.BuildHandler", content, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchesExpectedContextStrict", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateApprovalRequest", content, StringComparison.Ordinal);
        Assert.DoesNotContain("FinalizePreviewResult", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformServices_Uses_Injected_Platform_Abstractions()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Services", "Platform", "PlatformServices.cs");
        var content = File.ReadAllText(file);

        Assert.Contains("IDocumentResolver", content, StringComparison.Ordinal);
        Assert.Contains("IApprovalGate", content, StringComparison.Ordinal);
        Assert.Contains("ISnapshotService", content, StringComparison.Ordinal);
        Assert.DoesNotContain("new ApprovalService(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("new SnapshotService(", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerTab_Stays_At_Ui_Orchestration_Level()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "UI", "Tabs", "WorkerTab.cs");
        var content = File.ReadAllText(file);

        Assert.DoesNotContain("Autodesk.Revit", content, StringComparison.Ordinal);
        Assert.DoesNotContain("UIApplication", content, StringComparison.Ordinal);
        Assert.DoesNotContain("UIDocument", content, StringComparison.Ordinal);
        Assert.DoesNotContain("FilteredElementCollector", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction(", content, StringComparison.Ordinal);
        Assert.Contains("WorkerHostMissionClient", content, StringComparison.Ordinal);
        Assert.Contains("ChatTimelineProjector", content, StringComparison.Ordinal);
        Assert.DoesNotContain("InternalToolClient.Instance.CallWithCallback", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolNames.WorkerMessage", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentHost_Uses_Private_KernelPipeServer_As_ControlPlane_Boundary()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Infrastructure", "AgentHost.cs");
        var content = File.ReadAllText(file);

        Assert.Contains("new KernelPipeHostedService(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("new PipeServerHostedService(", content, StringComparison.Ordinal);
        Assert.Contains("kernelPipeServer.Start();", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Bridge_And_McpHost_Default_To_WorkerHost_PublicPipe()
    {
        var repoRoot = FindRepoRoot();
        var bridgeProgram = File.ReadAllText(Path.Combine(repoRoot, "src", "BIM765T.Revit.Bridge", "Program.cs"));
        var mcpProgram = File.ReadAllText(Path.Combine(repoRoot, "src", "BIM765T.Revit.McpHost", "Program.cs"));

        Assert.Contains("BridgeConstants.DefaultWorkerHostPipeName", bridgeProgram, StringComparison.Ordinal);
        Assert.Contains("BridgeConstants.DefaultWorkerHostPipeName", mcpProgram, StringComparison.Ordinal);
        Assert.Contains("CompatibilityService.CompatibilityServiceClient", bridgeProgram, StringComparison.Ordinal);
        Assert.Contains("CompatibilityService.CompatibilityServiceClient", mcpProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("BridgeConstants.DefaultKernelPipeName", mcpProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("BridgeConstants.DefaultPipeName", mcpProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybookLoader_Uses_ProductOwned_Pack_Assets()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Copilot.Core", "PlaybookLoaderService.cs");
        var content = File.ReadAllText(file);

        Assert.Contains("packs/playbooks", content, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/agent/playbooks", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalBrokerAssets_Point_To_Product_Docs()
    {
        var repoRoot = FindRepoRoot();
        var adapter = File.ReadAllText(Path.Combine(repoRoot, "packs", "agents", "external-broker", "assets", "BROKER.adapter.md"));
        var onboard = File.ReadAllText(Path.Combine(repoRoot, "packs", "agents", "external-broker", "assets", "onboard.md"));

        Assert.Contains("docs/README.md", adapter, StringComparison.Ordinal);
        Assert.Contains("docs/reference/mcphost.md", adapter, StringComparison.Ordinal);
        Assert.Contains("docs/integration/quickstart-claude-code.md", onboard, StringComparison.Ordinal);
        Assert.DoesNotContain("AGENTS.md", adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ASSISTANT.md", adapter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Critical_Runtime_Files_Do_Not_Contain_Known_Mojibake_Patterns()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Infrastructure", "Bridge", "PipeRequestProcessor.cs"),
            Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Infrastructure", "Bridge", "PipeServerHostedService.cs"),
            Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Services", "Bridge", "ToolRegistry.cs"),
            Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Services", "Platform", "PlatformServices.cs"),
            Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "Services", "Platform", "GenericMutationServices.cs"),
            Path.Combine(repoRoot, "src", "BIM765T.Revit.Contracts", "Validation", "ToolPayloadValidator.cs")
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("Ãƒ", content, StringComparison.Ordinal);
            Assert.DoesNotContain("?? b?", content, StringComparison.Ordinal);
            Assert.DoesNotContain("kh?ng", content, StringComparison.Ordinal);
            Assert.DoesNotContain("???", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AgentPaneControl_Uses_Dashboard_Shell_And_Not_Legacy_Tab_Navigation()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "UI", "AgentPaneControl.cs");
        var content = File.ReadAllText(file);

        Assert.Contains("DashboardSidebar", content, StringComparison.Ordinal);
        Assert.Contains("PaneTopBar", content, StringComparison.Ordinal);
        Assert.Contains("WorkerTab", content, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickToolTab", content, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceTab", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityTab", content, StringComparison.Ordinal);
        Assert.DoesNotContain("SidebarNav", content, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageBubble_Does_Not_Attach_Fixed_Code_Action_Bar_To_Timeline()
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(repoRoot, "src", "BIM765T.Revit.Agent", "UI", "Components", "MessageBubble.cs");
        var content = File.ReadAllText(file);

        Assert.DoesNotContain("contentStack.Children.Add(BuildActionBar", content, StringComparison.Ordinal);
    }

    private static Assembly LoadAssembly(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(path), "Assembly not found in test output: " + path);
        return Assembly.LoadFrom(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BIM765T.Revit.Agent.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repo root not found from test output path.");
    }
}
