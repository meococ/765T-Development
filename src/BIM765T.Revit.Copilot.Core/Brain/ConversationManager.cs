using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core.Brain;

public sealed class ConversationManager
{
    private const int MaxMessagesPerSession = 50;
    private readonly object _gate = new object();
    private readonly Dictionary<string, WorkerConversationSessionState> _sessions = new Dictionary<string, WorkerConversationSessionState>(StringComparer.OrdinalIgnoreCase);

    public WorkerConversationSessionState GetOrCreateSession(string? sessionId, string personaId, string clientSurface, string documentKey)
    {
        lock (_gate)
        {
            var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : (sessionId ?? string.Empty).Trim();
            if (_sessions.TryGetValue(resolvedSessionId, out var existing))
            {
                if (!string.IsNullOrWhiteSpace(personaId))
                {
                    existing.PersonaId = personaId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(clientSurface))
                {
                    existing.ClientSurface = clientSurface.Trim();
                }

                if (!string.IsNullOrWhiteSpace(documentKey))
                {
                    existing.DocumentKey = documentKey.Trim();
                }

                existing.LastUpdatedUtc = DateTime.UtcNow;
                return existing;
            }

            var session = new WorkerConversationSessionState
            {
                SessionId = resolvedSessionId,
                PersonaId = string.IsNullOrWhiteSpace(personaId) ? WorkerPersonas.RevitWorker : personaId.Trim(),
                ClientSurface = string.IsNullOrWhiteSpace(clientSurface) ? WorkerClientSurfaces.Ui : clientSurface.Trim(),
                DocumentKey = documentKey ?? string.Empty,
                StartedUtc = DateTime.UtcNow,
                LastUpdatedUtc = DateTime.UtcNow,
                Mission = new WorkerMission
                {
                    MissionId = Guid.NewGuid().ToString("N"),
                    Status = WorkerMissionStates.Idle,
                    Stage = WorkerFlowStages.Thinking,
                    LastUpdatedUtc = DateTime.UtcNow
                }
            };
            _sessions[resolvedSessionId] = session;
            return session;
        }
    }

    public bool TryGetSession(string sessionId, out WorkerConversationSessionState? session)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId ?? string.Empty, out var resolved))
            {
                session = resolved;
                return true;
            }
        }

        session = null;
        return false;
    }

    public WorkerConversationSessionState GetRequiredSession(string sessionId)
    {
        if (!TryGetSession(sessionId, out var session) || session == null)
        {
            throw new InvalidOperationException("Worker session not found.");
        }

        return session;
    }

    public IReadOnlyList<WorkerSessionSummary> ListSessions(int maxResults, bool includeEnded)
    {
        lock (_gate)
        {
            return _sessions.Values
                .Where(x => includeEnded || !string.Equals(x.Status, WorkerSessionStates.Ended, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastUpdatedUtc)
                .Take(Math.Max(1, maxResults))
                .Select(x => x.ToSummary())
                .ToList();
        }
    }

    public WorkerSessionSummary EndSession(string sessionId)
    {
        var session = GetRequiredSession(sessionId);
        lock (_gate)
        {
            session.Status = WorkerSessionStates.Ended;
            session.LastUpdatedUtc = DateTime.UtcNow;
            session.PendingApprovalState = new WorkerPendingApprovalState();
            session.Mission.Status = WorkerMissionStates.Completed;
            session.Mission.Stage = WorkerFlowStages.Done;
            session.Mission.PendingStep = string.Empty;
            session.Mission.LastUpdatedUtc = DateTime.UtcNow;
            return session.ToSummary();
        }
    }

    public WorkerSessionSummary SetPersona(string sessionId, string personaId)
    {
        var session = GetRequiredSession(sessionId);
        lock (_gate)
        {
            session.PersonaId = personaId;
            session.LastUpdatedUtc = DateTime.UtcNow;
            return session.ToSummary();
        }
    }

    public void AddMessage(string sessionId, WorkerChatMessage message)
    {
        if (message == null)
        {
            return;
        }

        var session = GetRequiredSession(sessionId);
        lock (_gate)
        {
            session.Messages.Add(message);
            while (session.Messages.Count > MaxMessagesPerSession)
            {
                session.Messages.RemoveAt(0);
            }

            session.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    public void UpdateContextSummary(string sessionId, WorkerContextSummary contextSummary)
    {
        var session = GetRequiredSession(sessionId);
        lock (_gate)
        {
            session.ContextSummary = contextSummary ?? new WorkerContextSummary();
            session.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    public void SetPendingApproval(string sessionId, WorkerPendingApprovalState pendingApproval)
    {
        var session = GetRequiredSession(sessionId);
        lock (_gate)
        {
            session.PendingApprovalState = pendingApproval ?? new WorkerPendingApprovalState();
            session.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    public void ClearPendingApproval(string sessionId)
    {
        SetPendingApproval(sessionId, new WorkerPendingApprovalState());
    }
}
