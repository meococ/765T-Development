using System;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI;

internal static class UiShellState
{
    private static readonly object SyncRoot = new object();
    private static string _lastSessionId = string.Empty;
    private static string _lastMissionId = string.Empty;
    private static string _workspaceId = string.Empty;
    private static string _deepScanStatus = ProjectDeepScanStatuses.NotStarted;
    private static string _initStatus = ProjectOnboardingStatuses.NotInitialized;
    private static string _documentKey = string.Empty;
    private static string _documentTitle = string.Empty;
    private static string _activeViewName = string.Empty;

    internal static string LastSessionId
    {
        get
        {
            lock (SyncRoot)
            {
                return _lastSessionId;
            }
        }
    }

    internal static string CurrentWorkspaceId
    {
        get
        {
            lock (SyncRoot)
            {
                return _workspaceId;
            }
        }
    }

    internal static string LastMissionId
    {
        get
        {
            lock (SyncRoot)
            {
                return _lastMissionId;
            }
        }
    }

    internal static string CurrentDeepScanStatus
    {
        get
        {
            lock (SyncRoot)
            {
                return _deepScanStatus;
            }
        }
    }

    internal static string CurrentInitStatus
    {
        get
        {
            lock (SyncRoot)
            {
                return _initStatus;
            }
        }
    }

    internal static string CurrentDocumentKey
    {
        get
        {
            lock (SyncRoot)
            {
                return _documentKey;
            }
        }
    }

    internal static string CurrentDocumentTitle
    {
        get
        {
            lock (SyncRoot)
            {
                return _documentTitle;
            }
        }
    }

    internal static string CurrentActiveViewName
    {
        get
        {
            lock (SyncRoot)
            {
                return _activeViewName;
            }
        }
    }

    internal static string CurrentActiveView => CurrentActiveViewName;

    internal static void UpdateFromWorker(WorkerResponse response)
    {
        if (response == null)
        {
            return;
        }

        var onboarding = response.OnboardingStatus ?? new OnboardingStatusDto();
        lock (SyncRoot)
        {
            _lastSessionId = FirstNonEmpty(response.SessionId, onboarding.SessionId, _lastSessionId);
            _lastMissionId = FirstNonEmpty(response.MissionId, onboarding.MissionId, _lastMissionId);
            _workspaceId = FirstNonEmpty(onboarding.WorkspaceId, response.WorkspaceId, response.ContextSummary?.WorkspaceId, _workspaceId);
            _deepScanStatus = FirstNonEmpty(onboarding.DeepScanStatus, InferDeepScanStatus(response.ContextSummary), _deepScanStatus, ProjectDeepScanStatuses.NotStarted);
            _initStatus = FirstNonEmpty(
                onboarding.InitStatus,
                string.IsNullOrWhiteSpace(_workspaceId) ? ProjectOnboardingStatuses.NotInitialized : ProjectOnboardingStatuses.Initialized,
                _initStatus,
                ProjectOnboardingStatuses.NotInitialized);
            _documentKey = FirstNonEmpty(response.ContextSummary?.DocumentKey, _documentKey);
            _documentTitle = FirstNonEmpty(response.ContextSummary?.DocumentTitle, _documentTitle);
            _activeViewName = FirstNonEmpty(response.ContextSummary?.ActiveViewName, _activeViewName);
        }
    }

    internal static void UpdateAmbient(string? workspaceId, ProjectContextBundleResponse? bundle)
    {
        lock (SyncRoot)
        {
            _workspaceId = FirstNonEmpty(bundle?.WorkspaceId, workspaceId, _workspaceId);
            if (bundle != null)
            {
                _deepScanStatus = FirstNonEmpty(bundle.DeepScanStatus, _deepScanStatus, ProjectDeepScanStatuses.NotStarted);
                _initStatus = bundle.Exists
                    ? ProjectOnboardingStatuses.Initialized
                    : ProjectOnboardingStatuses.NotInitialized;
            }
            else if (string.IsNullOrWhiteSpace(_workspaceId))
            {
                _initStatus = ProjectOnboardingStatuses.NotInitialized;
            }
        }
    }

    internal static void UpdateAmbientDocument(string? documentKey, string? documentTitle, string? activeViewName)
    {
        lock (SyncRoot)
        {
            _documentKey = FirstNonEmpty(documentKey, _documentKey);
            _documentTitle = FirstNonEmpty(documentTitle, _documentTitle);
            _activeViewName = FirstNonEmpty(activeViewName, _activeViewName);
        }
    }

    internal static void UpdateAmbientContext(string? documentTitle, string? activeViewName)
    {
        UpdateAmbientDocument(null, documentTitle, activeViewName);
    }

    internal static void RememberSession(string? sessionId)
    {
        lock (SyncRoot)
        {
            _lastSessionId = FirstNonEmpty(sessionId, _lastSessionId);
        }
    }

    internal static void RememberMission(string? missionId)
    {
        lock (SyncRoot)
        {
            _lastMissionId = FirstNonEmpty(missionId, _lastMissionId);
        }
    }

    internal static void ClearSession(string? sessionId = null)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.Equals(_lastSessionId, sessionId!.Trim(), StringComparison.Ordinal))
            {
                _lastSessionId = string.Empty;
                _lastMissionId = string.Empty;
            }
        }
    }

    private static string InferDeepScanStatus(WorkerContextSummary? summary)
    {
        if ((summary?.ProjectPendingUnknowns ?? Enumerable.Empty<string>()).Any(x => x?.IndexOf("deep scan pending", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return ProjectDeepScanStatuses.NotStarted;
        }

        return string.Empty;
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
