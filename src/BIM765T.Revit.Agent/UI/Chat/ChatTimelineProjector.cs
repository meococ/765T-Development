using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Chat;

internal sealed class ChatTimelineProjector
{
    internal ChatSessionVm Project(ChatSessionStore store)
    {
        var worker = store?.LatestWorkerResponse ?? new WorkerResponse();
        var mission = store?.LatestMissionResponse ?? new WorkerHostMissionResponse();
        var vm = new ChatSessionVm
        {
            SessionId = FirstNonEmpty(worker.SessionId, mission.SessionId, store?.SessionId),
            MissionId = FirstNonEmpty(worker.MissionId, mission.MissionId, store?.MissionId),
            IsBusy = store?.IsBusy == true,
            LatestWorkerResponse = worker,
            LatestMissionResponse = mission
        };

        foreach (var message in worker.Messages ?? new List<WorkerChatMessage>())
        {
            vm.Entries.Add(new TimelineEntryVm
            {
                Kind = string.Equals(message.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase)
                    ? TimelineEntryKinds.UserMessage
                    : TimelineEntryKinds.AssistantMessage,
                Message = message
            });
        }

        var hasConversationMessages = HasConversationMessages(worker.Messages);

        if (store != null
            && !string.IsNullOrWhiteSpace(store.PendingUserMessage)
            && !HasMatchingPendingUserTurn(worker.Messages, store.PendingUserMessage))
        {
            vm.Entries.Add(new TimelineEntryVm
            {
                Kind = TimelineEntryKinds.UserMessage,
                Message = new WorkerChatMessage
                {
                    Role = WorkerMessageRoles.User,
                    Content = store.PendingUserMessage,
                    TimestampUtc = DateTime.UtcNow
                }
            });
        }

        var streamingMessage = BuildStreamingAssistantMessage(store, worker, mission);
        if (streamingMessage != null)
        {
            vm.Entries.Add(new TimelineEntryVm
            {
                Kind = TimelineEntryKinds.AssistantMessage,
                Message = streamingMessage
            });
        }

        var trace = BuildMissionTrace(store, worker, mission);
        if (trace.Events.Count > 0 || store?.IsBusy == true)
        {
            vm.Entries.Add(new TimelineEntryVm
            {
                Kind = TimelineEntryKinds.MissionTraceTurn,
                Trace = trace
            });
        }

        var systemTurn = BuildSystemTurn(worker, mission, !hasConversationMessages);
        if (!string.IsNullOrWhiteSpace(systemTurn.Title))
        {
            vm.Entries.Add(new TimelineEntryVm
            {
                Kind = TimelineEntryKinds.SystemStateTurn,
                SystemTurn = systemTurn
            });
        }

        foreach (var artifact in BuildArtifacts(worker, mission))
        {
            vm.Entries.Add(new TimelineEntryVm
            {
                Kind = TimelineEntryKinds.ArtifactRow,
                Artifact = artifact
            });
        }

        return vm;
    }

    private static MissionTraceVm BuildMissionTrace(ChatSessionStore? store, WorkerResponse worker, WorkerHostMissionResponse mission)
    {
        var trace = new MissionTraceVm
        {
            MissionId = FirstNonEmpty(mission.MissionId, worker.MissionId, store?.MissionId),
            Summary = ResolveTraceSummary(worker, mission, store),
            State = FirstNonEmpty(mission.State, worker.MissionStatus, WorkerMissionStates.Idle),
            Stage = WorkerFlowStages.Normalize(FirstNonEmpty(worker.Stage, mission.FlowState, WorkerFlowStages.Thinking)),
            IsTerminal = IsTerminalState(mission.State),
            IsExpanded = store?.IsBusy == true || !IsTerminalState(mission.State),
            ReasoningMode = FirstNonEmpty(mission.ReasoningMode, worker.ReasoningMode, WorkerReasoningModes.RuleFirst)
        };

        trace.Badges.Add(trace.Stage.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(mission.ConfiguredProvider))
        {
            trace.Badges.Add(mission.ConfiguredProvider.ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(mission.PlannerModel))
        {
            trace.Badges.Add(mission.PlannerModel.ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(mission.AutonomyMode))
        {
            trace.Badges.Add(("mode:" + mission.AutonomyMode).ToUpperInvariant());
        }

        if (store?.IsBusy == true && store.GetEvents().Count == 0)
        {
            trace.Events.Add(new MissionTraceEventVm
            {
                EventType = "Waiting",
                Summary = "765T is reading context, running safety checks, and preparing a response.",
                AccentKind = WorkerFlowStages.Thinking
            });
            return trace;
        }

        foreach (var missionEvent in store?.GetEvents() ?? Array.Empty<WorkerHostMissionEvent>())
        {
            trace.Events.Add(new MissionTraceEventVm
            {
                Version = missionEvent.Version,
                EventType = missionEvent.EventType,
                OccurredUtc = missionEvent.OccurredUtc,
                Summary = SummarizeEvent(missionEvent),
                AccentKind = ResolveEventAccent(missionEvent.EventType)
            });
        }

        return trace;
    }

    private static SystemStateTurnVm BuildSystemTurn(WorkerResponse worker, WorkerHostMissionResponse mission, bool allowInformationalTurns)
    {
        if (worker.PendingApproval != null && !string.IsNullOrWhiteSpace(worker.PendingApproval.PendingActionId))
        {
            var approval = new SystemStateTurnVm
            {
                TurnKind = SystemTurnKinds.Approval,
                Title = "Approval required",
                Summary = FirstNonEmpty(worker.PendingApproval.Summary, "Preview is ready and awaiting approval.")
            };
            approval.Badges.Add(FirstNonEmpty(worker.PendingApproval.ExecutionTier, WorkerExecutionTiers.Tier1).ToUpperInvariant());
            approval.Actions.Add(new SystemTurnActionVm
            {
                    ActionKind = SystemTurnActionKinds.Approve,
                    Label = "Approve"
                });
            approval.Actions.Add(new SystemTurnActionVm
            {
                ActionKind = SystemTurnActionKinds.Reject,
                Label = "Reject"
            });
            return approval;
        }

        var fallback = worker.FallbackProposal;
        if (fallback != null
            && (!string.IsNullOrWhiteSpace(fallback.Summary)
                || fallback.ArtifactPaths.Count > 0
                || fallback.ArtifactKinds.Count > 0))
        {
            var turn = new SystemStateTurnVm
            {
                TurnKind = SystemTurnKinds.Fallback,
                Title = "Fallback artifact",
                Summary = FirstNonEmpty(fallback.PreviewSummary, fallback.Summary, fallback.Reason)
            };
            foreach (var kind in fallback.ArtifactKinds.Take(3))
            {
                turn.Badges.Add(kind.ToUpperInvariant());
            }

            if (fallback.ArtifactPaths.Count > 0)
            {
                turn.Actions.Add(new SystemTurnActionVm
                {
                    ActionKind = SystemTurnActionKinds.OpenArtifact,
                    Label = "Open artifact",
                    Path = fallback.ArtifactPaths[0]
                });
                turn.Actions.Add(new SystemTurnActionVm
                {
                    ActionKind = SystemTurnActionKinds.CopyPath,
                    Label = "Copy path",
                    Path = fallback.ArtifactPaths[0]
                });
            }

            if (!string.IsNullOrWhiteSpace(fallback.CandidateBuiltInToolName))
            {
                turn.Actions.Add(new SystemTurnActionVm
                {
                    ActionKind = SystemTurnActionKinds.ApplyInRevit,
                    Label = "Apply in Revit",
                    CommandText = fallback.CandidateBuiltInToolName
                });
            }

            return turn;
        }

        var onboarding = worker.OnboardingStatus;
        if (allowInformationalTurns
            && onboarding != null
            && (string.Equals(onboarding.InitStatus, ProjectOnboardingStatuses.NotInitialized, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(onboarding.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)))
        {
            var turn = new SystemStateTurnVm
            {
                TurnKind = SystemTurnKinds.Onboarding,
                Title = string.Equals(onboarding.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase)
                    ? "Deep scan recommended"
                    : "Project context initialization needed",
                Summary = FirstNonEmpty(
                    onboarding.Summary,
                    string.Equals(onboarding.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase)
                        ? "Workspace exists but deep scan is not yet complete."
                        : "Initialize workspace before relying on project context.")
            };
            turn.Badges.Add(FirstNonEmpty(onboarding.WorkspaceId, "ONBOARDING").ToUpperInvariant());
            turn.Badges.Add(FirstNonEmpty(onboarding.DeepScanStatus, ProjectDeepScanStatuses.NotStarted).ToUpperInvariant());
            if (!string.Equals(onboarding.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase))
            {
                turn.Actions.Add(new SystemTurnActionVm
                {
                    ActionKind = SystemTurnActionKinds.InitWorkspace,
                    Label = "Init workspace"
                });
            }
            else if (!string.Equals(onboarding.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            {
                turn.Actions.Add(new SystemTurnActionVm
                {
                    ActionKind = SystemTurnActionKinds.RunDeepScan,
                    Label = "Run deep scan"
                });
            }

            return turn;
        }

        if (!mission.Succeeded
            && (!string.IsNullOrWhiteSpace(mission.StatusCode) || !string.IsNullOrWhiteSpace(mission.ResponseText)))
        {
            return new SystemStateTurnVm
            {
                TurnKind = SystemTurnKinds.Error,
                Title = "Mission error",
                Summary = FirstNonEmpty(mission.ResponseText, mission.StatusCode, "WorkerHost is unavailable.")
            };
        }

        return new SystemStateTurnVm();
    }

    private static bool HasConversationMessages(IEnumerable<WorkerChatMessage>? messages)
    {
        return (messages ?? Array.Empty<WorkerChatMessage>()).Any(message =>
            string.Equals(message.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.Role, WorkerMessageRoles.Worker, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ArtifactAttachmentVm> BuildArtifacts(WorkerResponse worker, WorkerHostMissionResponse mission)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in (worker.ArtifactRefs ?? new List<string>())
                     .Concat(mission.ArtifactRefs ?? new List<string>())
                     .Concat(mission.EvidenceRefs ?? new List<string>())
                     .Concat(worker.FallbackProposal?.ArtifactPaths ?? new List<string>()))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var trimmed = path.Trim();
            if (!seen.Add(trimmed))
            {
                continue;
            }

            yield return new ArtifactAttachmentVm
            {
                Label = trimmed.IndexOf('\\') >= 0 || trimmed.IndexOf('/') >= 0
                    ? System.IO.Path.GetFileName(trimmed)
                    : trimmed,
                Path = trimmed,
                Source = "artifact"
            };
        }
    }

    private static bool HasMatchingPendingUserTurn(IEnumerable<WorkerChatMessage>? messages, string pendingText)
    {
        var lastUser = messages?
            .LastOrDefault(x => string.Equals(x.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase))
            ?.Content;
        return string.Equals((lastUser ?? string.Empty).Trim(), pendingText.Trim(), StringComparison.Ordinal);
    }

    private static WorkerChatMessage? BuildStreamingAssistantMessage(ChatSessionStore? store, WorkerResponse worker, WorkerHostMissionResponse mission)
    {
        if (store?.IsBusy != true)
        {
            return null;
        }

        var messages = worker.Messages ?? new List<WorkerChatMessage>();
        var lastConversationMessage = messages.LastOrDefault(message =>
            string.Equals(message.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.Role, WorkerMessageRoles.Worker, StringComparison.OrdinalIgnoreCase));
        if (lastConversationMessage != null
            && string.Equals(lastConversationMessage.Role, WorkerMessageRoles.Worker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var events = store.GetEvents();
        var latestEvent = events.Count > 0 ? events[events.Count - 1] : null;
        var content = ResolveStreamingAssistantText(latestEvent, worker);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var timestamp = lastConversationMessage?.TimestampUtc ?? DateTime.UtcNow;
        if (latestEvent != null && DateTime.TryParse(latestEvent.OccurredUtc, out var parsedOccurredUtc))
        {
            timestamp = parsedOccurredUtc.ToLocalTime();
        }

        return new WorkerChatMessage
        {
            Role = WorkerMessageRoles.Worker,
            Content = content,
            TimestampUtc = timestamp,
            StatusCode = "STREAMING"
        };
    }

    private static string ResolveStreamingAssistantText(WorkerHostMissionEvent? latestEvent, WorkerResponse worker)
    {
        switch (latestEvent?.EventType ?? string.Empty)
        {
            case "TaskStarted":
                return "Request received. Opening context from the current model...";
            case "IntentClassified":
                return "Intent identified. Enriching Revit context for an accurate response...";
            case "ContextResolved":
                return "Reading document, view, selection, and related memory before responding...";
            case "PlanBuilt":
                return "Plan ready. Composing response and next steps...";
            case "ExecutionStarted":
                return "Running execution step in Revit...";
            case "PreviewGenerated":
                return "Preview created. Composing results...";
            case "ApprovalRequested":
                return "Preview ready and awaiting your approval for the next step.";
            default:
                var documentTitle = FirstNonEmpty(worker.ContextSummary?.DocumentTitle, "current model");
                var viewName = FirstNonEmpty(worker.ContextSummary?.ActiveViewName, "active view");
                return $"Reading context of '{documentTitle}' at '{viewName}' and preparing response...";
        }
    }

    private static string ResolveTraceSummary(WorkerResponse worker, WorkerHostMissionResponse mission, ChatSessionStore? store)
    {
        if (store?.IsBusy == true)
        {
            return "765T is analyzing the request, running safety checks, and preparing a response.";
        }

        return FirstNonEmpty(
            worker.PlanSummary,
            mission.PlanningSummary,
            mission.ResponseText,
            worker.Messages.LastOrDefault(x => string.Equals(x.Role, WorkerMessageRoles.Worker, StringComparison.OrdinalIgnoreCase))?.Content,
            "Mission trace available for this response.");
    }

    private static string SummarizeEvent(WorkerHostMissionEvent missionEvent)
    {
        var payload = missionEvent?.PayloadJson ?? string.Empty;
        switch (missionEvent?.EventType ?? string.Empty)
        {
            case "TaskStarted":
                return "Request received and mission started.";
            case "IntentClassified":
                return "Request type and processing scope identified.";
            case "ContextResolved":
                return "Revit context and related retrieval read.";
            case "PlanBuilt":
                return "Safe plan built for this turn.";
            case "PreviewGenerated":
                return "Preview created for review.";
            case "ApprovalRequested":
                return "Awaiting approval.";
            case "ExecutionStarted":
                return "Execution started.";
            case "RevitMutationApplied":
                return "Changes applied via Revit kernel.";
            case "VerificationPassed":
                return "Verification passed.";
            case "VerificationFailed":
                return "Verification failed — review needed.";
            case "TaskCompleted":
                return "Mission completed.";
            case "TaskBlocked":
                return "Mission blocked.";
            case "TaskCanceled":
                return "Mission canceled.";
            case "UserApproved":
                return "Approved.";
            case "UserRejected":
                return "Rejected.";
            default:
                return string.IsNullOrWhiteSpace(payload)
                    ? missionEvent?.EventType ?? "Event"
                    : missionEvent!.EventType + ": " + TrimSummary(payload);
        }
    }

    private static string ResolveEventAccent(string? eventType)
    {
        switch ((eventType ?? string.Empty).Trim())
        {
            case "PreviewGenerated":
            case "ApprovalRequested":
                return WorkerFlowStages.Preview;
            case "ExecutionStarted":
            case "RevitMutationApplied":
                return WorkerFlowStages.Run;
            case "VerificationPassed":
                return WorkerFlowStages.Verify;
            case "VerificationFailed":
            case "TaskBlocked":
            case "TaskCanceled":
                return WorkerFlowStages.Error;
            case "TaskCompleted":
                return WorkerFlowStages.Done;
            case "PlanBuilt":
                return WorkerFlowStages.Plan;
            case "IntentClassified":
            case "ContextResolved":
                return WorkerFlowStages.Thinking;
            default:
                return WorkerFlowStages.Scan;
        }
    }

    private static bool IsTerminalState(string? state)
    {
        return string.Equals(state, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase);
    }

    [SuppressMessage("Performance", "CA1845:Use span-based 'string.Concat' and 'AsSpan' instead of 'Substring'", Justification = "Kept simple for multi-target compatibility with the add-in runtime.")]
    private static string TrimSummary(string value)
    {
        var source = value.Trim();
        return source.Length > 120 ? source.Substring(0, 120) + "..." : source;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
    }
}
