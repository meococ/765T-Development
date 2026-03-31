using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Agent.UI.Components;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Tabs.Services;

/// <summary>
/// Manages the SSE mission-event stream lifecycle for a single worker session.
/// Owns the <see cref="CancellationTokenSource"/> and reconnect-attempt counter,
/// and surfaces UI side-effects exclusively through events so the service stays
/// free of any WPF / Dispatcher dependency.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Service lives for the lifetime of the dockable pane.")]
internal sealed class MissionStreamService
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly WorkerHostMissionClient _missionClient;
    private readonly ChatSessionStore _chatStore;
    private readonly AgentSettings _settings;

    private CancellationTokenSource? _missionStreamCts;
    private int _missionStreamReconnectAttempts;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised on the calling thread each time a streaming event arrives from the
    /// WorkerHost.  Subscribers are responsible for marshalling to the UI thread
    /// when updating visual state.
    /// </summary>
    internal event Action<WorkerHostMissionEvent>? MissionEventReceived;

    /// <summary>
    /// Raised after <see cref="RefreshMissionSnapshotAsync"/> successfully
    /// fetches and applies a full mission snapshot.  Subscribers should use the
    /// payload to refresh the UI in one atomic pass.
    /// </summary>
    internal event Action<WorkerHostMissionResponse>? SnapshotReceived;

    /// <summary>
    /// Raised when the service wants to display a toast notification.
    /// The first argument is the message text; the second is the severity level.
    /// </summary>
    internal event Action<string, ToastType>? ToastRequested;

    /// <summary>
    /// Raised when the stream reaches a terminal state or reconnect attempts are
    /// exhausted, signalling that no further events will arrive on this stream.
    /// </summary>
    internal event Action? StreamEnded;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="MissionStreamService"/>.
    /// </summary>
    /// <param name="missionClient">HTTP client used to contact the WorkerHost mission API.</param>
    /// <param name="chatStore">Session store that accumulates mission events and snapshots.</param>
    /// <param name="settings">Agent configuration (timeouts, endpoints, feature flags).</param>
    internal MissionStreamService(
        WorkerHostMissionClient missionClient,
        ChatSessionStore chatStore,
        AgentSettings settings)
    {
        _missionClient = missionClient ?? throw new ArgumentNullException(nameof(missionClient));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cancels any active stream and, when the mission represented by
    /// <paramref name="response"/> is still running, immediately starts a new
    /// one.  Missions that are already in a terminal state or awaiting approval
    /// are silently skipped.
    /// </summary>
    /// <param name="response">The latest mission snapshot from the WorkerHost.</param>
    internal void StartMissionEventStreamIfNeeded(WorkerHostMissionResponse response)
    {
        CancelMissionEventStream();
        _missionStreamReconnectAttempts = 0;

        if (string.IsNullOrWhiteSpace(response.MissionId)
            || string.IsNullOrWhiteSpace(response.State)
            || string.Equals(response.State, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(response.State, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(response.State, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase)
            || response.HasPendingApproval)
        {
            return;
        }

        _missionStreamCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _ = StreamMissionEventsAsync(response.MissionId, _missionStreamCts.Token);
    }

    /// <summary>
    /// Cancels any active stream and starts a fresh one for the given
    /// <paramref name="missionId"/>.
    /// </summary>
    /// <param name="missionId">The mission identifier to stream events for.</param>
    /// <param name="resetReconnectAttempts">
    /// When <see langword="true"/> (the default) the reconnect counter is reset
    /// to zero.  Pass <see langword="false"/> when restarting after a transient
    /// error so that the back-off counter is preserved.
    /// </param>
    internal void StartMissionEventStream(string missionId, bool resetReconnectAttempts = true)
    {
        CancelMissionEventStream();

        if (resetReconnectAttempts)
        {
            _missionStreamReconnectAttempts = 0;
        }

        if (string.IsNullOrWhiteSpace(missionId))
        {
            return;
        }

        _missionStreamCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _ = StreamMissionEventsAsync(missionId, _missionStreamCts.Token);
    }

    /// <summary>
    /// Cancels the active mission event stream and disposes the underlying
    /// <see cref="CancellationTokenSource"/>.  Safe to call when no stream is
    /// active.
    /// </summary>
    internal void CancelMissionEventStream()
    {
        try
        {
            _missionStreamCts?.Cancel();
        }
        catch
        {
            // Ignore ObjectDisposedException or any race-condition exceptions.
        }
        finally
        {
            _missionStreamCts?.Dispose();
            _missionStreamCts = null;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="eventType"/> indicates
    /// that the full mission snapshot must be re-fetched to correctly represent
    /// the new state (e.g. approval checkpoints, terminal transitions).
    /// </summary>
    /// <param name="eventType">The <c>EventType</c> string carried by the mission event.</param>
    internal static bool RequiresSnapshotHydration(string? eventType)
    {
        return string.Equals(eventType, "ApprovalRequested", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "TaskBlocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "TaskCanceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "TaskCompleted", StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Private implementation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Core streaming loop.  Opens the SSE connection to the WorkerHost for
    /// <paramref name="missionId"/>, applies each arriving event to the chat
    /// store, and raises <see cref="MissionEventReceived"/> so that the UI layer
    /// can schedule a render.  On terminal events the snapshot is refreshed and
    /// <see cref="StreamEnded"/> is raised.  Transient errors trigger up to two
    /// automatic reconnect attempts with exponential back-off before surfacing a
    /// <see cref="ToastRequested"/> warning.
    /// </summary>
    private async Task StreamMissionEventsAsync(string missionId, CancellationToken cancellationToken)
    {
        try
        {
            await _missionClient.StreamMissionEventsAsync(
                missionId,
                async missionEvent =>
                {
                    // Apply directly — no Dispatcher, subscribers marshal if needed.
                    _chatStore.ApplyMissionEvent(missionEvent);
                    MissionEventReceived?.Invoke(missionEvent);

                    if (missionEvent.Terminal)
                    {
                        await RefreshMissionSnapshotAsync(missionId, cancelStreamAfterHydration: true).ConfigureAwait(false);
                        return;
                    }

                    if (RequiresSnapshotHydration(missionEvent.EventType))
                    {
                        await RefreshMissionSnapshotAsync(missionId, cancelStreamAfterHydration: true).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation — no action needed.
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested
                && _missionStreamReconnectAttempts < 2)
            {
                _missionStreamReconnectAttempts++;
                ToastRequested?.Invoke("Đang reconnect luồng event AI...", ToastType.Info);

                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(500 * _missionStreamReconnectAttempts),
                        cancellationToken).ConfigureAwait(false);

                    StartMissionEventStream(missionId, resetReconnectAttempts: false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    // Cancelled during back-off delay — exit cleanly.
                }
            }

            ToastRequested?.Invoke(
                FirstNonEmpty(ex.Message, "Mission event stream failed."),
                ToastType.Warning);

            StreamEnded?.Invoke();
        }
    }

    /// <summary>
    /// Fetches the full mission snapshot from the WorkerHost and raises
    /// <see cref="SnapshotReceived"/> so the UI layer can apply it atomically.
    /// When <paramref name="cancelStreamAfterHydration"/> is <see langword="true"/>
    /// the active stream is cancelled after the snapshot is applied, preventing
    /// further events from arriving after a terminal transition.
    /// </summary>
    private async Task RefreshMissionSnapshotAsync(string missionId, bool cancelStreamAfterHydration)
    {
        try
        {
            var snapshot = await _missionClient.GetMissionAsync(missionId, CancellationToken.None).ConfigureAwait(false);
            SnapshotReceived?.Invoke(snapshot);
        }
        catch
        {
            // Best-effort — silently swallow transient fetch failures.
        }
        finally
        {
            if (cancelStreamAfterHydration)
            {
                CancelMissionEventStream();
                StreamEnded?.Invoke();
            }
        }
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
