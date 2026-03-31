using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.UI;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Agent.UI.Components;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Tabs.Services;

/// <summary>
/// Orchestrates chat message submission for the Worker tab: guards against
/// concurrent sends, drives the chat-store state machine, delegates streaming
/// to <see cref="MissionStreamService"/>, and surfaces UI side-effects
/// exclusively through events so the service stays free of any WPF /
/// Dispatcher dependency.
/// </summary>
/// <remarks>
/// <para>
/// The critical difference from the original <c>WorkerTab.SendCurrentMessage</c>
/// is that submission is now expressed as <c>async Task</c> (
/// <see cref="SubmitMessageAsync"/>) instead of <c>async void</c>, which means
/// exceptions propagate correctly and callers can <c>await</c> completion.
/// </para>
/// <para>
/// All state changes that previously called <c>RenderCurrentState</c> now raise
/// <see cref="StateChanged"/>.  Input clearing raises <see cref="InputCleared"/>.
/// Toast notifications raise <see cref="ToastRequested"/>.  Callers (the UI layer)
/// subscribe to these events and marshal to the appropriate thread.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Service lives for the lifetime of the dockable pane.")]
internal sealed class ChatSubmissionService
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
    /// Raised whenever submission state changes (busy flag, store contents,
    /// failure messages).  Subscribers should refresh their UI representation of
    /// the current <see cref="ChatSessionStore"/> state.
    /// </summary>
    internal event Action? StateChanged;

    /// <summary>
    /// Raised after a mission response has been applied to the chat store.
    /// The payload is the <see cref="WorkerResponse"/> that was derived from
    /// the response — suitable for broadcasting to external state observers.
    /// </summary>
    internal event Action<WorkerResponse>? ResponseApplied;

    /// <summary>
    /// Raised when the service wants to display a toast notification.
    /// The first argument is the message text; the second is the severity level.
    /// </summary>
    internal event Action<string, ToastType>? ToastRequested;

    /// <summary>
    /// Raised when the composer input should be cleared (i.e. the message has
    /// been accepted and submission has started).
    /// </summary>
    internal event Action? InputCleared;

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Gets a value indicating whether a request is currently in flight.</summary>
    internal bool IsBusy => _isBusy;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="ChatSubmissionService"/>.
    /// </summary>
    /// <param name="missionClient">HTTP client used to contact the WorkerHost mission API.</param>
    /// <param name="chatStore">Session store that accumulates mission events and snapshots.</param>
    /// <param name="settings">Agent configuration (timeouts, endpoints, feature flags).</param>
    /// <param name="streamService">Service that manages the SSE mission-event stream lifecycle.</param>
    internal ChatSubmissionService(
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
    /// Submits <paramref name="message"/> to the WorkerHost as a new chat turn.
    /// The call is a no-op when <see cref="IsBusy"/> is <see langword="true"/>
    /// or when <paramref name="message"/> is null / whitespace.
    /// </summary>
    /// <param name="message">The raw text entered by the user.</param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the round-trip (including the
    /// initial HTTP response) has finished — successfully or with a handled
    /// failure.  The task never faults; exceptions are surfaced via
    /// <see cref="ToastRequested"/> and <see cref="StateChanged"/>.
    /// </returns>
    /// <remarks>
    /// This method replaces the original <c>async void SendCurrentMessage</c>.
    /// Changing the return type to <see cref="Task"/> ensures that unhandled
    /// exceptions are not silently swallowed and that callers can track
    /// completion without resorting to synchronisation hacks.
    /// </remarks>
    internal async Task SubmitMessageAsync(string message)
    {
        var trimmedMessage = (message ?? string.Empty).Trim();

        if (_isBusy || string.IsNullOrWhiteSpace(trimmedMessage))
        {
            return;
        }

        var missionId = Guid.NewGuid().ToString("N");

        _isBusy = true;
        UiShellState.SetShellMode(UiShellState.ShellModes.Waiting);

        _chatStore.BeginUserTurn(trimmedMessage);
        _chatStore.BeginMissionStream(missionId);

        InputCleared?.Invoke();
        StateChanged?.Invoke();

        _requestCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(30, _settings.RequestTimeoutSeconds)));

        try
        {
            await _missionClient.EnsureAvailableAsync(_requestCts.Token).ConfigureAwait(true);

            _streamService.StartMissionEventStream(missionId);

            var response = await _missionClient.SubmitChatAsync(
                new WorkerHostChatRequest
                {
                    MissionId = missionId,
                    SessionId = _chatStore.SessionId,
                    Message = trimmedMessage,
                    PersonaId = _settings.DefaultWorkerProfile?.PersonaId ?? WorkerPersonas.RevitWorker,
                    ContinueMission = true
                },
                _requestCts.Token).ConfigureAwait(true);

            ApplyMissionResponse(response);
            UiShellState.SetShellMode(UiShellState.ShellModes.Transcript);
        }
        catch (Exception ex)
        {
            _streamService.CancelMissionEventStream();

            HandleMissionFailure(ex.Message);
            UiShellState.SetShellMode(string.IsNullOrWhiteSpace(UiShellState.CurrentWorkspaceId)
                ? UiShellState.ShellModes.Onboarding
                : UiShellState.ShellModes.Dashboard);
        }
        finally
        {
            _isBusy = false;

            _requestCts?.Dispose();
            _requestCts = null;

            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Cancels any in-flight request by signalling its
    /// <see cref="CancellationTokenSource"/>.  Safe to call when no request is
    /// active.
    /// </summary>
    internal void CancelPendingRequest()
    {
        try
        {
            _requestCts?.Cancel();
        }
        catch
        {
            // Ignore ObjectDisposedException or any race-condition exceptions.
        }
    }

    // -------------------------------------------------------------------------
    // Private implementation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies a successful <see cref="WorkerHostMissionResponse"/> to the chat
    /// store, persists the session and mission identifiers in
    /// <see cref="UiShellState"/>, raises <see cref="StateChanged"/> so the UI
    /// refreshes, and conditionally starts a new mission event stream via
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

    /// <summary>
    /// Builds a synthetic failure record in the chat store so the user sees the
    /// error inline, raises <see cref="ToastRequested"/> with
    /// <see cref="ToastType.Error"/> severity, and raises
    /// <see cref="StateChanged"/> so the UI reflects the terminal state.
    /// </summary>
    /// <param name="message">
    /// The error text to display.  When null or whitespace the fallback
    /// <c>"WorkerHost request failed."</c> is used.
    /// </param>
    private void HandleMissionFailure(string message)
    {
        ToastRequested?.Invoke(
            FirstNonEmpty(message, "WorkerHost request failed."),
            ToastType.Error);

        var worker = _chatStore.LatestWorkerResponse ?? new WorkerResponse();
        worker.Messages ??= new List<WorkerChatMessage>();

        var pendingUserMessage = _chatStore.PendingUserMessage;
        var lastUserMessage = worker.Messages
            .LastOrDefault(x => string.Equals(x.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase))
            ?.Content;

        // Re-append the pending user message if it was not already committed to
        // the message list (e.g. the request failed before the server echoed it).
        if (!string.IsNullOrWhiteSpace(pendingUserMessage)
            && !string.Equals(
                (lastUserMessage ?? string.Empty).Trim(),
                pendingUserMessage.Trim(),
                StringComparison.Ordinal))
        {
            worker.Messages.Add(new WorkerChatMessage
            {
                Role = WorkerMessageRoles.User,
                Content = pendingUserMessage,
                TimestampUtc = DateTime.UtcNow
            });
        }

        worker.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.System,
            Content = FirstNonEmpty(message, "WorkerHost request failed."),
            TimestampUtc = DateTime.UtcNow,
            StatusCode = "MISSION_ERROR"
        });

        _chatStore.SeedFromWorkerResponse(worker);

        _chatStore.ApplyMissionResponse(new WorkerHostMissionResponse
        {
            SessionId = _chatStore.SessionId,
            MissionId = _chatStore.MissionId,
            State = WorkerMissionStates.Blocked,
            Succeeded = false,
            StatusCode = "MISSION_ERROR",
            ResponseText = message
        });

        StateChanged?.Invoke();
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
