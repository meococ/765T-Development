using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class TaskRecoveryPlanner
{
    public string InferNextAction(TaskRun run)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        if (IsPending(run, "preview"))
        {
            return "preview";
        }

        if (IsPending(run, "approve"))
        {
            return "approve";
        }

        if (IsPending(run, "execute"))
        {
            return "execute";
        }

        if (run.Steps.Any(x => string.Equals(x.StepId, "verify", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)))
        {
            return "verify";
        }

        if (run.Steps.Any(x => string.Equals(x.StepId, "summarize", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)))
        {
            return "summary";
        }

        return "summary";
    }

    public List<TaskRecoveryBranch> Build(TaskRun run)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        var nextAction = InferNextAction(run);
        var branches = new List<TaskRecoveryBranch>();
        var requiresApproval = run.Steps.Any(x => string.Equals(x.StepId, "approve", StringComparison.OrdinalIgnoreCase) && x.RequiresApproval);
        var hasApprovalToken = !string.IsNullOrWhiteSpace(run.ApprovalToken);
        var hasPreviewContext = !string.IsNullOrWhiteSpace(run.ExpectedContextJson);
        var isCompleted = string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase);
        var hasSelectedActions = run.SelectedActionIds.Count > 0 || run.RecommendedActionIds.Count > 0;

        if (isCompleted)
        {
            branches.Add(new TaskRecoveryBranch
            {
                BranchId = "summary_ready",
                Title = "Review summary",
                Description = "Task is already completed; inspect summary, evidence, or promote memory.",
                NextAction = "summary",
                ReasonCode = "COMPLETED",
                AutoResumable = true,
                IsRecommended = true
            });
            return branches;
        }

        switch (nextAction)
        {
            case "preview":
                branches.Add(new TaskRecoveryBranch
                {
                    BranchId = "preview_continue",
                    Title = "Build preview",
                    Description = "Generate or refresh bounded preview state before approval/execution.",
                    NextAction = "preview",
                    ReasonCode = string.IsNullOrWhiteSpace(run.LastErrorCode) ? "PREVIEW_PENDING" : run.LastErrorCode,
                    RequiresFreshPreview = !hasPreviewContext || RequiresPreviewRefresh(run.LastErrorCode),
                    AutoResumable = true,
                    IsRecommended = true
                });
                break;

            case "approve":
                branches.Add(new TaskRecoveryBranch
                {
                    BranchId = "await_operator_approval",
                    Title = "Await approval",
                    Description = "Preview is ready; operator approval is required before mutation can continue.",
                    NextAction = "approve",
                    ReasonCode = string.IsNullOrWhiteSpace(run.LastErrorCode) ? StatusCodes.TaskApprovalRequired : run.LastErrorCode,
                    RequiresApproval = true,
                    AutoResumable = false,
                    IsRecommended = true
                });

                branches.Add(new TaskRecoveryBranch
                {
                    BranchId = "refresh_preview",
                    Title = "Refresh preview",
                    Description = "Rebuild preview and expected context if scope or approval state may have drifted.",
                    NextAction = "preview",
                    ReasonCode = RequiresPreviewRefresh(run.LastErrorCode) ? run.LastErrorCode : "PREVIEW_REFRESH",
                    RequiresFreshPreview = true,
                    AutoResumable = true,
                    IsRecommended = RequiresPreviewRefresh(run.LastErrorCode)
                });
                break;

            case "execute":
                if (requiresApproval && !hasApprovalToken)
                {
                    branches.Add(new TaskRecoveryBranch
                    {
                        BranchId = "approval_missing",
                        Title = "Approval missing",
                        Description = "Execution is blocked until a valid approval token is captured from preview/approval.",
                        NextAction = "approve",
                        ReasonCode = StatusCodes.TaskApprovalRequired,
                        RequiresApproval = true,
                        AutoResumable = false,
                        IsRecommended = true
                    });
                }
                else
                {
                    branches.Add(new TaskRecoveryBranch
                    {
                        BranchId = "execute_continue",
                        Title = "Continue execute",
                        Description = "Resume the approved execution lane using persisted preview token and expected context.",
                        NextAction = "execute",
                        ReasonCode = string.IsNullOrWhiteSpace(run.LastErrorCode) ? "EXECUTE_PENDING" : run.LastErrorCode,
                        RequiresApproval = requiresApproval,
                        AutoResumable = true,
                        IsRecommended = !RequiresPreviewRefresh(run.LastErrorCode)
                    });
                }

                if (hasSelectedActions || RequiresPreviewRefresh(run.LastErrorCode))
                {
                    branches.Add(new TaskRecoveryBranch
                    {
                        BranchId = "execute_refresh_preview",
                        Title = "Rebuild preview before execute",
                        Description = "Refresh preview/approval state before continuing execution.",
                        NextAction = "preview",
                        ReasonCode = RequiresPreviewRefresh(run.LastErrorCode) ? run.LastErrorCode : "PREVIEW_REFRESH",
                        RequiresFreshPreview = true,
                        AutoResumable = true,
                        IsRecommended = RequiresPreviewRefresh(run.LastErrorCode)
                    });
                }
                break;

            case "verify":
                branches.Add(new TaskRecoveryBranch
                {
                    BranchId = "verify_continue",
                    Title = "Run verifier",
                    Description = "Continue with post-action verification and residual analysis.",
                    NextAction = "verify",
                    ReasonCode = string.IsNullOrWhiteSpace(run.LastErrorCode) ? "VERIFY_PENDING" : run.LastErrorCode,
                    AutoResumable = true,
                    IsRecommended = true
                });
                break;

            default:
                branches.Add(new TaskRecoveryBranch
                {
                    BranchId = "summary_finalize",
                    Title = "Finalize summary",
                    Description = "Mark final summary checkpoint and keep the task ready for review/promote-memory.",
                    NextAction = "summary",
                    ReasonCode = "SUMMARY_PENDING",
                    AutoResumable = true,
                    IsRecommended = true
                });
                break;
        }

        if (string.Equals(run.Status, "blocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "partial", StringComparison.OrdinalIgnoreCase))
        {
            branches.Add(new TaskRecoveryBranch
            {
                BranchId = "inspect_residuals",
                Title = "Inspect residuals",
                Description = "Review residual issues and diagnostics before deciding whether to retry or replan.",
                NextAction = "summary",
                ReasonCode = string.IsNullOrWhiteSpace(run.LastErrorCode) ? run.Status.ToUpperInvariant() : run.LastErrorCode,
                AutoResumable = false,
                IsRecommended = !branches.Any(x => x.IsRecommended)
            });
        }

        if (!hasSelectedActions && string.Equals(run.TaskKind, "fix_loop", StringComparison.OrdinalIgnoreCase))
        {
            branches.Add(new TaskRecoveryBranch
            {
                BranchId = "manual_replan",
                Title = "Replan task",
                Description = "No executable selected actions remain; create a new task plan with a narrower scope or different scenario.",
                NextAction = "plan",
                ReasonCode = StatusCodes.TaskStepBlocked,
                AutoResumable = false,
                IsRecommended = false
            });
        }

        return branches
            .GroupBy(x => x.BranchId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.IsRecommended).ThenByDescending(y => y.AutoResumable).First())
            .OrderByDescending(x => x.IsRecommended)
            .ThenByDescending(x => x.AutoResumable)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public TaskRecoveryBranch? SelectBranch(TaskRun run, string preferredBranchId)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        var branches = Build(run);
        if (!string.IsNullOrWhiteSpace(preferredBranchId))
        {
            return branches.FirstOrDefault(x => string.Equals(x.BranchId, preferredBranchId, StringComparison.OrdinalIgnoreCase));
        }

        return branches.FirstOrDefault(x => x.IsRecommended && x.AutoResumable)
            ?? branches.FirstOrDefault(x => x.AutoResumable)
            ?? branches.FirstOrDefault(x => x.IsRecommended)
            ?? branches.FirstOrDefault();
    }

    private static bool IsPending(TaskRun run, string stepId)
    {
        return run.Steps.Any(x => string.Equals(x.StepId, stepId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Status, "pending", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequiresPreviewRefresh(string reasonCode)
    {
        return string.Equals(reasonCode, StatusCodes.ContextMismatch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reasonCode, StatusCodes.ApprovalExpired, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reasonCode, StatusCodes.ApprovalInvalid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reasonCode, StatusCodes.ApprovalMismatch, StringComparison.OrdinalIgnoreCase);
    }
}
