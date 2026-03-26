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
    public void Tool_Graph_Overlay_Only_References_Existing_Tools()
    {
        var repoRoot = FindRepoRoot();
        var overlayPath = Path.Combine(repoRoot, "docs", "agent", "skills", "tool-intelligence", "TOOL_GRAPH.overlay.json");
        Assert.True(File.Exists(overlayPath), "Missing overlay file: " + overlayPath);

        var catalog = JsonUtil.DeserializeRequired<ToolGraphOverlayCatalog>(File.ReadAllText(overlayPath));
        var knownTools = typeof(ToolNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(x => x.FieldType == typeof(string))
            .Select(x => (string)x.GetValue(null)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog.Entries)
        {
            Assert.Contains(entry.ToolName, knownTools);
            Assert.All(entry.FollowUps, tool => Assert.Contains(tool, knownTools));
        }
    }

    [Fact]
    public void Task_Templates_Only_Reference_Existing_Tools()
    {
        var repoRoot = FindRepoRoot();
        var templatePath = Path.Combine(repoRoot, "docs", "agent", "skills", "tool-intelligence", "TASK_TEMPLATES.md");
        Assert.True(File.Exists(templatePath), "Missing task template file: " + templatePath);

        var knownTools = typeof(ToolNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(x => x.FieldType == typeof(string))
            .Select(x => (string)x.GetValue(null)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matches = Regex.Matches(File.ReadAllText(templatePath), @"^\s*-\s*tool:\s*(?<tool>[a-z0-9._]+)\s*$", RegexOptions.Multiline);
        Assert.True(matches.Count > 0, "No `- tool:` lines found in task template file.");
        foreach (Match match in matches)
        {
            var toolName = match.Groups["tool"].Value;
            Assert.Contains(toolName, knownTools);
        }
    }

    [Fact]
    public void Canonical_Agent_Docs_Exist_And_Temporary_Patterns_File_Is_Removed()
    {
        var repoRoot = FindRepoRoot();

        Assert.True(File.Exists(Path.Combine(repoRoot, "AGENTS.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "ASSISTANT.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "ARCHITECTURE.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "PATTERNS.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "assistant", "BASELINE.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "assistant", "CONFIG_MATRIX.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "assistant", "SPECIALISTS.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs", "assistant", "USE_CASE_MATRIX.md")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "docs", "PATERNS.tmp")));
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
    public void Instruction_Adapters_Do_Not_Reference_Stale_Context_Path_Or_Handoff()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "README.md"),
            Path.Combine(repoRoot, "ASSISTANT.md"),
            Path.Combine(repoRoot, "AGENTS.md"),
            Path.Combine(repoRoot, ".assistant", "commands", "delegate-external-ai.md"),
            Path.Combine(repoRoot, ".assistant", "commands", "onboard.md"),
            Path.Combine(repoRoot, "docs", "assistant", "BASELINE.md"),
            Path.Combine(repoRoot, "docs", "assistant", "CONFIG_MATRIX.md")
        };

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }
            var content = File.ReadAllText(file);
            Assert.DoesNotContain(".assistant/context/_session_state.json", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ROUND_TASK_HANDOFF.md", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void External_Ai_Delegation_Command_References_Current_Model_Baseline()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, ".assistant", "commands", "delegate-external-ai.md");
        var content = File.ReadAllText(path);

        Assert.Contains("gpt-5.4", content, StringComparison.Ordinal);
        Assert.DoesNotContain("gpt-4.1", content, StringComparison.Ordinal);
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
            Assert.DoesNotContain("Ã", content, StringComparison.Ordinal);
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

    [Fact]
    public void Doc_Index_References_Exist_Filesystem()
    {
        var repoRoot = FindRepoRoot();
        var indexPath = Path.Combine(repoRoot, "docs", "INDEX.md");
        var indexContent = File.ReadAllText(indexPath);

        // Extract file paths from markdown table cells: `path` or "path"
        var matches = Regex.Matches(indexContent, @"(?:[`""'])([^\s`""']+(?:\.md|\.json)[^\s`""']*)(?:[`""'])");
        var missingFiles = new List<string>();

        foreach (Match match in matches)
        {
            var relativePath = match.Groups[1].Value;

            // Skip external URLs, absolute paths, and glob patterns
            if (relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains('*'))
            {
                continue;
            }

            // Convert relative doc paths to filesystem paths
            string fullPath;
            if (relativePath.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (relativePath.StartsWith("archive/", StringComparison.OrdinalIgnoreCase))
            {
                // archive/ paths in docs/INDEX.md refer to docs/archive/
                fullPath = Path.Combine(repoRoot, "docs", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (File.Exists(Path.Combine(repoRoot, relativePath)))
            {
                // File at repo root (e.g. README.md, CLAUDE.md)
                fullPath = Path.Combine(repoRoot, relativePath);
            }
            else if (File.Exists(Path.Combine(repoRoot, "docs", relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                // File in docs/ subdirectory (bare name)
                fullPath = Path.Combine(repoRoot, "docs", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (File.Exists(Path.Combine(repoRoot, "docs", "ba", "phase-0", relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                // BA phase-0 files
                fullPath = Path.Combine(repoRoot, "docs", "ba", "phase-0", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (File.Exists(Path.Combine(repoRoot, "docs", "ba", "phase-1", relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                // BA phase-1 files
                fullPath = Path.Combine(repoRoot, "docs", "ba", "phase-1", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (File.Exists(Path.Combine(repoRoot, "docs", "ba", "phase-2", relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                fullPath = Path.Combine(repoRoot, "docs", "ba", "phase-2", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (File.Exists(Path.Combine(repoRoot, "docs", "ba", "phase-3", relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                fullPath = Path.Combine(repoRoot, "docs", "ba", "phase-3", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (File.Exists(Path.Combine(repoRoot, "docs", "ba", "phase-4", relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                fullPath = Path.Combine(repoRoot, "docs", "ba", "phase-4", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (File.Exists(Path.Combine(repoRoot, "docs", "ba", "phase-5", relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                fullPath = Path.Combine(repoRoot, "docs", "ba", "phase-5", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            else
            {
                // Last resort: try docs/ bare lookup
                fullPath = Path.Combine(repoRoot, "docs", relativePath.Replace('/', Path.DirectorySeparatorChar));
            }

            if (!File.Exists(fullPath))
            {
                missingFiles.Add(relativePath + " (tried: " + fullPath + ")");
            }
        }

        Assert.Empty(missingFiles);
    }

    [Fact]
    public void Read_Order_Consistent_Between_Key_Docs()
    {
        var repoRoot = FindRepoRoot();

        // Read-order should be: README → AGENTS → ASSISTANT → docs/ARCHITECTURE → docs/PATTERNS
        var readOrderFiles = new[]
        {
            "README.md",
            "AGENTS.md",
            "ASSISTANT.md",
            "docs/ARCHITECTURE.md",
            "docs/PATTERNS.md",
            "docs/assistant/BASELINE.md"
        };

        foreach (var file in readOrderFiles)
        {
            var path = Path.Combine(repoRoot, file);
            Assert.True(File.Exists(path), $"Read-order file missing: {file}");
        }

        // AGENTS.md and ASSISTANT.md should both reference docs/ARCHITECTURE.md and docs/PATTERNS.md
        var agentsPath = Path.Combine(repoRoot, "AGENTS.md");
        var assistantPath = Path.Combine(repoRoot, "ASSISTANT.md");

        if (File.Exists(agentsPath))
        {
            var agentsContent = File.ReadAllText(agentsPath);
            Assert.Contains("docs/ARCHITECTURE.md", agentsContent);
            Assert.Contains("docs/PATTERNS.md", agentsContent);
        }

        if (File.Exists(assistantPath))
        {
            var assistantContent = File.ReadAllText(assistantPath);
            Assert.Contains("docs/ARCHITECTURE.md", assistantContent);
        }
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
