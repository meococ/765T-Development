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

public sealed class PlaybookAndKnowledgeServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BIM765T-PlaybookKnowledgeTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void PlaybookLoaderService_Recommends_Best_Playbook_And_Alternatives()
    {
        var service = new PlaybookLoaderService(new[]
        {
            new PlaybookDefinition
            {
                PlaybookId = "sheet_setup_packet",
                Description = "Set up sheet packet for documentation export.",
                RequiredContext = "project",
                DecisionGate = new PlaybookDecisionGate { UseWhen = new List<string> { "sheet setup export" } },
                Steps = new List<PlaybookStepDefinition>
                {
                    new PlaybookStepDefinition { StepName = "Create sheet", Tool = ToolNames.SheetCreateSafe, Purpose = "Create sheets" }
                }
            },
            new PlaybookDefinition
            {
                PlaybookId = "qc_summary",
                Description = "Build QC review summary for sheet export and warnings.",
                RequiredContext = "project",
                DecisionGate = new PlaybookDecisionGate { UseWhen = new List<string> { "sheet export review" } },
                Steps = new List<PlaybookStepDefinition>
                {
                    new PlaybookStepDefinition { StepName = "Run QC", Tool = ToolNames.ReviewSmartQc, Purpose = "QC" }
                }
            }
        });

        var recommendation = service.Recommend(
            "Need sheet setup and export packet for project issue",
            "project",
            new[] { ToolNames.SheetCreateSafe, ToolNames.ExportIfcSafe });

        Assert.Equal("sheet_setup_packet", recommendation.PlaybookId);
        Assert.True(recommendation.Confidence > 0);
        Assert.Single(recommendation.Steps);
        Assert.Contains("qc_summary", recommendation.AlternativePlaybooks);
        Assert.True(service.TryGet("sheet_setup_packet", out var playbook));
        Assert.Equal("sheet_setup_packet", playbook.PlaybookId);
        Assert.Equal(2, service.GetAll().Count);
    }

    [Fact]
    public void PlaybookLoaderService_LoadAll_Loads_Valid_Repo_Playbooks_And_Skips_Invalid()
    {
        var repoRoot = CreateRepoSkeleton();
        var playbookDir = Path.Combine(repoRoot, "docs", "agent", "playbooks");
        Directory.CreateDirectory(playbookDir);

        File.WriteAllText(Path.Combine(playbookDir, "valid.json"), JsonUtil.Serialize(new PlaybookDefinition
        {
            PlaybookId = "delivery_packet",
            Description = "Delivery packet",
            Steps = new List<PlaybookStepDefinition>
            {
                new PlaybookStepDefinition { StepName = "Export", Tool = ToolNames.ExportIfcSafe, Purpose = "Export IFC" }
            }
        }));
        File.WriteAllText(Path.Combine(playbookDir, "broken.json"), "{not-json");

        var dirs = PlaybookLoaderService.ResolveCandidateDirectories(repoRoot);
        var loaded = PlaybookLoaderService.LoadAll(repoRoot);

        Assert.Contains(dirs, x => x.EndsWith(Path.Combine("docs", "agent", "playbooks"), StringComparison.OrdinalIgnoreCase));
        Assert.Single(loaded);
        Assert.Equal("delivery_packet", loaded[0].PlaybookId);
    }

    [Fact]
    public void KnowledgeUpdateService_Updates_ProjectMemory_And_Appends_Lesson()
    {
        var repoRoot = CreateRepoSkeleton();
        var docsAgent = Path.Combine(repoRoot, "docs", "agent");
        Directory.CreateDirectory(docsAgent);
        File.WriteAllText(Path.Combine(docsAgent, "PROJECT_MEMORY.md"), "# Project Memory");
        File.WriteAllText(Path.Combine(docsAgent, "LESSONS_LEARNED.md"), "# Lessons");
        Directory.CreateDirectory(Path.Combine(docsAgent, "skills", "tool-intelligence"));
        File.WriteAllText(Path.Combine(docsAgent, "skills", "tool-intelligence", "TOOL_GRAPH.overlay.json"), JsonUtil.Serialize(new ToolGraphOverlayCatalog()));

        var service = new KnowledgeUpdateService(repoRoot);
        var update = service.UpdateProjectMemory("Worker Product v1", "- one worker shell", "phase-a");
        var duplicate = service.UpdateProjectMemory("Worker Product v1", "- one worker shell", "phase-a");
        var lesson = service.AppendLesson("Task card drift", "Prompt too wide", "Narrow scope", "Use Task Card");

        Assert.True(update.Updated);
        Assert.False(duplicate.Updated);
        Assert.True(lesson.Updated);
        Assert.Contains("Worker Product v1", File.ReadAllText(Path.Combine(docsAgent, "PROJECT_MEMORY.md")));
        Assert.Contains("Task card drift", File.ReadAllText(Path.Combine(docsAgent, "LESSONS_LEARNED.md")));
    }

    [Fact]
    public void KnowledgeUpdateService_Updates_ToolGraphOverlay_Idempotently()
    {
        var repoRoot = CreateRepoSkeleton();
        var overlayDir = Path.Combine(repoRoot, "docs", "agent", "skills", "tool-intelligence");
        Directory.CreateDirectory(overlayDir);
        Directory.CreateDirectory(Path.Combine(repoRoot, "docs", "agent"));
        File.WriteAllText(Path.Combine(repoRoot, "docs", "agent", "PROJECT_MEMORY.md"), "# Project Memory");
        File.WriteAllText(Path.Combine(repoRoot, "docs", "agent", "LESSONS_LEARNED.md"), "# Lessons");
        File.WriteAllText(Path.Combine(overlayDir, "TOOL_GRAPH.overlay.json"), JsonUtil.Serialize(new ToolGraphOverlayCatalog()));

        var service = new KnowledgeUpdateService(repoRoot);
        var add = service.UpdateToolGraphOverlay(new ToolGraphOverlayEntry
        {
            ToolName = ToolNames.TaskPlan,
            Prerequisites = new List<string> { ToolNames.ContextGetHotState },
            FollowUps = new List<string> { ToolNames.TaskPreview }
        });
        var duplicate = service.UpdateToolGraphOverlay(new ToolGraphOverlayEntry
        {
            ToolName = ToolNames.TaskPlan
        });

        Assert.True(add.Updated);
        Assert.False(duplicate.Updated);

        var catalog = JsonUtil.DeserializeRequired<ToolGraphOverlayCatalog>(File.ReadAllText(Path.Combine(overlayDir, "TOOL_GRAPH.overlay.json")));
        Assert.Single(catalog.Entries);
        Assert.Equal(ToolNames.TaskPlan, catalog.Entries[0].ToolName);
    }

    private string CreateRepoSkeleton()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "BIM765T.Revit.Agent.sln"), string.Empty);
        return _root;
    }
}
