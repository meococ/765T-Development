using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.WorkerHost.Eventing;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed partial class SafetyAgent
{
    // Tool name patterns that are blocked regardless of context
    private static readonly HashSet<string> BlockedToolPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete_all",
        "format_harddrive",
        "drop_database",
        "execute_shell_admin",
        "revit_api_delete_document",
    };

    // Patterns that indicate potentially dangerous content in command text
    [GeneratedRegex("(?i)(drop\\s+table|truncate\\s+db|delete\\s+from\\s+\\w+\\s+where\\s+1\\s*=\\s*1|format\\s+c:|rm\\s+-rf\\s+/|del\\s+/f\\s+/s\\s+/q)", RegexOptions.Compiled)]
    private static partial Regex DangerousPatternsRegex();

    // Patterns for SQL injection attempts
    [GeneratedRegex("(?i)(union\\s+select|;\\s*drop\\s|;\\s*delete\\s+|'\\s*or\\s+'1'\\s*=\\s*'1)", RegexOptions.Compiled)]
    private static partial Regex SqlInjectionRegex();

    public SafetyAssessment EvaluateSubmission(MissionPlan plan)
    {
        var assessment = new SafetyAssessment
        {
            ResolvedCommandText = plan.CommandText,
            Summary = plan.Summary
        };

        if (string.IsNullOrWhiteSpace(plan.ToolName))
        {
            assessment.Allowed = false;
            assessment.StatusCode = StatusCodes.InvalidRequest;
            assessment.Diagnostics.Add("Planner did not resolve a tool name.");
            return assessment;
        }

        if (!ValidateToolIdentifier(plan.ToolName, assessment))
        {
            return assessment;
        }

        foreach (var chosenTool in plan.ChosenToolSequence ?? new List<string>())
        {
            if (!ValidateToolIdentifier(chosenTool, assessment))
            {
                return assessment;
            }
        }

        // Check command text for dangerous patterns
        if (!string.IsNullOrWhiteSpace(plan.CommandText))
        {
            if (DangerousPatternsRegex().IsMatch(plan.CommandText))
            {
                assessment.Allowed = false;
                assessment.StatusCode = StatusCodes.PolicyBlocked;
                assessment.Diagnostics.Add("Command text contains patterns indicative of destructive operations.");
                return assessment;
            }

            if (SqlInjectionRegex().IsMatch(plan.CommandText))
            {
                assessment.Allowed = false;
                assessment.StatusCode = StatusCodes.PolicyBlocked;
                assessment.Diagnostics.Add("Command text contains potential SQL injection patterns.");
                return assessment;
            }
        }

        return assessment;
    }

    private static bool ValidateToolIdentifier(string toolName, SafetyAssessment assessment)
    {
        foreach (var blocked in BlockedToolPrefixes)
        {
            if (toolName.StartsWith(blocked, StringComparison.OrdinalIgnoreCase))
            {
                assessment.Allowed = false;
                assessment.StatusCode = StatusCodes.PolicyBlocked;
                assessment.Diagnostics.Add($"Tool name '{toolName}' matches a blocked prefix '{blocked}'.");
                return false;
            }
        }

        if (toolName.Contains("..", StringComparison.Ordinal)
            || toolName.Contains(';', StringComparison.Ordinal)
            || toolName.Contains('|', StringComparison.Ordinal))
        {
            assessment.Allowed = false;
            assessment.StatusCode = StatusCodes.InvalidRequest;
            assessment.Diagnostics.Add($"Tool name '{toolName}' contains suspicious characters.");
            return false;
        }

        return true;
    }

    public SafetyAssessment EvaluateCommand(MissionCommandInput input, MissionSnapshot snapshot)
    {
        var assessment = new SafetyAssessment
        {
            ResolvedCommandText = PlannerAgent.NormalizeCommandName(input.CommandName),
            Summary = $"Command={PlannerAgent.NormalizeCommandName(input.CommandName)}"
        };

        if (snapshot.Terminal)
        {
            assessment.Allowed = false;
            assessment.StatusCode = StatusCodes.TaskAlreadyCompleted;
            assessment.Diagnostics.Add("Mission is already terminal.");
            return assessment;
        }

        switch (assessment.ResolvedCommandText)
        {
            case "approval":
                if (!input.AllowMutations)
                {
                    assessment.Allowed = false;
                    assessment.StatusCode = StatusCodes.PolicyBlocked;
                    assessment.Diagnostics.Add("AllowMutations=false blocks approval execution.");
                    return assessment;
                }

                if (string.IsNullOrWhiteSpace(snapshot.ApprovalToken))
                {
                    assessment.Allowed = false;
                    assessment.StatusCode = StatusCodes.WorkerPendingApprovalMissing;
                    assessment.Diagnostics.Add("No pending approval token exists on the mission snapshot.");
                    return assessment;
                }

                if (!MatchesOrEmpty(input.ApprovalToken, snapshot.ApprovalToken))
                {
                    assessment.Allowed = false;
                    assessment.StatusCode = StatusCodes.ApprovalMismatch;
                    assessment.Diagnostics.Add("Approval token does not match the latest mission snapshot.");
                    return assessment;
                }

                if (!MatchesOrEmpty(input.PreviewRunId, snapshot.PreviewRunId))
                {
                    assessment.Allowed = false;
                    assessment.StatusCode = StatusCodes.PreviewRunRequired;
                    assessment.Diagnostics.Add("PreviewRunId does not match the latest mission snapshot.");
                    return assessment;
                }

                if (!MatchesOrEmpty(input.ExpectedContextJson, snapshot.ExpectedContextJson))
                {
                    assessment.Allowed = false;
                    assessment.StatusCode = StatusCodes.ContextMismatch;
                    assessment.Diagnostics.Add("Expected context json does not match the snapshot.");
                    return assessment;
                }
                break;

            case "reject":
                if (string.IsNullOrWhiteSpace(snapshot.ApprovalToken))
                {
                    assessment.Allowed = false;
                    assessment.StatusCode = StatusCodes.WorkerPendingApprovalMissing;
                    assessment.Diagnostics.Add("Reject only makes sense when an approval is pending.");
                    return assessment;
                }
                break;

            case "resume":
                if (string.Equals(snapshot.State, WorkerMissionStates.AwaitingApproval, StringComparison.OrdinalIgnoreCase))
                {
                    assessment.Allowed = false;
                    assessment.StatusCode = StatusCodes.TaskApprovalRequired;
                    assessment.Diagnostics.Add("Mission is waiting for approval, not resume.");
                    return assessment;
                }
                break;
        }

        return assessment;
    }

    private static bool MatchesOrEmpty(string incoming, string snapshotValue)
    {
        return string.IsNullOrWhiteSpace(incoming)
            || string.Equals(incoming, snapshotValue ?? string.Empty, StringComparison.Ordinal);
    }
}
