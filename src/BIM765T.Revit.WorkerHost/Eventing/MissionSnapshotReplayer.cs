using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BIM765T.Revit.Contracts.Platform;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;

namespace BIM765T.Revit.WorkerHost.Eventing;

internal static class MissionSnapshotReplayer
{
    public static MissionSnapshot? Replay(string streamId, IReadOnlyList<MissionEventRecord> events)
    {
        if (string.IsNullOrWhiteSpace(streamId) || events == null || events.Count == 0)
        {
            return null;
        }

        var snapshot = new MissionSnapshot
        {
            MissionId = streamId,
            State = WorkerMissionStates.Planned
        };

        foreach (var record in events.OrderBy(x => x.Version))
        {
            snapshot.Version = record.Version;
            ApplyEvent(snapshot, record);
        }

        return snapshot;
    }

    private static void ApplyEvent(MissionSnapshot snapshot, MissionEventRecord record)
    {
        switch (record.EventType)
        {
            case "TaskStarted":
                snapshot.State = WorkerMissionStates.Running;
                snapshot.RequestJson = FirstNonEmpty(GetString(record.PayloadJson, "request"), GetString(record.PayloadJson, "input"), snapshot.RequestJson);
                break;

            case "IntentClassified":
                snapshot.Intent = FirstNonEmpty(
                    GetString(record.PayloadJson, "intent"),
                    GetString(record.PayloadJson, "Intent"),
                    GetString(record.PayloadJson, "IntentSummary"),
                    snapshot.Intent);
                snapshot.FlowState = FirstNonEmpty(GetString(record.PayloadJson, "FlowState"), GetString(record.PayloadJson, "flowState"), snapshot.FlowState);
                snapshot.GroundingLevel = FirstNonEmpty(GetString(record.PayloadJson, "GroundingLevel"), GetString(record.PayloadJson, "groundingLevel"), snapshot.GroundingLevel);
                snapshot.AutonomyMode = FirstNonEmpty(GetString(record.PayloadJson, "AutonomyMode"), GetString(record.PayloadJson, "autonomyMode"), snapshot.AutonomyMode);
                break;

            case "PreviewGenerated":
                snapshot.State = WorkerMissionStates.AwaitingApproval;
                snapshot.ResponseJson = FirstNonEmpty(GetString(record.PayloadJson, "response"), snapshot.ResponseJson);
                break;

            case "ApprovalRequested":
                snapshot.State = WorkerMissionStates.AwaitingApproval;
                snapshot.ApprovalToken = FirstNonEmpty(GetString(record.PayloadJson, "ApprovalToken"), GetString(record.PayloadJson, "approvalToken"), snapshot.ApprovalToken);
                snapshot.PreviewRunId = FirstNonEmpty(GetString(record.PayloadJson, "PreviewRunId"), GetString(record.PayloadJson, "previewRunId"), snapshot.PreviewRunId);
                snapshot.ExpectedContextJson = FirstNonEmpty(GetString(record.PayloadJson, "ExpectedContextJson"), GetString(record.PayloadJson, "expectedContextJson"), snapshot.ExpectedContextJson);
                break;

            case "ContextResolved":
                snapshot.GroundingLevel = FirstNonEmpty(GetString(record.PayloadJson, "GroundingLevel"), GetString(record.PayloadJson, "groundingLevel"), snapshot.GroundingLevel);
                snapshot.EvidenceRefs = ReadStringArray(record.PayloadJson, "EvidenceRefs", "evidenceRefs", snapshot.EvidenceRefs);
                break;

            case "PlanBuilt":
                snapshot.PlanSummary = FirstNonEmpty(GetString(record.PayloadJson, "summary"), GetString(record.PayloadJson, "Summary"), snapshot.PlanSummary);
                snapshot.FlowState = FirstNonEmpty(GetString(record.PayloadJson, "FlowState"), GetString(record.PayloadJson, "flowState"), snapshot.FlowState);
                snapshot.GroundingLevel = FirstNonEmpty(GetString(record.PayloadJson, "GroundingLevel"), GetString(record.PayloadJson, "groundingLevel"), snapshot.GroundingLevel);
                snapshot.PlannerTraceSummary = FirstNonEmpty(GetString(record.PayloadJson, "PlannerTraceSummary"), GetString(record.PayloadJson, "plannerTraceSummary"), snapshot.PlannerTraceSummary);
                snapshot.ApprovalRequired = ReadBool(record.PayloadJson, "ApprovalRequired", "approvalRequired", snapshot.ApprovalRequired);
                snapshot.ChosenToolSequence = ReadStringArray(record.PayloadJson, "ChosenToolSequence", "chosenToolSequence", snapshot.ChosenToolSequence);
                snapshot.AutonomyMode = FirstNonEmpty(GetString(record.PayloadJson, "AutonomyMode"), GetString(record.PayloadJson, "autonomyMode"), snapshot.AutonomyMode);
                break;

            case "UserApproved":
            case "ExecutionStarted":
                snapshot.State = WorkerMissionStates.Running;
                break;

            case "VerificationPassed":
                snapshot.State = WorkerMissionStates.Verifying;
                snapshot.LastStatusCode = FirstNonEmpty(GetString(record.PayloadJson, "status"), snapshot.LastStatusCode, StatusCodes.Ok);
                break;

            case "VerificationFailed":
                snapshot.State = WorkerMissionStates.Failed;
                snapshot.LastStatusCode = FirstNonEmpty(GetString(record.PayloadJson, "status"), snapshot.LastStatusCode, StatusCodes.FixLoopVerificationFailed);
                break;

            case "TaskCompleted":
                snapshot.State = WorkerMissionStates.Completed;
                snapshot.Terminal = true;
                snapshot.LastStatusCode = FirstNonEmpty(GetString(record.PayloadJson, "status"), snapshot.LastStatusCode, StatusCodes.Ok);
                snapshot.ResponseJson = FirstNonEmpty(GetString(record.PayloadJson, "response"), snapshot.ResponseJson);
                break;

            case "TaskBlocked":
                snapshot.State = WorkerMissionStates.Blocked;
                snapshot.Terminal = true;
                snapshot.LastStatusCode = FirstNonEmpty(GetString(record.PayloadJson, "status"), snapshot.LastStatusCode, StatusCodes.TaskStepBlocked);
                snapshot.ResponseJson = FirstNonEmpty(GetString(record.PayloadJson, "response"), snapshot.ResponseJson);
                break;

            case "TaskCanceled":
                snapshot.State = WorkerMissionStates.Blocked;
                snapshot.Terminal = true;
                snapshot.LastStatusCode = FirstNonEmpty(GetString(record.PayloadJson, "status"), snapshot.LastStatusCode, StatusCodes.TaskAlreadyCompleted);
                break;
        }

        if (record.Terminal)
        {
            snapshot.Terminal = true;
        }
    }

    private static string GetString(string payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.ToString()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ReadBool(string payloadJson, string primaryName, string secondaryName, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            if (document.RootElement.TryGetProperty(primaryName, out var value) || document.RootElement.TryGetProperty(secondaryName, out value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => fallback
                };
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static List<string> ReadStringArray(string payloadJson, string primaryName, string secondaryName, List<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            if (!(document.RootElement.TryGetProperty(primaryName, out var value) || document.RootElement.TryGetProperty(secondaryName, out value))
                || value.ValueKind != JsonValueKind.Array)
            {
                return fallback;
            }

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }
        catch
        {
            return fallback;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    }
}
