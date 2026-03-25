using System.Collections.Generic;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class TaskRecoveryPlannerTests
{
    [Fact]
    public void Build_For_Planned_Run_Recommends_Preview()
    {
        var planner = new TaskRecoveryPlanner();
        var run = NewRun("planned");

        var branches = planner.Build(run);

        Assert.Equal("preview", planner.InferNextAction(run));
        Assert.Contains(branches, x => x.BranchId == "preview_continue" && x.AutoResumable && x.IsRecommended);
    }

    [Fact]
    public void Build_For_Approval_Pending_Run_Offers_Await_Approval_And_Refresh_Preview()
    {
        var planner = new TaskRecoveryPlanner();
        var run = NewRun("preview_ready");
        run.ApprovalToken = "token-1";
        run.PreviewRunId = "preview-1";
        run.ExpectedContextJson = "{}";
        run.Steps[0].Status = "completed";
        run.Steps[1].Status = "completed";

        var branches = planner.Build(run);

        Assert.Equal("approve", planner.InferNextAction(run));
        Assert.Contains(branches, x => x.BranchId == "await_operator_approval" && !x.AutoResumable && x.RequiresApproval);
        Assert.Contains(branches, x => x.BranchId == "refresh_preview" && x.AutoResumable);
    }

    [Fact]
    public void Build_For_ContextMismatch_Execute_Recommends_Fresh_Preview()
    {
        var planner = new TaskRecoveryPlanner();
        var run = NewRun("blocked");
        run.LastErrorCode = StatusCodes.ContextMismatch;
        run.ExpectedContextJson = "{}";
        run.ApprovalToken = "token-1";
        run.Steps[0].Status = "completed";
        run.Steps[1].Status = "completed";
        run.Steps[2].Status = "completed";
        run.SelectedActionIds = new List<string> { "a1" };

        var branch = planner.SelectBranch(run, string.Empty);

        Assert.NotNull(branch);
        Assert.Equal("execute_refresh_preview", branch!.BranchId);
        Assert.Equal("preview", branch.NextAction);
        Assert.True(branch.RequiresFreshPreview);
    }

    [Fact]
    public void Build_For_Verify_Pending_Recommends_Verify()
    {
        var planner = new TaskRecoveryPlanner();
        var run = NewRun("applied");
        run.Steps[0].Status = "completed";
        run.Steps[1].Status = "completed";
        run.Steps[2].Status = "completed";
        run.Steps[3].Status = "completed";

        var branch = planner.SelectBranch(run, string.Empty);

        Assert.NotNull(branch);
        Assert.Equal("verify_continue", branch!.BranchId);
        Assert.Equal("verify", branch.NextAction);
    }

    private static TaskRun NewRun(string status)
    {
        return new TaskRun
        {
            RunId = "run-1",
            TaskKind = "fix_loop",
            TaskName = "parameter_hygiene",
            Status = status,
            RecommendedActionIds = new List<string> { "a1" },
            Steps = new List<TaskStepState>
            {
                new TaskStepState { StepId = "plan", Status = "completed" },
                new TaskStepState { StepId = "preview", Status = "pending" },
                new TaskStepState { StepId = "approve", Status = "pending", RequiresApproval = true },
                new TaskStepState { StepId = "execute", Status = "pending", RequiresApproval = true },
                new TaskStepState { StepId = "verify", Status = "pending" },
                new TaskStepState { StepId = "summarize", Status = "pending" }
            }
        };
    }
}
