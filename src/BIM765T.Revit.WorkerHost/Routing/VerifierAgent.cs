using System;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Kernel;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class VerifierAgent
{
    public VerificationResult Evaluate(KernelInvocationResult result, MissionSnapshot snapshot, KernelToolRequest kernelRequest, MissionCommandInput? command = null)
    {
        var responseText = TryExtractWorkerResponseText(result.PayloadJson ?? string.Empty);
        if (string.IsNullOrWhiteSpace(responseText) && result.Diagnostics.Count > 0)
        {
            responseText = string.Join(" | ", result.Diagnostics);
        }

        var verification = new VerificationResult
        {
            ResponseText = responseText
        };

        var workerState = TryExtractWorkerMissionState(result.PayloadJson ?? string.Empty);
        var commandName = command == null ? string.Empty : PlannerAgent.NormalizeCommandName(command.CommandName);

        if (result.ConfirmationRequired)
        {
            verification.State = WorkerMissionStates.AwaitingApproval;
            verification.Events.Add(new MissionEventDescriptor
            {
                EventType = "PreviewGenerated",
                Payload = new { response = result.PayloadJson ?? string.Empty, status = result.StatusCode }
            });
            verification.Events.Add(new MissionEventDescriptor
            {
                EventType = "ApprovalRequested",
                Payload = new { result.ApprovalToken, result.PreviewRunId },
                Terminal = false
            });
            return verification;
        }

        if (string.Equals(commandName, "reject", StringComparison.OrdinalIgnoreCase))
        {
            verification.State = WorkerMissionStates.Blocked;
            verification.Terminal = true;
            verification.Events.Add(new MissionEventDescriptor
            {
                EventType = "TaskBlocked",
                Payload = new { status = result.StatusCode, response = result.PayloadJson ?? string.Empty },
                Terminal = true
            });
            return verification;
        }

        if (string.Equals(commandName, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            verification.State = WorkerMissionStates.Blocked;
            verification.Terminal = true;
            verification.Events.Add(new MissionEventDescriptor
            {
                EventType = "TaskCanceled",
                Payload = new { status = result.StatusCode, response = result.PayloadJson ?? string.Empty },
                Terminal = true
            });
            return verification;
        }

        if (!kernelRequest.DryRun)
        {
            verification.Events.Add(new MissionEventDescriptor
            {
                EventType = "ExecutionStarted",
                Payload = new { kernelRequest.ToolName, kernelRequest.RequestedAtUtc }
            });

            if (result.ChangedIds.Count > 0
                || result.Artifacts.Count > 0
                || !string.IsNullOrWhiteSpace(result.DiffSummaryJson))
            {
                verification.Events.Add(new MissionEventDescriptor
                {
                    EventType = "RevitMutationApplied",
                    Payload = new
                    {
                        result.ChangedIds,
                        result.Artifacts,
                        result.DiffSummaryJson
                    }
                });
            }
        }

        if (IsVerificationFailure(result))
        {
            verification.Events.Add(new MissionEventDescriptor
            {
                EventType = "VerificationFailed",
                Payload = new { status = result.StatusCode, review = result.ReviewSummaryJson ?? string.Empty }
            });
        }
        else if (result.Succeeded && (!string.IsNullOrWhiteSpace(result.ReviewSummaryJson) || !kernelRequest.DryRun))
        {
            verification.Events.Add(new MissionEventDescriptor
            {
                EventType = "VerificationPassed",
                Payload = new { review = result.ReviewSummaryJson ?? string.Empty }
            });
        }

        verification.State = ResolveState(result, workerState);
        verification.Terminal = IsTerminalState(verification.State);
        verification.Events.Add(new MissionEventDescriptor
        {
            EventType = result.Succeeded ? "TaskCompleted" : "TaskBlocked",
            Payload = new { status = result.StatusCode, response = result.PayloadJson ?? string.Empty },
            Terminal = verification.Terminal
        });
        return verification;
    }

    private static string ResolveState(KernelInvocationResult result, string workerState)
    {
        if (!string.IsNullOrWhiteSpace(workerState))
        {
            return workerState;
        }

        return result.Succeeded ? WorkerMissionStates.Completed : WorkerMissionStates.Blocked;
    }

    private static bool IsVerificationFailure(KernelInvocationResult result)
    {
        return string.Equals(result.StatusCode, StatusCodes.FixLoopVerificationFailed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.StatusCode, StatusCodes.TaskStepBlocked, StringComparison.OrdinalIgnoreCase)
            || (!result.Succeeded && !string.IsNullOrWhiteSpace(result.ReviewSummaryJson));
    }

    private static bool IsTerminalState(string state)
    {
        return string.Equals(state, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase);
    }

    private static string TryExtractWorkerResponseText(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return string.Empty;
        }

        try
        {
            var response = JsonUtil.Deserialize<WorkerResponse>(payloadJson);
            return response.Messages.Count > 0 ? response.Messages[^1].Content ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryExtractWorkerMissionState(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return string.Empty;
        }

        try
        {
            var response = JsonUtil.Deserialize<WorkerResponse>(payloadJson);
            return response.MissionStatus ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
