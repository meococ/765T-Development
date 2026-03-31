using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.UI;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Agent.UI.Components;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.UI.Tabs.Services;

/// <summary>
/// Owns the session and mission restore lifecycle for the Worker tab:
/// bootstrapping from previously remembered state, opening a specific session,
/// auto-resuming the most-recently-updated session for a document, showing the
/// onboarding state when no restorable session exists, and handling the
/// "start new task" teardown path.
/// </summary>
/// <remarks>
/// <para>
/// Every state-change that previously called <c>RenderCurrentState()</c> on
/// <c>WorkerTab</c> now raises <see cref="StateChanged"/> so that the UI layer
/// can schedule a render without this service holding any WPF / Dispatcher
/// dependency.  Likewise, <c>_onStateChanged</c> callback broadcasts are
/// replaced by <see cref="ResponseApplied"/>, and toast calls are replaced by
/// <see cref="ToastRequested"/>.
/// </para>
/// <para>
/// <see cref="InternalToolClient.Instance"/> and <see cref="UiShellState"/>
/// are consumed as static singletons exactly as they are in the original tab —
/// no additional abstraction is introduced for them here.
/// </para>
/// </remarks>
internal sealed class SessionLifecycleService
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly WorkerHostMissionClient _missionClient;
    private readonly ChatSessionStore _chatStore;
    private readonly AgentSettings _settings;
    private readonly MissionStreamService _streamService;
    private readonly ProjectDashboardService _projectDashboard;

    private bool _bootstrapped;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised whenever session or mission state changes in a way that requires
    /// the UI to re-render (replaces direct <c>RenderCurrentState()</c> calls
    /// from the original <c>WorkerTab</c>).
    /// </summary>
    internal event Action? StateChanged;

    /// <summary>
    /// Raised after a <see cref="WorkerResponse"/> has been applied to the chat
    /// store during mission/session restore.  The payload is forwarded directly
    /// to any external state observers (e.g. the project-brief card).
    /// </summary>
    internal event Action<WorkerResponse>? ResponseApplied;

    /// <summary>
    /// Raised when the service wants to display a toast notification.
    /// The first argument is the message text; the second is the severity level.
    /// </summary>
#pragma warning disable CS0067 // Event is never used — reserved for future failure paths
    internal event Action<string, ToastType>? ToastRequested;
#pragma warning restore CS0067

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets a value indicating whether the one-time bootstrap sequence has
    /// already been executed for the current pane lifetime.
    /// </summary>
    internal bool IsBootstrapped => _bootstrapped;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="SessionLifecycleService"/>.
    /// </summary>
    /// <param name="missionClient">HTTP client used to contact the WorkerHost mission API.</param>
    /// <param name="chatStore">Session store that accumulates mission events and snapshots.</param>
    /// <param name="settings">Agent configuration (timeouts, LLM provider labels, feature flags).</param>
    /// <param name="streamService">Service that manages the SSE mission-event stream lifecycle.</param>
    /// <param name="projectDashboard">Service that owns project-workspace dashboard state.</param>
    internal SessionLifecycleService(
        WorkerHostMissionClient missionClient,
        ChatSessionStore chatStore,
        AgentSettings settings,
        MissionStreamService streamService,
        ProjectDashboardService projectDashboard)
    {
        _missionClient = missionClient ?? throw new ArgumentNullException(nameof(missionClient));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _streamService = streamService ?? throw new ArgumentNullException(nameof(streamService));
        _projectDashboard = projectDashboard ?? throw new ArgumentNullException(nameof(projectDashboard));
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Main bootstrap entry point.  Attempts to restore state in priority order:
    /// <list type="number">
    ///   <item>The most-recently-remembered mission (<see cref="UiShellState.LastMissionId"/>).</item>
    ///   <item>The most-recently-remembered session (<see cref="UiShellState.LastSessionId"/>).</item>
    ///   <item>The most-recently-updated active session returned by <c>worker.list_sessions</c>.</item>
    ///   <item>Onboarding state when no restorable session exists.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Sets <see cref="UiShellState.ShellModes.Transcript"/> on success and
    /// <see cref="UiShellState.ShellModes.Onboarding"/> when no prior state is
    /// found.  Callers should guard with <see cref="IsBootstrapped"/> and call
    /// <see cref="MarkBootstrapped"/> immediately before invoking this method
    /// to prevent duplicate bootstrap runs.
    /// </remarks>
    internal async Task RestoreOrShowOnboardingAsync()
    {
        if (await TryRestoreMissionAsync(UiShellState.LastMissionId).ConfigureAwait(true))
        {
            UiShellState.SetShellMode(UiShellState.ShellModes.Transcript);
            return;
        }

        if (await TryRestoreSessionAsync(UiShellState.LastSessionId).ConfigureAwait(true))
        {
            UiShellState.SetShellMode(UiShellState.ShellModes.Transcript);
            return;
        }

        var sessionsResponse = await InternalToolClient.Instance.CallAsync(
            ToolNames.WorkerListSessions,
            JsonUtil.Serialize(new WorkerListSessionsRequest
            {
                MaxResults = 6,
                IncludeEnded = false
            }),
            false).ConfigureAwait(true);

        if (sessionsResponse.Succeeded)
        {
            var sessions = JsonUtil.DeserializeRequired<List<WorkerSessionSummary>>(sessionsResponse.PayloadJson);

            foreach (var session in sessions
                         .Where(x => !string.IsNullOrWhiteSpace(x?.SessionId))
                         .OrderByDescending(x => x.LastUpdatedUtc))
            {
                if (await TryRestoreSessionAsync(session.SessionId).ConfigureAwait(true))
                {
                    return;
                }
            }
        }

        UiShellState.SetShellMode(UiShellState.ShellModes.Onboarding);
        await ShowOnboardingStateAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the session identified by <paramref name="sessionId"/> and seeds the
    /// chat store with its persisted <see cref="WorkerResponse"/>.
    /// </summary>
    /// <param name="sessionId">The session identifier to open.</param>
    /// <returns>
    /// <see langword="true"/> when the session was successfully fetched and
    /// applied; <see langword="false"/> when <paramref name="sessionId"/> is
    /// blank or the tool call does not succeed.
    /// </returns>
    internal Task<bool> OpenSessionAsync(string sessionId)
    {
        return TryRestoreSessionAsync(sessionId);
    }

    /// <summary>
    /// Inspects <paramref name="sessions"/> for a candidate that can be resumed
    /// without user intervention, preferring sessions whose
    /// <see cref="WorkerSessionSummary.DocumentKey"/> matches
    /// <paramref name="documentKey"/>.
    /// </summary>
    /// <param name="sessions">
    /// The set of available session summaries, typically the result of a recent
    /// <c>worker.list_sessions</c> call.
    /// </param>
    /// <param name="documentKey">
    /// The ambient document key for the currently active Revit model, used to
    /// prefer document-specific sessions.  May be <c>null</c> or empty.
    /// </param>
    /// <param name="currentSessionId">
    /// The session already active in the UI, if any.  When non-empty this method
    /// returns <see cref="string.Empty"/> immediately because a resume is not
    /// needed.
    /// </param>
    /// <returns>
    /// The resumed session identifier on success, or <see cref="string.Empty"/>
    /// when no suitable candidate exists or the resume attempt fails.
    /// </returns>
    internal async Task<string> TryAutoResumeLatestSessionAsync(
        IEnumerable<WorkerSessionSummary> sessions,
        string? documentKey,
        string? currentSessionId)
    {
        if (!CanAutoResumeLatestSession(currentSessionId))
        {
            return string.Empty;
        }

        var candidates = (sessions ?? Array.Empty<WorkerSessionSummary>())
            .Where(x => !string.IsNullOrWhiteSpace(x?.SessionId))
            .Select(x => x!)
            .OrderByDescending(x => x.LastUpdatedUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        // Prefer the most-recent session whose document key matches the ambient
        // document; fall back to the globally most-recent session.
        var preferred = candidates.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(documentKey)
                && string.Equals(x.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
            ?? candidates[0];

        if (string.IsNullOrWhiteSpace(preferred.SessionId)
            || string.Equals(
                preferred.SessionId,
                FirstNonEmpty(currentSessionId, _chatStore.SessionId),
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return await TryRestoreSessionAsync(preferred.SessionId).ConfigureAwait(true)
            ? preferred.SessionId
            : string.Empty;
    }

    /// <summary>
    /// Cancels any in-flight request, resets the chat store to a clean state,
    /// clears the remembered session from <see cref="UiShellState"/>, raises
    /// <see cref="StateChanged"/>, and kicks off a fresh onboarding fetch in
    /// the background.
    /// </summary>
    /// <remarks>
    /// This method intentionally does NOT await <see cref="ShowOnboardingStateAsync"/>
    /// because it is invoked from synchronous shell-action callbacks.  Fire-and-forget
    /// is safe here because <see cref="ShowOnboardingStateAsync"/> only performs
    /// tool calls and store mutations that are independently guarded.
    /// </remarks>
    internal void StartNewTask()
    {
        _chatStore.Reset();
        UiShellState.ClearSession();
        StateChanged?.Invoke();
        _ = ShowOnboardingStateAsync();
    }

    /// <summary>
    /// Sets the bootstrapped flag to <see langword="true"/>, preventing any
    /// subsequent call to <see cref="RestoreOrShowOnboardingAsync"/> from being
    /// triggered again for the lifetime of the current pane instance.
    /// </summary>
    internal void MarkBootstrapped()
    {
        _bootstrapped = true;
    }

    // -------------------------------------------------------------------------
    // Private implementation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to restore the mission identified by <paramref name="missionId"/>
    /// by fetching its latest snapshot from the WorkerHost.  On success the
    /// snapshot is applied to the chat store and ambient shell state is updated.
    /// On any failure the remembered mission identifier is cleared so that the
    /// next bootstrap pass does not retry a stale ID.
    /// </summary>
    /// <param name="missionId">The mission identifier to restore.</param>
    /// <returns>
    /// <see langword="true"/> when a non-empty mission was fetched and applied;
    /// <see langword="false"/> otherwise.
    /// </returns>
    private async Task<bool> TryRestoreMissionAsync(string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId))
        {
            return false;
        }

        try
        {
            var response = await _missionClient.GetMissionAsync(missionId, CancellationToken.None)
                .ConfigureAwait(true);

            ApplyMissionResponse(response);

            return !string.IsNullOrWhiteSpace(response.MissionId);
        }
        catch
        {
            // The mission may have been evicted from the WorkerHost; clear the
            // stale reference so we don't retry it on the next bootstrap.
            UiShellState.RememberMission(string.Empty);
            return false;
        }
    }

    /// <summary>
    /// Attempts to restore the session identified by <paramref name="sessionId"/>
    /// by fetching its persisted <see cref="WorkerResponse"/> via the internal
    /// tool client.  On success the response is seeded into the chat store,
    /// project dashboard state is refreshed, and ambient shell state is updated.
    /// On failure the remembered session identifier is cleared.
    /// </summary>
    /// <param name="sessionId">The session identifier to restore.</param>
    /// <returns>
    /// <see langword="true"/> when the session was successfully fetched and
    /// applied; <see langword="false"/> otherwise.
    /// </returns>
    private async Task<bool> TryRestoreSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var sessionResponse = await InternalToolClient.Instance.CallAsync(
            ToolNames.WorkerGetSession,
            JsonUtil.Serialize(new WorkerSessionRequest { SessionId = sessionId }),
            false).ConfigureAwait(true);

        if (!sessionResponse.Succeeded)
        {
            UiShellState.ClearSession(sessionId);
            return false;
        }

        var worker = JsonUtil.DeserializeRequired<WorkerResponse>(sessionResponse.PayloadJson);

        _chatStore.SeedFromWorkerResponse(worker);

        await _projectDashboard.RefreshProjectDashboardStateAsync(
            FirstNonEmpty(
                worker.WorkspaceId,
                worker.OnboardingStatus?.WorkspaceId,
                worker.ContextSummary?.WorkspaceId),
            null,
            null).ConfigureAwait(true);

        UiShellState.RememberSession(worker.SessionId);
        UiShellState.RememberMission(worker.MissionId);

        StateChanged?.Invoke();
        ResponseApplied?.Invoke(worker);

        return true;
    }

    /// <summary>
    /// Fetches the current worker context and project bundle via the internal
    /// tool client, synthesises a lightweight <see cref="WorkerResponse"/>
    /// representing the onboarding / empty-session state, seeds it into the
    /// chat store, and refreshes the project dashboard.
    /// </summary>
    /// <remarks>
    /// This path is taken when no prior session or mission can be restored, so
    /// the user sees a fully initialised onboarding card with live context
    /// (workspace, grounding level, etc.) already populated.
    /// </remarks>
    private async Task ShowOnboardingStateAsync()
    {
        var contextResponse = await InternalToolClient.Instance.CallAsync(
            ToolNames.WorkerGetContext,
            JsonUtil.Serialize(new WorkerContextRequest
            {
                SessionId = string.Empty,
                IncludeTaskContext = true,
                IncludeDeltaSummary = false,
                MaxRecentOperations = 1,
                MaxRecentEvents = 1
            }),
            false).ConfigureAwait(true);

        var context = contextResponse.Succeeded
            ? JsonUtil.DeserializeRequired<WorkerContextResponse>(contextResponse.PayloadJson)
            : new WorkerContextResponse();

        var workspaceId = FirstNonEmpty(UiShellState.CurrentWorkspaceId, context.WorkspaceId);

        ProjectContextBundleResponse? bundle = null;

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            var bundleResponse = await InternalToolClient.Instance.CallAsync(
                ToolNames.ProjectGetContextBundle,
                JsonUtil.Serialize(new ProjectContextBundleRequest
                {
                    WorkspaceId = workspaceId,
                    Query = string.Empty,
                    MaxSourceRefs = 4,
                    MaxStandardsRefs = 4
                }),
                false).ConfigureAwait(true);

            if (bundleResponse.Succeeded)
            {
                bundle = JsonUtil.DeserializeRequired<ProjectContextBundleResponse>(bundleResponse.PayloadJson);
            }
        }

        UiShellState.UpdateAmbient(workspaceId, bundle);

        var onboarding = new OnboardingStatusDto
        {
            WorkspaceId = FirstNonEmpty(bundle?.WorkspaceId, workspaceId),
            WorkspaceRootPath = bundle?.WorkspaceRootPath ?? string.Empty,
            InitStatus = bundle?.Exists == true
                ? ProjectOnboardingStatuses.Initialized
                : ProjectOnboardingStatuses.NotInitialized,
            DeepScanStatus = FirstNonEmpty(bundle?.DeepScanStatus, ProjectDeepScanStatuses.NotStarted),
            PrimaryModelStatus = FirstNonEmpty(
                bundle?.PrimaryModelStatus,
                context.ProjectPrimaryModelStatus,
                ProjectPrimaryModelStatuses.NotRequested),
            Summary = FirstNonEmpty(
                bundle?.Summary,
                context.ProjectSummary,
                "Chat is ready. Init workspace to enable project-aware context for this model.")
        };

        var worker = new WorkerResponse
        {
            SessionId = string.Empty,
            MissionId = string.Empty,
            MissionStatus = WorkerMissionStates.Idle,
            WorkspaceId = onboarding.WorkspaceId,
            OnboardingStatus = onboarding,
            ContextSummary = new WorkerContextSummary
            {
                DocumentKey = context.TaskContext?.Document?.DocumentKey ?? string.Empty,
                DocumentTitle = context.TaskContext?.Document?.Title ?? string.Empty,
                ActiveViewName = context.TaskContext?.ActiveContext?.ViewName ?? string.Empty,
                WorkspaceId = onboarding.WorkspaceId,
                ProjectPrimaryModelStatus = onboarding.PrimaryModelStatus,
                ProjectSummary = onboarding.Summary,
                GroundingLevel = bundle?.Exists == true
                    ? string.Equals(
                        bundle.DeepScanStatus,
                        ProjectDeepScanStatuses.Completed,
                        StringComparison.OrdinalIgnoreCase)
                        ? WorkerGroundingLevels.DeepScanGrounded
                        : WorkerGroundingLevels.WorkspaceGrounded
                    : WorkerGroundingLevels.LiveContextOnly,
                GroundingSummary = bundle?.Exists == true
                    ? FirstNonEmpty(bundle.DeepScanSummary, bundle.Summary, onboarding.Summary)
                    : "Dang dung live Revit context; project context chua init.",
                GroundingRefs = (bundle?.TopStandardsRefs ?? new List<ProjectContextRef>())
                    .Concat(bundle?.SourceRefs ?? new List<ProjectContextRef>())
                    .Select(x => FirstNonEmpty(x.RelativePath, x.Title, x.RefId))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(6)
                    .ToList()
            },
            ConfiguredProvider = _settings.LlmProviderLabel,
            PlannerModel = _settings.LlmPlannerModel,
            ResponseModel = _settings.LlmResponseModel
        };

        _chatStore.SeedFromWorkerResponse(worker);

        await _projectDashboard.RefreshProjectDashboardStateAsync(
            onboarding.WorkspaceId,
            bundle,
            null).ConfigureAwait(true);

        StateChanged?.Invoke();
        ResponseApplied?.Invoke(worker);
    }

    /// <summary>
    /// Applies a <see cref="WorkerHostMissionResponse"/> to the chat store,
    /// persists the session and mission identifiers in <see cref="UiShellState"/>,
    /// raises <see cref="StateChanged"/> and <see cref="ResponseApplied"/>, and
    /// starts or cancels the mission event stream as required by the mission's
    /// current state.
    /// </summary>
    /// <param name="response">The mission response to apply.</param>
    private void ApplyMissionResponse(WorkerHostMissionResponse response)
    {
        _chatStore.ApplyMissionResponse(response);

        UiShellState.RememberSession(_chatStore.SessionId);
        UiShellState.RememberMission(_chatStore.MissionId);

        StateChanged?.Invoke();
        ResponseApplied?.Invoke(_chatStore.LatestWorkerResponse);

        _streamService.StartMissionEventStreamIfNeeded(response);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the chat store is in a state that
    /// allows auto-resuming a previous session (i.e. no session is currently
    /// active and the store is either empty or shows the onboarding card).
    /// </summary>
    /// <param name="currentSessionId">
    /// The session identifier the caller already considers active, if any.
    /// When non-empty the method returns <see langword="false"/> immediately.
    /// </param>
    private bool CanAutoResumeLatestSession(string? currentSessionId)
    {
        // A session is already active — no resume needed.
        if (!string.IsNullOrWhiteSpace(FirstNonEmpty(currentSessionId, _chatStore.SessionId)))
        {
            return false;
        }

        var worker = _chatStore.LatestWorkerResponse ?? new WorkerResponse();

        // Resume is safe only when the store is brand-new (no messages) or is
        // currently showing the onboarding card.
        return (worker.Messages?.Count ?? 0) == 0 || worker.OnboardingStatus != null;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the first non-whitespace value from <paramref name="values"/>,
    /// or an empty string when all values are null or whitespace.
    /// </summary>
    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return string.Empty;
    }
}
