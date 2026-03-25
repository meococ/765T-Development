using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.UI.Chat;

internal sealed class ChatSessionStore
{
    private readonly SortedDictionary<long, WorkerHostMissionEvent> _events = new SortedDictionary<long, WorkerHostMissionEvent>();

    internal string SessionId { get; private set; } = string.Empty;

    internal string MissionId { get; private set; } = string.Empty;

    internal bool IsBusy { get; private set; }

    internal string PendingUserMessage { get; private set; } = string.Empty;

    internal WorkerResponse LatestWorkerResponse { get; private set; } = new WorkerResponse();

    internal WorkerHostMissionResponse LatestMissionResponse { get; private set; } = new WorkerHostMissionResponse();

    internal void Reset()
    {
        SessionId = string.Empty;
        MissionId = string.Empty;
        IsBusy = false;
        PendingUserMessage = string.Empty;
        LatestWorkerResponse = new WorkerResponse();
        LatestMissionResponse = new WorkerHostMissionResponse();
        _events.Clear();
    }

    internal void SeedFromWorkerResponse(WorkerResponse response)
    {
        LatestWorkerResponse = response ?? new WorkerResponse();
        SessionId = FirstNonEmpty(LatestWorkerResponse.SessionId, SessionId);
        MissionId = FirstNonEmpty(LatestWorkerResponse.MissionId, MissionId);
        PendingUserMessage = string.Empty;
        IsBusy = false;
        _events.Clear();
        LatestMissionResponse = new WorkerHostMissionResponse
        {
            SessionId = SessionId,
            MissionId = MissionId,
            State = LatestWorkerResponse.MissionStatus
        };
    }

    internal void BeginUserTurn(string message)
    {
        PendingUserMessage = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(PendingUserMessage))
        {
            return;
        }

        LatestWorkerResponse.Messages ??= new List<WorkerChatMessage>();
        LatestWorkerResponse.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.User,
            Content = PendingUserMessage,
            TimestampUtc = DateTime.UtcNow
        });
        LatestWorkerResponse.MissionStatus = WorkerMissionStates.Understanding;
        LatestWorkerResponse.Stage = WorkerStages.Context;
        LatestMissionResponse.SessionId = FirstNonEmpty(LatestWorkerResponse.SessionId, LatestMissionResponse.SessionId, SessionId);
        LatestMissionResponse.MissionId = FirstNonEmpty(LatestWorkerResponse.MissionId, LatestMissionResponse.MissionId, MissionId);
        LatestMissionResponse.State = WorkerMissionStates.Understanding;
        LatestMissionResponse.Succeeded = false;
        LatestMissionResponse.StatusCode = string.Empty;
        LatestMissionResponse.ResponseText = string.Empty;
        LatestMissionResponse.HasPendingApproval = false;
        LatestMissionResponse.PendingActionId = string.Empty;
        _events.Clear();
        LatestMissionResponse.Events = new List<WorkerHostMissionEvent>();
        IsBusy = true;
    }

    internal void BeginMissionStream(string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId))
        {
            return;
        }

        MissionId = missionId.Trim();
        LatestMissionResponse.MissionId = MissionId;
        LatestWorkerResponse.MissionId = FirstNonEmpty(MissionId, LatestWorkerResponse.MissionId);
        LatestMissionResponse.State = FirstNonEmpty(LatestMissionResponse.State, WorkerMissionStates.Understanding);
        IsBusy = true;
    }

    internal void ApplyMissionResponse(WorkerHostMissionResponse response)
    {
        LatestMissionResponse = response ?? new WorkerHostMissionResponse();
        SessionId = FirstNonEmpty(LatestMissionResponse.SessionId, SessionId);
        MissionId = FirstNonEmpty(LatestMissionResponse.MissionId, MissionId);
        if (TryReadWorkerResponse(LatestMissionResponse.PayloadJson, out var worker))
        {
            LatestWorkerResponse = worker;
            SessionId = FirstNonEmpty(worker.SessionId, SessionId);
            MissionId = FirstNonEmpty(worker.MissionId, MissionId);
        }

        foreach (var missionEvent in LatestMissionResponse.Events ?? Enumerable.Empty<WorkerHostMissionEvent>())
        {
            _events[missionEvent.Version] = missionEvent;
        }

        PendingUserMessage = string.Empty;
        IsBusy = !IsTerminalState(LatestMissionResponse.State) && !LatestMissionResponse.HasPendingApproval;
    }

    internal void ApplyMissionEvent(WorkerHostMissionEvent missionEvent)
    {
        if (missionEvent == null)
        {
            return;
        }

        _events[missionEvent.Version] = missionEvent;
        LatestMissionResponse.Events = _events.Values.ToList();
    }

    internal void SetBusy(bool isBusy)
    {
        IsBusy = isBusy;
    }

    internal IReadOnlyList<WorkerHostMissionEvent> GetEvents()
    {
        return _events.Values.ToList();
    }

    private static bool TryReadWorkerResponse(string payloadJson, out WorkerResponse response)
    {
        response = new WorkerResponse();
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            response = JsonUtil.DeserializeRequired<WorkerResponse>(payloadJson);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTerminalState(string? state)
    {
        var normalized = (state ?? string.Empty).Trim();
        return string.Equals(normalized, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase);
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
