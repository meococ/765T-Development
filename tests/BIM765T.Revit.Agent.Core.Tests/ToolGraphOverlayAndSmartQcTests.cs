using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ToolGraphOverlayAndSmartQcTests
{
    [Fact]
    public void ToolGraphOverlayService_LoadDefaultEntries_Finds_Repo_Overlay()
    {
        var entries = ToolGraphOverlayService.LoadDefaultEntries();

        Assert.NotEmpty(entries);
        Assert.Contains(entries, x => x.ToolName == ToolNames.ReviewSmartQc);
        Assert.Contains(entries, x => x.ToolName == ToolNames.DataExtractScheduleStructured);
    }

    [Fact]
    public void SmartQcAggregationService_LoadRuleset_Finds_Repo_Ruleset()
    {
        var service = new SmartQcAggregationService();

        var ruleset = service.LoadRuleset("base-rules", out var resolvedPath);

        Assert.Equal("base-rules", ruleset.Name);
        Assert.Contains("base-rules", resolvedPath);
        Assert.True(ruleset.Rules.Count >= 5);
    }

    [Fact]
    public void ToolGuidanceService_Merges_Curated_Overlay()
    {
        var service = new ToolGuidanceService(
            new ToolCapabilitySearchService(),
            new ToolGraphOverlayService(new[]
            {
                new ToolGraphOverlayEntry
                {
                    ToolName = ToolNames.ElementQuery,
                    Prerequisites = new List<string> { "session.get_task_context" },
                    FollowUps = new List<string> { ToolNames.ElementInspect },
                    AntiPatterns = new List<string> { "Khong query toan model khi chi can selection." },
                    TypicalChains = new List<string> { "element.query -> element.inspect -> parameter.trace" },
                    RecoveryHints = new List<string> { "Thu giam scope query truoc khi retry." },
                    RecommendedTemplates = new List<string> { "MODEL_QC_BASE" }
                }
            }));

        var response = service.Build(new[]
        {
            new ToolManifest
            {
                ToolName = ToolNames.ElementQuery,
                Description = "Query elements.",
                PermissionLevel = PermissionLevel.Read
            },
            new ToolManifest
            {
                ToolName = ToolNames.ElementInspect,
                Description = "Inspect element.",
                PermissionLevel = PermissionLevel.Read
            }
        }, new ToolGuidanceRequest
        {
            ToolNames = new List<string> { ToolNames.ElementQuery },
            MaxResults = 5
        });

        var guidance = Assert.Single(response.Guidance);
        Assert.Contains("session.get_task_context", guidance.Prerequisites);
        Assert.Contains(ToolNames.ElementInspect, guidance.FollowUps);
        Assert.Contains("Khong query toan model khi chi can selection.", guidance.AntiPatterns);
        Assert.Contains("MODEL_QC_BASE", guidance.RecommendedTemplates);
    }

    [Fact]
    public void SmartQcAggregationService_Builds_Naming_Duplicate_And_Workset_Findings()
    {
        var service = new SmartQcAggregationService();
        var response = service.Build(
            new SmartQcEvidenceBundle
            {
                WorksetHealth = new WorksetHealthResponse
                {
                    Review = new ReviewReport
                    {
                        Issues = new List<ReviewIssue>
                        {
                            new ReviewIssue { Code = "SELECTION_WORKSET_NOT_EDITABLE", Message = "Selection contains elements on non-editable worksets." }
                        }
                    }
                },
                NamingByScope = new Dictionary<string, NamingAuditResponse>
                {
                    ["views"] = new NamingAuditResponse
                    {
                        Violations = new List<NamingViolationItem>
                        {
                            new NamingViolationItem { ElementId = 5, Violation = "Contains invalid characters" }
                        }
                    }
                },
                Duplicates = new DuplicateElementsResponse
                {
                    DuplicateGroups = new List<DuplicateGroup>
                    {
                        new DuplicateGroup { ElementIds = new List<int> { 11, 12 }, Category = "Walls", TypeName = "Basic Wall", Location = "(0,0,0)" }
                    }
                },
                ExecutedChecks = new List<string> { ToolNames.ReviewWorksetHealth, ToolNames.AuditNamingConvention, ToolNames.AuditDuplicateElements }
            },
            new SmartQcRequest
            {
                DocumentKey = "path:b",
                RulesetName = "base-rules",
                MaxFindings = 10
            },
            new SmartQcRulesetDefinition
            {
                Name = "base-rules",
                Description = "test",
                Rules = new List<SmartQcRuleDefinition>
                {
                    new SmartQcRuleDefinition { RuleId = "WORKSET_SELECTION_NOT_EDITABLE", CheckKey = "workset.selection_non_editable", Title = "Workset" },
                    new SmartQcRuleDefinition { RuleId = "VIEW_NAMING", CheckKey = "naming.violations", Scope = "views", Title = "Naming" },
                    new SmartQcRuleDefinition { RuleId = "DUPLICATE_ELEMENTS", CheckKey = "duplicates.groups", Title = "Duplicates" }
                }
            },
            "builtin:test");

        Assert.Equal(3, response.FindingCount);
        Assert.Contains(response.Findings, x => x.RuleId == "WORKSET_SELECTION_NOT_EDITABLE");
        Assert.Contains(response.Findings, x => x.RuleId == "VIEW_NAMING");
        Assert.Contains(response.Findings, x => x.RuleId == "DUPLICATE_ELEMENTS");
    }

    [Fact]
    public void SmartQcAggregationService_Builds_Findings_From_Evidence()
    {
        var service = new SmartQcAggregationService();
        var response = service.Build(
            new SmartQcEvidenceBundle
            {
                ModelHealth = new ModelHealthResponse
                {
                    HasPath = false,
                    TotalWarnings = 125,
                    TotalLinks = 3,
                    LoadedLinks = 2
                },
                Standards = new ModelStandardsResponse
                {
                    Results = new List<StandardsCheckItem>
                    {
                        new StandardsCheckItem
                        {
                            RuleName = "Project Information Completeness",
                            Status = "Fail",
                            Description = "Missing project number."
                        }
                    }
                },
                Sheets = new List<SheetSummaryResponse>
                {
                    new SheetSummaryResponse
                    {
                        SheetId = 101,
                        Review = new ReviewReport
                        {
                            Issues = new List<ReviewIssue>
                            {
                                new ReviewIssue { Code = "TITLEBLOCK_MISSING", Message = "Sheet khong co title block." }
                            }
                        }
                    }
                },
                ExecutedChecks = new List<string> { ToolNames.ReviewModelHealth, ToolNames.AuditModelStandards, ToolNames.ReviewSheetSummary }
            },
            new SmartQcRequest
            {
                DocumentKey = "path:a",
                RulesetName = "base-rules",
                MaxFindings = 10
            },
            new SmartQcRulesetDefinition
            {
                Name = "base-rules",
                Description = "test",
                Rules = new List<SmartQcRuleDefinition>
                {
                    new SmartQcRuleDefinition { RuleId = "DOC_NO_PATH", CheckKey = "model.missing_path", Title = "Path", Severity = "warning" },
                    new SmartQcRuleDefinition { RuleId = "DOC_WARNINGS_LIMIT", CheckKey = "model.warnings_threshold", Title = "Warnings", Severity = "warning", Threshold = 50 },
                    new SmartQcRuleDefinition { RuleId = "STANDARDS_FAILED", CheckKey = "standards.failed_rules", Title = "Standards", Severity = "warning" },
                    new SmartQcRuleDefinition { RuleId = "SHEET_TITLEBLOCK", CheckKey = "sheets.missing_titleblock", Title = "Titleblock", Severity = "warning" }
                }
            },
            "builtin:test");

        Assert.Equal("path:a", response.DocumentKey);
        Assert.True(response.FindingCount >= 4);
        Assert.Contains(response.Findings, x => x.RuleId == "DOC_NO_PATH");
        Assert.Contains(response.Findings, x => x.RuleId == "DOC_WARNINGS_LIMIT");
        Assert.Contains(response.Findings, x => x.RuleId == "STANDARDS_FAILED");
        Assert.Contains(response.Findings, x => x.RuleId == "SHEET_TITLEBLOCK");
    }

    [Fact]
    public void ToolGraphOverlayService_ResolveCandidatePaths_Includes_Base_And_Repo_Paths()
    {
        var paths = ToolGraphOverlayService.ResolveCandidatePaths(System.AppContext.BaseDirectory);

        Assert.NotEmpty(paths);
        Assert.Contains(paths, x => x.EndsWith(@"tool-intelligence\TOOL_GRAPH.overlay.json", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, x => x.IndexOf(@"docs\agent\skills\tool-intelligence\TOOL_GRAPH.overlay.json", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
