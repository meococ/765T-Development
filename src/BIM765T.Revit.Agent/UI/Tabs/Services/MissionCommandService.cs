using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.UI;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Agent.UI.Components;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Tabs.Services;

/// <summary>
/// Handles the approve / reject / resume mission commands for the Worker tab.
/// Each command follows the same round-trip pattern: build a
/// <see cref="WorkerHostMissionCommandRequest"/> from the current chat-store
/// state, POST to the appropriate WorkerHost endpoint, apply the response back
/// into the store, and restart the mission event stream when needed.
/// </summary>
/// <remarks>
/// <para>
/// All WPF / Dispatcher concerns are deliberately excluded. UI side-effects are
/// surfaced exclusively via <see cref="StateChanged"/>, <see cref="ResponseApplied"/>,
/// and <see cref="ToastRequested"/> so that callers can marshal to the appropriate
/// thread without coupling this service to <c>System.Windows</c>.
/// </para>
/// <para>
/// The public entry points (<see cref="ApproveAsync"/>, <see cref="RejectAsync"/>,
/// <see cref="ResumeAsync"/>) are thin wrappers that forward to the shared private
/// implementation <see cref="ExecuteMissionCommandAsync"/>. This mirrors the
/// original <c>WorkerTab.HandleSystemAction</c> → <c>ExecuteMissionCommandAsync</c>
/// dispatch, but expressed as properly awaitable <c>Task</c>-returning methods.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Service lives for the lifetime of the dockable pane.")]
internal sealed class MissionCommandService
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly WorkerHostMissionClient _missionClient;
    private readonly ChatSessionStore _chatStore;
    private readonly AgentSettings _settings;
    private readonly MissionStreamService _streamService;

    private CancellationTokenSource? _requestCts;
    private bool _isBusy;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised whenever the busy flag changes (at the start and end of every
    /// command) so that subscribers can refresh their representation of the
    /// current <see cref="ChatSessionStore"/> state.
    /// </summary>
    internal event Action? StateChanged;

    /// <summary>
    /// Raised after a successful command round-trip and the resulting
    /// <see cref="WorkerHostMissionResponse"/> has been applied to the chat
    /// store.  The payload is the <see cref="WorkerResponse"/> snapshot that
    /// was derived from the response — suitable for broadcasting to external
    /// state observers.
    /// </summary>
    internal event Action<WorkerResponse>? ResponseApplied;

    /// <summary>
    /// Raised when the service wants to display a toast notification.
    /// The first argument is the message text; the second is the severity level.
    /// </summary>
    internal event Action<string, ToastType>? ToastRequested;

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Gets a value indicating whether a command is currently in flight.</summary>
    internal bool IsBusy => _isBusy;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="MissionCommandService"/>.
    /// </summary>
    /// <param name="missionClient">HTTP client used to contact the WorkerHost mission API.</param>
    /// <param name="chatStore">Session store that holds the current mission and approval token.</param>
    /// <param name="settings">Agent configuration (timeouts, endpoints, feature flags).</param>
    /// <param name="streamService">Service that manages the SSE mission-event stream lifecycle.</param>
    internal MissionCommandService(
        WorkerHostMissionClient missionClient,
        ChatSessionStore chatStore,
        AgentSettings settings,
        MissionStreamService streamService)
    {
        _missionClient = missionClient ?? throw new ArgumentNullException(nameof(missionClient));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _streamService = streamService ?? throw new ArgumentNullException(nameof(streamService));
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends an <c>approve</c> command for the currently active mission.
    /// The call is a no-op when <see cref="IsBusy"/> is <see langword="true"/>
    /// or when there is no active mission in the chat store.
    /// </summary>
    /// <returns>
    /// A <see cref="Task"/> that completes when the round-trip has finished —
    /// successfully or with a handled failure surfaced via
    /// <see cref="ToastRequested"/>.
    /// </returns>
    internal Task ApproveAsync() => ExecuteMissionCommandAsync("approve");

    /// <summary>
    /// Sends a <c>reject</c> command for the currently active mission.
    /// The call is a no-op when <see cref="IsBusy"/> is <see langword="true"/>
    /// or when there is no active mission in the chat store.
    /// </summary>
    /// <returns>
    /// A <see cref="Task"/> that completes when the round-trip has finished —
    /// successfully or with a handled failure surfaced via
    /// <see cref="ToastRequested"/>.
    /// </returns>
    internal Task RejectAsync() => ExecuteMissionCommandAsync("reject");

    /// <summary>
    /// Sends a <c>resume</c> command for the currently active mission.
    /// The call is a no-op when <see cref="IsBusy"/> is <see langword="true"/>
    /// or when there is no active mission in the chat store.
    /// </summary>
    /// <returns>
    /// A <see cref="Task"/> that completes when the round-trip has finished —
    /// successfully or with a handled failure surfaced via
    /// <see cref="ToastRequested"/>.
    /// </returns>
    internal Task ResumeAsync() => ExecuteMissionCommandAsync("resume");

    // -------------------------------------------------------------------------
    // Core implementation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends a mission command to the WorkerHost and applies the response to
    /// the chat store.
    /// </summary>
    /// <param name="commandName">
    /// One of <c>"approve"</c>, <c>"reject"</c>, or <c>"resume"</c>.  Any
    /// unrecognised value is treated as <c>"resume"</c> (matches the original
    /// <c>switch</c> default in <c>WorkerTab.ExecuteMissionCommandAsync</c>).
    /// </param>
    /// <remarks>
    /// <list type="number">
    ///   <item>Returns immediately when <see cref="IsBusy"/> is <see langword="true"/>
    ///   or when the chat store holds no active mission identifier.</item>
    ///   <item>Sets <see cref="IsBusy"/> and raises <see cref="StateChanged"/> so
    ///   the UI can disable command buttons while the request is in flight.</item>
    ///   <item>Creates a cancellation token scoped to
    ///   <see cref="AgentSettings.RequestTimeoutSeconds"/> (minimum 30 s).</item>
    ///   <item>On success: applies the response via <see cref="ApplyMissionResponse"/>
    ///   which raises <see cref="ResponseApplied"/> and restarts the event stream
    ///   when needed.</item>
    ///   <item>On failure: raises <see cref="ToastRequested"/> with
    ///   <see cref="ToastType.Error"/> so the caller can surface the error inline.</item>
    ///   <item>Always clears <see cref="IsBusy"/>, disposes the CTS, and raises
    ///   <see cref="StateChanged"/> in the <c>finally</c> block.</item>
    /// </list>
    /// </remarks>
    private async Task ExecuteMissionCommandAsync(string commandName)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(_chatStore.MissionId))
        {
            return;
        }

        _isBusy = true;
        _chatStore.SetBusy(true);
        StateChanged?.Invoke();

        _requestCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(30, _settings.RequestTimeoutSeconds)));

        try
        {
            var request = new WorkerHostMissionCommandRequest
            {
                SessionId = _chatStore.SessionId,
                ApprovalToken = _chatStore.LatestMissionResponse.ApprovalToken,
                PreviewRunId = _chatStore.LatestMissionResponse.PreviewRunId
            };

            WorkerHostMissionResponse response;

            switch (commandName)
            {
                case "approve":
                    response = await _missionClient
                        .ApproveAsync(_chatStore.MissionId, request, _requestCts.Token)
                        .ConfigureAwait(true);
                    break;

                case "reject":
                    response = await _missionClient
                        .RejectAsync(_chatStore.MissionId, request, _requestCts.Token)
                        .ConfigureAwait(true);
                    break;

                default:
                    response = await _missionClient
                        .ResumeAsync(_chatStore.MissionId, request, _requestCts.Token)
                        .ConfigureAwait(true);
                    break;
            }

            ApplyMissionResponse(response);
        }
        catch (Exception ex)
        {
            ToastRequested?.Invoke(
                string.IsNullOrWhiteSpace(ex.Message) ? "WorkerHost request failed." : ex.Message,
                ToastType.Error);
        }
        finally
        {
            _isBusy = false;
            _chatStore.SetBusy(false);

            _requestCts?.Dispose();
            _requestCts = null;

            StateChanged?.Invoke();
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies a successful <see cref="WorkerHostMissionResponse"/> to the chat
    /// store, persists the session and mission identifiers in
    /// <see cref="UiShellState"/>, raises <see cref="StateChanged"/> so the UI
    /// refreshes, raises <see cref="ResponseApplied"/> with the latest worker
    /// response snapshot, and conditionally restarts the mission event stream via
    /// <see cref="MissionStreamService.StartMissionEventStreamIfNeeded"/>.
    /// </summary>
    /// <param name="response">The mission response returned by the WorkerHost.</param>
    private void ApplyMissionResponse(WorkerHostMissionResponse response)
    {
        _chatStore.ApplyMissionResponse(response);

        UiShellState.RememberSession(_chatStore.SessionId);
        UiShellState.RememberMission(_chatStore.MissionId);

        StateChanged?.Invoke();
        ResponseApplied?.Invoke(_chatStore.LatestWorkerResponse);

        _streamService.StartMissionEventStreamIfNeeded(response);
    }
}
