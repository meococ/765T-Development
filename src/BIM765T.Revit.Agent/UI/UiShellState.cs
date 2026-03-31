using System;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI;

internal static class UiShellState
{
    internal static class ShellModes
    {
        internal const string Onboarding = "onboarding";
        internal const string Dashboard = "dashboard";
        internal const string Transcript = "transcript";
        internal const string Waiting = "waiting";
    }

    private static readonly object SyncRoot = new object();
    private static string _lastSessionId = string.Empty;
    private static string _lastMissionId = string.Empty;
    private static string _workspaceId = string.Empty;
    private static string _deepScanStatus = ProjectDeepScanStatuses.NotStarted;
    private static string _initStatus = ProjectOnboardingStatuses.NotInitialized;
    private static string _documentKey = string.Empty;
    private static string _documentTitle = string.Empty;
    private static string _activeViewName = string.Empty;
    private static string _shellMode = ShellModes.Onboarding;

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

    internal static string CurrentShellMode
    {
        get
        {
            lock (SyncRoot)
            {
                return _shellMode;
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
            _workspaceId = ResolveWorkspaceId(onboarding.WorkspaceId, response.WorkspaceId, response.ContextSummary?.WorkspaceId, _workspaceId);
            _deepScanStatus = FirstNonEmpty(onboarding.DeepScanStatus, InferDeepScanStatus(response.ContextSummary), _deepScanStatus, ProjectDeepScanStatuses.NotStarted);
            _initStatus = FirstNonEmpty(
                onboarding.InitStatus,
                string.IsNullOrWhiteSpace(_workspaceId) ? ProjectOnboardingStatuses.NotInitialized : ProjectOnboardingStatuses.Initialized,
                _initStatus,
                ProjectOnboardingStatuses.NotInitialized);
            _documentKey = FirstNonEmpty(response.ContextSummary?.DocumentKey, _documentKey);
            _documentTitle = FirstNonEmpty(response.ContextSummary?.DocumentTitle, _documentTitle);
            _activeViewName = FirstNonEmpty(response.ContextSummary?.ActiveViewName, _activeViewName);
            if (!string.IsNullOrWhiteSpace(response.SessionId) || !string.IsNullOrWhiteSpace(response.MissionId))
            {
                _shellMode = ShellModes.Transcript;
            }
        }
    }

    internal static void UpdateAmbient(string? workspaceId, ProjectContextBundleResponse? bundle)
    {
        lock (SyncRoot)
        {
            _workspaceId = ResolveWorkspaceId(bundle?.WorkspaceId, workspaceId, _workspaceId);
            if (bundle != null)
            {
                _deepScanStatus = FirstNonEmpty(bundle.DeepScanStatus, _deepScanStatus, ProjectDeepScanStatuses.NotStarted);
                _initStatus = bundle.Exists
                    ? ProjectOnboardingStatuses.Initialized
                    : string.IsNullOrWhiteSpace(_workspaceId)
                        ? ProjectOnboardingStatuses.NotInitialized
                        : _initStatus;
                if (bundle.Exists && string.Equals(_shellMode, ShellModes.Onboarding, StringComparison.OrdinalIgnoreCase))
                {
                    _shellMode = ShellModes.Dashboard;
                }
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
            if (!string.IsNullOrWhiteSpace(_lastSessionId))
            {
                _shellMode = ShellModes.Transcript;
            }
        }
    }

    internal static void RememberMission(string? missionId)
    {
        lock (SyncRoot)
        {
            _lastMissionId = FirstNonEmpty(missionId, _lastMissionId);
            if (!string.IsNullOrWhiteSpace(_lastMissionId))
            {
                _shellMode = ShellModes.Transcript;
            }
        }
    }

    internal static void SetShellMode(string? shellMode)
    {
        lock (SyncRoot)
        {
            _shellMode = FirstNonEmpty(shellMode, _shellMode, ShellModes.Onboarding);
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
                _shellMode = string.IsNullOrWhiteSpace(_workspaceId) ? ShellModes.Onboarding : ShellModes.Dashboard;
            }
        }
    }

    internal static string ResolveWorkspaceId(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = NormalizeWorkspaceId(value);
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    internal static string NormalizeWorkspaceId(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
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
