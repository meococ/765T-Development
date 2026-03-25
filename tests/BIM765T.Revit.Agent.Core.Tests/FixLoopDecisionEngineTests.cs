using System.Collections.Generic;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class FixLoopDecisionEngineTests
{
    [Fact]
    public void ResolveParameterRule_PrefersMoreSpecificCategoryAndFamilyRule()
    {
        var rules = new List<ParameterRuleCandidate>
        {
            new ParameterRuleCandidate { ParameterName = "Comments", Strategy = "batch_fill", FillValue = "GENERIC", Priority = 5 },
            new ParameterRuleCandidate { ParameterName = "Comments", CategoryName = "Walls", Strategy = "batch_fill", FillValue = "WALL", Priority = 10 },
            new ParameterRuleCandidate { ParameterName = "Comments", CategoryName = "Walls", FamilyName = "Basic Wall", Strategy = "batch_fill", FillValue = "BASIC", Priority = 10 }
        };

        var result = FixLoopDecisionEngine.ResolveParameterRule("Comments", "Walls", "Basic Wall", "Wall A", rules);

        Assert.NotNull(result);
        Assert.Equal("BASIC", result!.FillValue);
    }

    [Fact]
    public void ResolveViewTemplateRule_PrefersSheetPrefixAndCurrentTemplateSpecificRule()
    {
        var rules = new List<ViewTemplateRuleCandidate>
        {
            new ViewTemplateRuleCandidate { ViewType = "FloorPlan", TargetTemplateName = "PLAN", Priority = 5 },
            new ViewTemplateRuleCandidate { ViewType = "FloorPlan", CurrentTemplateNameContains = "WORKING", TargetTemplateName = "PLAN-WORKING", Priority = 10 },
            new ViewTemplateRuleCandidate { ViewType = "FloorPlan", CurrentTemplateNameContains = "WORKING", SheetNumberPrefix = "A-", TargetTemplateName = "PLAN-A", Priority = 10 }
        };

        var result = FixLoopDecisionEngine.ResolveViewTemplateRule("FloorPlan", "Level 1", "WORKING PLAN", "A-101", rules);

        Assert.NotNull(result);
        Assert.Equal("PLAN-A", result!.TargetTemplateName);
    }

    [Fact]
    public void SelectDefaultActionIds_PrefersRecommendedExecutableActions()
    {
        var actions = new List<FixLoopCandidateAction>
        {
            new FixLoopCandidateAction { ActionId = "review", Title = "Review", IsExecutable = true, IsRecommended = false, Priority = 100, RiskLevel = "low" },
            new FixLoopCandidateAction { ActionId = "apply", Title = "Apply", IsExecutable = true, IsRecommended = true, Priority = 10, RiskLevel = "medium" },
            new FixLoopCandidateAction { ActionId = "blocked", Title = "Blocked", IsExecutable = false, IsRecommended = true, Priority = 50, RiskLevel = "low" }
        };

        var result = FixLoopDecisionEngine.SelectDefaultActionIds(actions);

        Assert.Single(result);
        Assert.Equal("apply", result[0]);
    }

    [Fact]
    public void GetProjectOverridePathCandidates_PutsProjectSpecificPathsBeforeGlobal()
    {
        var result = new List<string>(FixLoopDecisionEngine.GetProjectOverridePathCandidates(@"C:\playbooks", "default.fix_loop_v1", "SR_QQ-T_LOD400", @"C:\Projects\MMBS\SR_QQ-T_LOD400.rvt"));

        Assert.NotEmpty(result);
        Assert.Contains(@"C:\playbooks\projects\sr-qq-t-lod400\default.fix_loop_v1.json", result);
        Assert.Equal(@"C:\playbooks\default.fix_loop_v1.json", result[result.Count - 1]);
    }

    [Fact]
    public void SortActions_OrdersByRecommendedThenPriorityThenRisk()
    {
        var actions = new List<FixLoopCandidateAction>
        {
            new FixLoopCandidateAction { ActionId = "medium-priority", Title = "B", IsExecutable = true, IsRecommended = true, Priority = 10, RiskLevel = "medium", Verification = new FixLoopVerificationCriteria { ExpectedIssueDelta = 1 } },
            new FixLoopCandidateAction { ActionId = "high-priority", Title = "A", IsExecutable = true, IsRecommended = true, Priority = 20, RiskLevel = "high", Verification = new FixLoopVerificationCriteria { ExpectedIssueDelta = 1 } },
            new FixLoopCandidateAction { ActionId = "not-recommended", Title = "C", IsExecutable = true, IsRecommended = false, Priority = 100, RiskLevel = "low", Verification = new FixLoopVerificationCriteria { ExpectedIssueDelta = 1 } }
        };

        var result = FixLoopDecisionEngine.SortActions(actions);

        Assert.Equal("high-priority", result[0].ActionId);
        Assert.Equal("medium-priority", result[1].ActionId);
        Assert.Equal("not-recommended", result[2].ActionId);
    }
}
