using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
/// Owns all project-workspace and dashboard lifecycle logic for the Worker tab.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates:
/// <list type="bullet">
///   <item>Project init preview + apply flow (<see cref="RunProjectInitApplyAsync"/>).</item>
///   <item>Workspace initialization via the WorkerHost project API (<see cref="InitializeWorkspaceAsync"/>).</item>
///   <item>Project deep-scan execution (<see cref="RunProjectDeepScanAsync"/>).</item>
///   <item>Dashboard state caching and async refresh (<see cref="CacheProjectDashboardState"/>,
///     <see cref="RefreshProjectDashboardStateAsync"/>).</item>
///   <item>Local worker-response mutation helpers consumed by the above flows.</item>
/// </list>
/// </para>
/// <para>
/// All WPF / Dispatcher concerns are deliberately excluded.  UI side-effects are
/// surfaced via <see cref="StateChanged"/>, <see cref="ToastRequested"/>, and
/// <see cref="ResponseApplied"/> so that callers can marshal to the correct thread
/// without coupling this service to <c>System.Windows</c>.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Service lives for the lifetime of the dockable pane.")]
internal sealed class ProjectDashboardService
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly WorkerHostMissionClient _missionClient;
    private readonly ChatSessionStore _chatStore;
    private readonly AgentSettings _settings;

    private ProjectContextBundleResponse? _projectDashboardBundle;
    private ProjectDeepScanReportResponse? _projectDashboardReport;
    private CancellationTokenSource? _requestCts;
    private bool _isBusy;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised whenever the service mutates state that requires the UI to re-render
    /// (replaces all direct <c>RenderCurrentState()</c> calls from the original tab).
    /// </summary>
    internal event Action? StateChanged;

    /// <summary>
    /// Raised when the service wants to display a toast notification.
    /// The first argument is the message text; the second is the severity level.
    /// </summary>
    internal event Action<string, ToastType>? ToastRequested;

    /// <summary>
    /// Raised after a <see cref="WorkerResponse"/> has been applied to the chat
    /// store so that external state observers (e.g. the project-brief card) can
    /// refresh their presentation.
    /// </summary>
    internal event Action<WorkerResponse>? ResponseApplied;

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Gets the last successfully fetched project context bundle, or <c>null</c> if none.</summary>
    internal ProjectContextBundleResponse? CachedBundle => _projectDashboardBundle;

    /// <summary>Gets the last successfully fetched deep-scan report, or <c>null</c> if none.</summary>
    internal ProjectDeepScanReportResponse? CachedReport => _projectDashboardReport;

    /// <summary>Gets a value indicating whether a tool action is currently in flight.</summary>
    internal bool IsBusy => _isBusy;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="ProjectDashboardService"/>.
    /// </summary>
    /// <param name="missionClient">HTTP client used to contact the WorkerHost project API.</param>
    /// <param name="chatStore">Session store holding the current worker response and session context.</param>
    /// <param name="settings">Agent configuration (timeouts, LLM provider labels, feature flags).</param>
    internal ProjectDashboardService(
        WorkerHostMissionClient missionClient,
        ChatSessionStore chatStore,
        AgentSettings settings)
    {
        _missionClient = missionClient ?? throw new ArgumentNullException(nameof(missionClient));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // -------------------------------------------------------------------------
    // Public surface — primary entry points called from WorkerTab
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs the lightweight two-phase project-init flow that operates entirely
    /// via <see cref="InternalToolClient"/> (no WorkerHost HTTP round-trip).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Phase 1 — Preview:</b> calls <c>ProjectInitPreview</c> to determine
    /// whether a workspace already exists for the active model's source directory.
    /// If the workspace is already initialised, the existing context bundle is
    /// fetched and applied locally, then the method returns early.
    /// </para>
    /// <para>
    /// <b>Phase 2 — Apply:</b> calls <c>ProjectInitApply</c> to write the
    /// workspace manifest, include the live primary-model summary, and bind the
    /// new workspace to the chat session.
    /// </para>
    /// <para>
    /// The method is intentionally guarded by <see cref="IsBusy"/>: concurrent
    /// invocations are silently dropped.
    /// </para>
    /// </remarks>
    internal async Task RunProjectInitApplyAsync()
    {
        if (_isBusy)
        {
            return;
        }

        BeginLocalToolAction();
        try
        {
            // ---- Phase 1: resolve document context ----
            var (primaryRevitFilePath, sourceRootPath, workspaceIdHint, displayName) =
                await ResolveInitPrerequisitesAsync().ConfigureAwait(true);

            // ---- Phase 2: preview ----
            var (preview, effectiveWorkspaceId) =
                await RunInitPreviewPhaseAsync(primaryRevitFilePath, sourceRootPath, workspaceIdHint, displayName)
                    .ConfigureAwait(true);

            // ---- Early exit: workspace already initialised ----
            if (preview.WorkspaceExists
                || string.Equals(
                    preview.OnboardingStatus.InitStatus,
                    ProjectOnboardingStatuses.Initialized,
                    StringComparison.OrdinalIgnoreCase))
            {
                await ApplyExistingWorkspaceAsync(effectiveWorkspaceId).ConfigureAwait(true);
                return;
            }

            // ---- Phase 3: apply ----
            await RunInitApplyPhaseAsync(primaryRevitFilePath, sourceRootPath, effectiveWorkspaceId, displayName)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportLocalActionFailure(
                FirstNonEmpty(ex.Message, "Workspace initialization failed."),
                "PROJECT_INIT_ERROR");
        }
        finally
        {
            EndLocalToolAction();
        }
    }

    /// <summary>
    /// Initialises the workspace for the currently active Revit document via the
    /// WorkerHost project HTTP API.  Delegates to <see cref="RunProjectActionAsync"/>
    /// so busy-guarding, cancellation, and failure handling are all centralised.
    /// </summary>
    internal async Task InitializeWorkspaceAsync()
    {
        await RunProjectActionAsync(async cancellationToken =>
        {
            var document = await ResolveActiveDocumentAsync().ConfigureAwait(true);
            if (document == null)
            {
                throw new InvalidOperationException(
                    "Khong lay duoc active document de chay project init.");
            }

            if (string.IsNullOrWhiteSpace(document.PathName))
            {
                throw new InvalidOperationException(
                    "Model hien tai chua co file path. Hay save model truoc khi init workspace.");
            }

            var sourceRootPath = Path.GetDirectoryName(document.PathName);
            if (string.IsNullOrWhiteSpace(sourceRootPath))
            {
                throw new InvalidOperationException(
                    "Khong resolve duoc source root tu model hien tai.");
            }

            var preferredWorkspaceId = NormalizePreferredWorkspaceId(UiShellState.CurrentWorkspaceId);

            var preview = await _missionClient.PreviewProjectInitAsync(
                new ProjectInitPreviewRequest
                {
                    SourceRootPath = sourceRootPath,
                    WorkspaceId = preferredWorkspaceId,
                    DisplayName = FirstNonEmpty(Path.GetFileName(sourceRootPath), document.Title),
                    PrimaryRevitFilePath = document.PathName
                },
                cancellationToken).ConfigureAwait(true);

            if (!preview.IsValid)
            {
                throw new InvalidOperationException(
                    FirstNonEmpty(
                        preview.Summary,
                        preview.Errors.FirstOrDefault(),
                        "project.init_preview bi block."));
            }

            var workspaceId = FirstNonEmpty(
                preferredWorkspaceId,
                preview.SuggestedWorkspaceId,
                preview.WorkspaceId);

            var apply = await _missionClient.ApplyProjectInitAsync(
                new ProjectInitApplyRequest
                {
                    SourceRootPath = sourceRootPath,
                    WorkspaceId = workspaceId,
                    DisplayName = FirstNonEmpty(
                        preview.WorkspaceId,
                        preview.SuggestedWorkspaceId,
                        Path.GetFileName(sourceRootPath),
                        document.Title),
                    FirmPackIds = preview.FirmPackIds?.ToList() ?? new List<string>(),
                    PrimaryRevitFilePath = document.PathName,
                    IncludeLivePrimaryModelSummary = true,
                    AllowExistingWorkspaceOverwrite = false
                },
                cancellationToken).ConfigureAwait(true);

            ApplyProjectOperationState(
                FirstNonEmpty(
                    apply.Summary,
                    $"Workspace `{apply.WorkspaceId}` da duoc init cho model hien tai."),
                BuildProjectContextSummary(document, apply.ContextBundle, apply.OnboardingStatus),
                apply.OnboardingStatus,
                BuildProjectToolCard(
                    ToolNames.ProjectInitApply,
                    apply.StatusCode,
                    apply.Summary,
                    apply,
                    apply.ProjectContextPath,
                    apply.ManifestReportPath,
                    apply.SummaryReportPath,
                    apply.ProjectBriefPath,
                    apply.PrimaryModelReportPath),
                apply.ProjectContextPath,
                apply.ManifestReportPath,
                apply.SummaryReportPath,
                apply.ProjectBriefPath,
                apply.PrimaryModelReportPath);

            await RefreshProjectDashboardStateAsync(apply.WorkspaceId, apply.ContextBundle, null)
                .ConfigureAwait(true);

        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs a project deep scan for the currently active workspace via the
    /// WorkerHost project HTTP API.  Requires the workspace to have been
    /// initialised first (<see cref="InitializeWorkspaceAsync"/>).
    /// </summary>
    internal async Task RunProjectDeepScanAsync()
    {
        await RunProjectActionAsync(async cancellationToken =>
        {
            var document = await ResolveActiveDocumentAsync().ConfigureAwait(true);
            if (document == null)
            {
                throw new InvalidOperationException(
                    "Khong lay duoc active document de chay project deep scan.");
            }

            var workspaceId = FirstNonEmpty(
                NormalizePreferredWorkspaceId(UiShellState.CurrentWorkspaceId),
                _chatStore.LatestWorkerResponse?.OnboardingStatus?.WorkspaceId,
                _chatStore.LatestWorkerResponse?.WorkspaceId);

            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                throw new InvalidOperationException(
                    "Workspace chua init. Bam Init workspace truoc khi chay deep scan.");
            }

            var response = await _missionClient.RunProjectDeepScanAsync(
                workspaceId,
                new ProjectDeepScanRequest { WorkspaceId = workspaceId },
                cancellationToken).ConfigureAwait(true);

            ApplyProjectOperationState(
                FirstNonEmpty(
                    response.Summary,
                    response.Report?.Summary,
                    $"Project Brain deep scan da hoan tat cho workspace `{workspaceId}`."),
                BuildProjectContextSummary(document, response.ContextBundle, response.OnboardingStatus),
                response.OnboardingStatus,
                BuildProjectToolCard(
                    ToolNames.ProjectDeepScan,
                    response.StatusCode,
                    response.Summary,
                    response,
                    response.ReportPath,
                    response.SummaryReportPath),
                response.ReportPath,
                response.SummaryReportPath);

            await RefreshProjectDashboardStateAsync(
                    response.WorkspaceId,
                    response.ContextBundle,
                    BuildProjectDashboardReportResponse(response))
                .ConfigureAwait(true);

        }).ConfigureAwait(true);
    }

    // -------------------------------------------------------------------------
    // Dashboard state management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes bundle and report snapshots into the in-memory dashboard cache,
    /// evicting stale entries when the effective workspace changes.
    /// </summary>
    /// <param name="workspaceId">
    /// Hint workspace identifier; the effective ID is resolved from
    /// <paramref name="bundle"/> and <paramref name="report"/> first.
    /// </param>
    /// <param name="bundle">New context bundle snapshot, or <c>null</c> to keep any existing entry.</param>
    /// <param name="report">New deep-scan report snapshot, or <c>null</c> to keep any existing entry.</param>
    internal void CacheProjectDashboardState(
        string? workspaceId,
        ProjectContextBundleResponse? bundle,
        ProjectDeepScanReportResponse? report)
    {
        var effectiveWorkspaceId = FirstNonEmpty(
            bundle?.WorkspaceId,
            report?.WorkspaceId,
            workspaceId);

        if (string.IsNullOrWhiteSpace(effectiveWorkspaceId))
        {
            if (bundle == null && report == null)
            {
                _projectDashboardBundle = null;
                _projectDashboardReport = null;
            }

            // Signal consumers that render state may have changed.
            StateChanged?.Invoke();
            return;
        }

        if (bundle != null)
        {
            _projectDashboardBundle = bundle;
        }
        else if (!string.Equals(
            _projectDashboardBundle?.WorkspaceId,
            effectiveWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            _projectDashboardBundle = null;
        }

        if (report != null)
        {
            _projectDashboardReport = report;
        }
        else if (!string.Equals(
            _projectDashboardReport?.WorkspaceId,
            effectiveWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            _projectDashboardReport = null;
        }

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Refreshes the dashboard cache, optionally fetching a live bundle and/or
    /// deep-scan report from the WorkerHost when <paramref name="preferBundle"/>
    /// or <paramref name="preferReport"/> are <c>null</c>.
    /// </summary>
    /// <param name="workspaceId">Workspace identifier hint.</param>
    /// <param name="preferBundle">
    /// A freshly obtained bundle to write directly into the cache, bypassing the
    /// live fetch.  Pass <c>null</c> to trigger a live fetch when the cache is
    /// empty or stale.
    /// </param>
    /// <param name="preferReport">
    /// A freshly obtained report to write directly into the cache.  Pass <c>null</c>
    /// to trigger a live fetch when a completed deep scan is known to exist.
    /// </param>
    internal async Task RefreshProjectDashboardStateAsync(
        string? workspaceId,
        ProjectContextBundleResponse? preferBundle,
        ProjectDeepScanReportResponse? preferReport)
    {
        var effectiveWorkspaceId = FirstNonEmpty(
            preferBundle?.WorkspaceId,
            preferReport?.WorkspaceId,
            workspaceId);

        CacheProjectDashboardState(effectiveWorkspaceId, preferBundle, preferReport);

        if (string.IsNullOrWhiteSpace(effectiveWorkspaceId))
        {
            return;
        }

        // Fetch bundle when not supplied by caller.
        if (preferBundle == null)
        {
            var resolvedBundle = await TryGetProjectContextBundleAsync(effectiveWorkspaceId)
                .ConfigureAwait(true);

            if (resolvedBundle != null)
            {
                _projectDashboardBundle = resolvedBundle;
            }
        }

        // Fetch deep-scan report when the bundle indicates one is available.
        var effectiveBundle = preferBundle ?? _projectDashboardBundle;
        if (preferReport == null
            && effectiveBundle != null
            && string.Equals(
                effectiveBundle.DeepScanStatus,
                ProjectDeepScanStatuses.Completed,
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var report = await _missionClient
                    .GetProjectDeepScanAsync(effectiveWorkspaceId, CancellationToken.None)
                    .ConfigureAwait(true);

                if (report != null && report.Exists)
                {
                    _projectDashboardReport = report;
                }
            }
            catch
            {
                // Best-effort — dashboard shows last known state.
            }
        }

        StateChanged?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Active task context
    // -------------------------------------------------------------------------

    /// <summary>
    /// Retrieves the active task context (document + active view) via
    /// <see cref="InternalToolClient"/> and pushes ambient shell state.
    /// </summary>
    /// <returns>The current <see cref="TaskContextResponse"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the underlying tool call does not succeed.
    /// </exception>
    internal async Task<TaskContextResponse> GetActiveTaskContextAsync()
    {
        var response = await InternalToolClient.Instance.CallAsync(
            ToolNames.SessionGetTaskContext,
            JsonUtil.Serialize(new TaskContextRequest
            {
                MaxRecentOperations = 1,
                MaxRecentEvents = 1,
                IncludeCapabilities = false,
                IncludeToolCatalog = false
            }),
            false).ConfigureAwait(true);

        if (!response.Succeeded)
        {
            throw new InvalidOperationException(
                BuildToolFailureMessage("Failed to read active document context.", response));
        }

        var context = JsonUtil.DeserializeRequired<TaskContextResponse>(response.PayloadJson);
        UiShellState.UpdateAmbientContext(context.Document?.Title, context.ActiveContext?.ViewName);
        return context;
    }

    // -------------------------------------------------------------------------
    // Workspace-ID helpers (called from WorkerTab)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the workspace identifier to use for a UI-initiated action,
    /// preferring any already-known workspace from the chat store or shell state.
    /// Falls back to a slug derived from the active document's file path.
    /// </summary>
    /// <param name="document">The currently active Revit document summary.</param>
    /// <returns>
    /// A non-empty workspace identifier string.  Returns <c>"default"</c> as a
    /// last resort.
    /// </returns>
    internal string ResolveActionWorkspaceId(DocumentSummaryDto document)
    {
        var existingWorkspaceId = FirstNonEmpty(
            _chatStore.LatestWorkerResponse.OnboardingStatus?.WorkspaceId,
            _chatStore.LatestWorkerResponse.WorkspaceId,
            UiShellState.CurrentWorkspaceId);

        if (!string.IsNullOrWhiteSpace(existingWorkspaceId))
        {
            return existingWorkspaceId;
        }

        var fileStem = Path.GetFileNameWithoutExtension(document?.PathName ?? string.Empty);
        var candidate = Slugify(FirstNonEmpty(fileStem, document?.Title, "default"));
        return string.IsNullOrWhiteSpace(candidate) ? "default" : candidate;
    }

    // -------------------------------------------------------------------------
    // Local state mutation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies a successful project init or deep-scan result to the local worker
    /// response, persists it into the chat store, and updates the dashboard cache.
    /// </summary>
    /// <param name="bundle">The context bundle returned by the tool, or <c>null</c>.</param>
    /// <param name="onboarding">The onboarding status returned by the tool, or <c>null</c>.</param>
    /// <param name="systemMessage">Human-readable message appended to the chat transcript.</param>
    /// <param name="statusCode">Machine-readable status code for the transcript entry.</param>
    internal void ApplyProjectStateLocally(
        ProjectContextBundleResponse? bundle,
        OnboardingStatusDto? onboarding,
        string systemMessage,
        string statusCode)
    {
        CommitLocalWorkerMutation(worker =>
        {
            var effectiveOnboarding = onboarding
                ?? bundle?.OnboardingStatus
                ?? worker.OnboardingStatus
                ?? new OnboardingStatusDto();

            var effectiveWorkspaceId = FirstNonEmpty(
                bundle?.WorkspaceId,
                effectiveOnboarding.WorkspaceId,
                worker.WorkspaceId);

            worker.WorkspaceId = effectiveWorkspaceId;
            worker.OnboardingStatus = effectiveOnboarding;

            worker.ContextSummary.DocumentTitle =
                FirstNonEmpty(worker.ContextSummary.DocumentTitle, UiShellState.CurrentDocumentTitle);
            worker.ContextSummary.ActiveViewName =
                FirstNonEmpty(worker.ContextSummary.ActiveViewName, UiShellState.CurrentActiveView);
            worker.ContextSummary.WorkspaceId =
                FirstNonEmpty(effectiveWorkspaceId, worker.ContextSummary.WorkspaceId);
            worker.ContextSummary.ProjectSummary =
                FirstNonEmpty(bundle?.Summary, effectiveOnboarding.Summary, worker.ContextSummary.ProjectSummary);
            worker.ContextSummary.ProjectPrimaryModelStatus =
                FirstNonEmpty(
                    bundle?.PrimaryModelStatus,
                    effectiveOnboarding.PrimaryModelStatus,
                    worker.ContextSummary.ProjectPrimaryModelStatus);

            if (bundle != null)
            {
                worker.ContextSummary.ProjectPendingUnknowns =
                    bundle.PendingUnknowns?.ToList() ?? new List<string>();

                worker.ContextSummary.ProjectTopRefs =
                    (bundle.SourceRefs ?? new List<ProjectContextRef>())
                        .Select(ResolveContextRefLabel)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Take(6)
                        .ToList();
            }

            worker.MissionStatus = WorkerMissionStates.Completed;
            worker.Stage = WorkerStages.Done;

            worker.Messages.Add(new WorkerChatMessage
            {
                Role = WorkerMessageRoles.System,
                Content = systemMessage,
                TimestampUtc = DateTime.UtcNow,
                StatusCode = statusCode
            });
        });

        CacheProjectDashboardState(
            FirstNonEmpty(
                bundle?.WorkspaceId,
                onboarding?.WorkspaceId,
                UiShellState.CurrentWorkspaceId),
            bundle,
            null);
    }

    // -------------------------------------------------------------------------
    // Failure reporting helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the most descriptive error message from a failed
    /// <see cref="ToolResponseEnvelope"/> and forwards to
    /// <see cref="ReportLocalActionFailure"/>.
    /// </summary>
    /// <param name="fallbackMessage">Message to display when the envelope contains no diagnostics.</param>
    /// <param name="response">The failed tool response envelope.</param>
    internal void ReportToolActionFailure(string fallbackMessage, ToolResponseEnvelope response)
    {
        ReportLocalActionFailure(
            BuildToolFailureMessage(fallbackMessage, response),
            FirstNonEmpty(response?.StatusCode, "TOOL_ACTION_FAILED"));
    }

    /// <summary>
    /// Records an action failure in the worker chat transcript (with
    /// <c>Blocked</c> / <c>Recovery</c> state) and raises <see cref="ToastRequested"/>
    /// with <see cref="ToastType.Error"/>.
    /// </summary>
    /// <param name="message">Human-readable failure description.</param>
    /// <param name="statusCode">Machine-readable failure code.</param>
    internal void ReportLocalActionFailure(string message, string statusCode)
    {
        ToastRequested?.Invoke(message, ToastType.Error);

        CommitLocalWorkerMutation(worker =>
        {
            worker.ContextSummary.DocumentTitle =
                FirstNonEmpty(worker.ContextSummary.DocumentTitle, UiShellState.CurrentDocumentTitle);
            worker.ContextSummary.ActiveViewName =
                FirstNonEmpty(worker.ContextSummary.ActiveViewName, UiShellState.CurrentActiveView);

            worker.MissionStatus = WorkerMissionStates.Blocked;
            worker.Stage = WorkerStages.Recovery;

            worker.Messages.Add(new WorkerChatMessage
            {
                Role = WorkerMessageRoles.System,
                Content = message,
                TimestampUtc = DateTime.UtcNow,
                StatusCode = statusCode
            });
        });
    }

    // -------------------------------------------------------------------------
    // Worker-response mutation core
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies <paramref name="mutate"/> to the latest worker response snapshot,
    /// seeds the updated snapshot back into the chat store, and raises
    /// <see cref="StateChanged"/> so subscribers can re-render.
    /// </summary>
    /// <param name="mutate">Delegate that performs in-place edits on the worker response.</param>
    internal void CommitLocalWorkerMutation(Action<WorkerResponse> mutate)
    {
        var worker = _chatStore.LatestWorkerResponse ?? new WorkerResponse();
        worker.Messages ??= new List<WorkerChatMessage>();
        worker.ContextSummary ??= new WorkerContextSummary();
        worker.OnboardingStatus ??= new OnboardingStatusDto();

        mutate(worker);

        _chatStore.SeedFromWorkerResponse(worker);
        ResponseApplied?.Invoke(worker);
        StateChanged?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Busy-flag management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Marks the service and chat store as busy and raises <see cref="StateChanged"/>
    /// so that calling UI can show a loading indicator.
    /// </summary>
    internal void BeginLocalToolAction()
    {
        _isBusy = true;
        _chatStore.SetBusy(true);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Clears the busy flag on both the service and the chat store and raises
    /// <see cref="StateChanged"/> so that callers can restore interactive controls.
    /// </summary>
    internal void EndLocalToolAction()
    {
        _isBusy = false;
        _chatStore.SetBusy(false);
        StateChanged?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Static label / context-ref helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the most human-readable label available for a project context reference,
    /// preferring <see cref="ProjectContextRef.Title"/> over
    /// <see cref="ProjectContextRef.RelativePath"/> over
    /// <see cref="ProjectContextRef.SourcePath"/>.
    /// </summary>
    internal static string ResolveContextRefLabel(ProjectContextRef reference)
        => FirstNonEmpty(reference?.Title, reference?.RelativePath, reference?.SourcePath);

    // -------------------------------------------------------------------------
    // Project operation state helpers (used by InitializeWorkspaceAsync / RunProjectDeepScanAsync)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies the result of a completed project operation (init or deep scan) to
    /// the local worker response: writes the updated context summary, onboarding
    /// status, a worker chat message, a tool card, and any artifact refs, then
    /// seeds the chat store.
    /// </summary>
    internal void ApplyProjectOperationState(
        string message,
        WorkerContextSummary contextSummary,
        OnboardingStatusDto onboardingStatus,
        WorkerToolCard toolCard,
        params string?[] artifactPaths)
    {
        var worker = CloneLatestWorkerResponse();

        worker.ContextSummary = contextSummary ?? worker.ContextSummary ?? new WorkerContextSummary();
        worker.OnboardingStatus = onboardingStatus ?? worker.OnboardingStatus ?? new OnboardingStatusDto();
        worker.WorkspaceId = FirstNonEmpty(
            onboardingStatus?.WorkspaceId,
            contextSummary?.WorkspaceId,
            worker.WorkspaceId);

        worker.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.Worker,
            Content = FirstNonEmpty(message, "Project action completed."),
            TimestampUtc = DateTime.UtcNow,
            ToolName = toolCard.ToolName,
            StatusCode = toolCard.StatusCode
        });

        worker.ToolCards ??= new List<WorkerToolCard>();
        worker.ToolCards.Add(toolCard);
        worker.ArtifactRefs = MergeArtifactRefs(worker.ArtifactRefs, artifactPaths);
        worker.ConfiguredProvider = FirstNonEmpty(worker.ConfiguredProvider, _settings.LlmProviderLabel);
        worker.PlannerModel = FirstNonEmpty(worker.PlannerModel, _settings.LlmPlannerModel);
        worker.ResponseModel = FirstNonEmpty(worker.ResponseModel, _settings.LlmResponseModel);

        _chatStore.SeedFromWorkerResponse(worker);
        ResponseApplied?.Invoke(worker);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Applies a project operation failure to the local worker response:
    /// raises a toast, appends a blocked system message, and synthesises a
    /// <see cref="WorkerHostMissionResponse"/> with <c>Blocked</c> state.
    /// </summary>
    /// <param name="message">Human-readable failure description.</param>
    internal void ApplyProjectActionFailure(string? message)
    {
        var errorMessage = FirstNonEmpty(message, "Project action failed.");

        ToastRequested?.Invoke(errorMessage, ToastType.Error);

        var worker = CloneLatestWorkerResponse();
        worker.Messages.Add(new WorkerChatMessage
        {
            Role = WorkerMessageRoles.System,
            Content = errorMessage,
            TimestampUtc = DateTime.UtcNow,
            StatusCode = "PROJECT_ACTION_FAILED"
        });

        _chatStore.SeedFromWorkerResponse(worker);

        _chatStore.ApplyMissionResponse(new WorkerHostMissionResponse
        {
            SessionId = _chatStore.SessionId,
            MissionId = _chatStore.MissionId,
            State = WorkerMissionStates.Blocked,
            Succeeded = false,
            StatusCode = "PROJECT_ACTION_FAILED",
            ResponseText = errorMessage
        });

        ResponseApplied?.Invoke(worker);
        StateChanged?.Invoke();
    }

    // -------------------------------------------------------------------------
    // RunProjectActionAsync — busy-guard + cancellation wrapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a project action delegate inside a busy-guard, cancellation
    /// scope, and structured error handler.
    /// </summary>
    /// <remarks>
    /// Concurrent calls are silently dropped when <see cref="IsBusy"/> is
    /// <c>true</c>.  The cancellation token provided to <paramref name="action"/>
    /// respects <see cref="AgentSettings.RequestTimeoutSeconds"/>, with a minimum
    /// of 45 seconds.
    /// </remarks>
    /// <param name="action">Async delegate to execute with a scoped cancellation token.</param>
    internal async Task RunProjectActionAsync(Func<CancellationToken, Task> action)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _chatStore.SetBusy(true);
        StateChanged?.Invoke();

        _requestCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(45, _settings.RequestTimeoutSeconds)));

        try
        {
            await action(_requestCts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ApplyProjectActionFailure(ex.Message);
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

    // =========================================================================
    // Private — RunProjectInitApplyAsync sub-phases
    // =========================================================================

    /// <summary>
    /// Resolves the four values required to kick off the init-preview phase:
    /// the absolute Revit file path, its parent directory, a workspace-ID hint,
    /// and a display name.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the active task context cannot be retrieved, or when the
    /// active model has no saved file path, or when its source directory cannot
    /// be determined.
    /// </exception>
    private async Task<(string primaryRevitFilePath, string sourceRootPath, string workspaceIdHint, string displayName)>
        ResolveInitPrerequisitesAsync()
    {
        var taskContext = await GetActiveTaskContextAsync().ConfigureAwait(true);
        var document = taskContext.Document ?? new DocumentSummaryDto();
        var primaryRevitFilePath = (document.PathName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(primaryRevitFilePath))
        {
            ReportLocalActionFailure(
                "Current model has no saved file path. Please save the file before initializing workspace.",
                "PROJECT_INIT_PATH_REQUIRED");
            throw new InvalidOperationException("PROJECT_INIT_PATH_REQUIRED");
        }

        var sourceRootPath = Path.GetDirectoryName(primaryRevitFilePath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceRootPath))
        {
            ReportLocalActionFailure(
                "Cannot determine the source directory for the current model.",
                "PROJECT_INIT_SOURCE_ROOT_MISSING");
            throw new InvalidOperationException("PROJECT_INIT_SOURCE_ROOT_MISSING");
        }

        var workspaceIdHint = ResolveActionWorkspaceId(document);
        var displayName = FirstNonEmpty(
            document.Title,
            Path.GetFileNameWithoutExtension(primaryRevitFilePath),
            workspaceIdHint);

        return (primaryRevitFilePath, sourceRootPath, workspaceIdHint, displayName);
    }

    /// <summary>
    /// Calls the <c>ProjectInitPreview</c> tool and derives the effective workspace
    /// identifier from the response.
    /// </summary>
    /// <returns>
    /// A tuple of the raw preview response and the effective workspace identifier
    /// to use for the apply phase (or the early-exit path).
    /// </returns>
    private async Task<(ProjectInitPreviewResponse preview, string effectiveWorkspaceId)>
        RunInitPreviewPhaseAsync(
            string primaryRevitFilePath,
            string sourceRootPath,
            string workspaceIdHint,
            string displayName)
    {
        var previewEnvelope = await InternalToolClient.Instance.CallAsync(
            ToolNames.ProjectInitPreview,
            JsonUtil.Serialize(new ProjectInitPreviewRequest
            {
                SourceRootPath = sourceRootPath,
                WorkspaceId = workspaceIdHint,
                DisplayName = displayName,
                PrimaryRevitFilePath = primaryRevitFilePath
            }),
            false).ConfigureAwait(true);

        if (!previewEnvelope.Succeeded)
        {
            ReportToolActionFailure("Failed to prepare project context for the current model.", previewEnvelope);
            throw new InvalidOperationException(
                BuildToolFailureMessage("PROJECT_INIT_PREVIEW_FAILED", previewEnvelope));
        }

        var preview = JsonUtil.DeserializeRequired<ProjectInitPreviewResponse>(previewEnvelope.PayloadJson);
        var effectiveWorkspaceId = FirstNonEmpty(
            preview.WorkspaceId,
            preview.SuggestedWorkspaceId,
            workspaceIdHint);

        return (preview, effectiveWorkspaceId);
    }

    /// <summary>
    /// Handles the early-exit path when the workspace already exists: fetches
    /// the existing context bundle, updates ambient shell state, and applies the
    /// bundle locally.
    /// </summary>
    private async Task ApplyExistingWorkspaceAsync(string effectiveWorkspaceId)
    {
        var existingBundle = await TryGetProjectContextBundleAsync(effectiveWorkspaceId)
            .ConfigureAwait(true);

        UiShellState.UpdateAmbient(effectiveWorkspaceId, existingBundle);

        ApplyProjectStateLocally(
            existingBundle,
            existingBundle?.OnboardingStatus,
            $"Workspace '{effectiveWorkspaceId}' is ready. Chat will use existing project context for subsequent turns.",
            "PROJECT_INIT_READY");

        ToastRequested?.Invoke(
            $"Workspace '{effectiveWorkspaceId}' is ready.",
            ToastType.Success);
    }

    /// <summary>
    /// Calls the <c>ProjectInitApply</c> tool to write the workspace manifest,
    /// then updates ambient shell state and applies the result locally.
    /// </summary>
    private async Task RunInitApplyPhaseAsync(
        string primaryRevitFilePath,
        string sourceRootPath,
        string effectiveWorkspaceId,
        string displayName)
    {
        var applyEnvelope = await InternalToolClient.Instance.CallAsync(
            ToolNames.ProjectInitApply,
            JsonUtil.Serialize(new ProjectInitApplyRequest
            {
                SourceRootPath = sourceRootPath,
                WorkspaceId = effectiveWorkspaceId,
                DisplayName = displayName,
                PrimaryRevitFilePath = primaryRevitFilePath,
                AllowExistingWorkspaceOverwrite = false,
                IncludeLivePrimaryModelSummary = true
            }),
            false).ConfigureAwait(true);

        if (!applyEnvelope.Succeeded)
        {
            ReportToolActionFailure("Workspace initialization failed.", applyEnvelope);
            throw new InvalidOperationException(
                BuildToolFailureMessage("PROJECT_INIT_APPLY_FAILED", applyEnvelope));
        }

        var apply = JsonUtil.DeserializeRequired<ProjectInitApplyResponse>(applyEnvelope.PayloadJson);

        UiShellState.UpdateAmbient(apply.WorkspaceId, apply.ContextBundle);

        ApplyProjectStateLocally(
            apply.ContextBundle,
            apply.OnboardingStatus,
            $"Workspace '{apply.WorkspaceId}' initialized. Chat now has project context bound to the current model.",
            "PROJECT_INIT_APPLIED");

        ToastRequested?.Invoke(
            $"Workspace '{apply.WorkspaceId}' initialized.",
            ToastType.Success);
    }

    // =========================================================================
    // Private — tool calls
    // =========================================================================

    /// <summary>
    /// Fetches the project context bundle for the given workspace via
    /// <see cref="InternalToolClient"/>.
    /// </summary>
    /// <param name="workspaceId">The workspace to fetch the bundle for.</param>
    /// <returns>
    /// The deserialized bundle on success, or <c>null</c> when the workspace
    /// identifier is blank or the tool call does not succeed.
    /// </returns>
    private static async Task<ProjectContextBundleResponse?> TryGetProjectContextBundleAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        var response = await InternalToolClient.Instance.CallAsync(
            ToolNames.ProjectGetContextBundle,
            JsonUtil.Serialize(new ProjectContextBundleRequest
            {
                WorkspaceId = workspaceId,
                Query = string.Empty,
                MaxSourceRefs = 4,
                MaxStandardsRefs = 4
            }),
            false).ConfigureAwait(true);

        return response.Succeeded
            ? JsonUtil.DeserializeRequired<ProjectContextBundleResponse>(response.PayloadJson)
            : null;
    }

    /// <summary>
    /// Fetches the currently active Revit document summary via
    /// <see cref="InternalToolClient"/>.
    /// </summary>
    /// <returns>
    /// The deserialized <see cref="DocumentSummaryDto"/>, or <c>null</c> if the
    /// tool call does not succeed.
    /// </returns>
    private static async Task<DocumentSummaryDto?> ResolveActiveDocumentAsync()
    {
        var response = await InternalToolClient.Instance
            .CallAsync(ToolNames.DocumentGetActive, "{}", false)
            .ConfigureAwait(true);

        return response.Succeeded
            ? JsonUtil.DeserializeRequired<DocumentSummaryDto>(response.PayloadJson)
            : null;
    }

    // =========================================================================
    // Private — worker-response helpers
    // =========================================================================

    /// <summary>
    /// Creates a deep copy of the latest worker response from the chat store,
    /// initialising missing collections so that callers can append to them safely.
    /// </summary>
    private WorkerResponse CloneLatestWorkerResponse()
    {
        var source = _chatStore.LatestWorkerResponse ?? new WorkerResponse();
        return new WorkerResponse
        {
            SessionId = source.SessionId,
            MissionId = source.MissionId,
            MissionStatus = source.MissionStatus,
            Messages = source.Messages?.ToList() ?? new List<WorkerChatMessage>(),
            ActionCards = source.ActionCards?.ToList() ?? new List<WorkerActionCard>(),
            PendingApproval = source.PendingApproval ?? new PendingApprovalRef(),
            ToolCards = source.ToolCards?.ToList() ?? new List<WorkerToolCard>(),
            ArtifactRefs = source.ArtifactRefs?.ToList() ?? new List<string>(),
            ContextSummary = source.ContextSummary ?? new WorkerContextSummary(),
            ReasoningSummary = source.ReasoningSummary,
            PlanSummary = source.PlanSummary,
            Stage = source.Stage,
            Progress = source.Progress,
            Confidence = source.Confidence,
            RecoveryHints = source.RecoveryHints?.ToList() ?? new List<string>(),
            ExecutionTier = source.ExecutionTier,
            AutoExecutionEligible = source.AutoExecutionEligible,
            QueueState = source.QueueState ?? new QueueStateResponse(),
            WorkspaceId = source.WorkspaceId,
            SelectedPlaybook = source.SelectedPlaybook ?? new PlaybookRecommendation(),
            PlaybookPreview = source.PlaybookPreview ?? new PlaybookPreviewResponse(),
            StandardsSummary = source.StandardsSummary,
            ResolvedCapabilityDomain = source.ResolvedCapabilityDomain,
            PolicySummary = source.PolicySummary,
            RecommendedSpecialists = source.RecommendedSpecialists?.ToList() ?? new List<CapabilitySpecialistDescriptor>(),
            CompiledPlan = source.CompiledPlan ?? new CompiledTaskPlan(),
            ContextPills = source.ContextPills?.ToList() ?? new List<WorkerContextPill>(),
            ExecutionItems = source.ExecutionItems?.ToList() ?? new List<WorkerExecutionItem>(),
            EvidenceItems = source.EvidenceItems?.ToList() ?? new List<WorkerEvidenceItem>(),
            SuggestedCommands = source.SuggestedCommands?.ToList() ?? new List<WorkerCommandSuggestion>(),
            PrimaryRiskSummary = source.PrimaryRiskSummary ?? new WorkerRiskSummary(),
            SurfaceHint = source.SurfaceHint ?? new WorkerSurfaceHint(),
            OnboardingStatus = source.OnboardingStatus ?? new OnboardingStatusDto(),
            FallbackProposal = source.FallbackProposal ?? new FallbackArtifactProposal(),
            SkillCaptureProposal = source.SkillCaptureProposal ?? new SkillCaptureProposal(),
            ProjectPatternSnapshot = source.ProjectPatternSnapshot ?? new ProjectPatternSnapshot(),
            TemplateSynthesisProposal = source.TemplateSynthesisProposal ?? new TemplateSynthesisProposal(),
            DeltaSuggestions = source.DeltaSuggestions?.ToList() ?? new List<DeltaSuggestion>(),
            ConfiguredProvider = source.ConfiguredProvider,
            PlannerModel = source.PlannerModel,
            ResponseModel = source.ResponseModel,
            ReasoningMode = source.ReasoningMode
        };
    }

    // =========================================================================
    // Private — static factory helpers (context summary + tool card)
    // =========================================================================

    /// <summary>
    /// Builds a <see cref="WorkerContextSummary"/> from a document summary and
    /// the project context bundle / onboarding status returned by a project action.
    /// </summary>
    private static WorkerContextSummary BuildProjectContextSummary(
        DocumentSummaryDto document,
        ProjectContextBundleResponse? bundle,
        OnboardingStatusDto? onboardingStatus)
    {
        var safeBundle = bundle ?? new ProjectContextBundleResponse();
        var safeOnboarding = onboardingStatus ?? new OnboardingStatusDto();

        var allRefs = (safeBundle.TopStandardsRefs ?? new List<ProjectContextRef>())
            .Concat(safeBundle.SourceRefs ?? new List<ProjectContextRef>())
            .Concat(safeBundle.DeepScanRefs ?? new List<ProjectContextRef>())
            .Select(x => FirstNonEmpty(x.RelativePath, x.Title, x.RefId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(8)
            .ToList();

        return new WorkerContextSummary
        {
            DocumentKey = document?.DocumentKey ?? string.Empty,
            DocumentTitle = document?.Title ?? string.Empty,
            WorkspaceId = FirstNonEmpty(safeBundle.WorkspaceId, safeOnboarding.WorkspaceId),
            ProjectSummary = FirstNonEmpty(safeBundle.DeepScanSummary, safeBundle.Summary, safeOnboarding.Summary),
            ProjectPrimaryModelStatus = FirstNonEmpty(
                safeBundle.PrimaryModelStatus,
                safeOnboarding.PrimaryModelStatus),
            ProjectTopRefs = allRefs,
            ProjectPendingUnknowns = safeBundle.PendingUnknowns?.ToList() ?? new List<string>(),
            GroundingLevel = safeBundle.Exists
                ? string.Equals(
                    safeBundle.DeepScanStatus,
                    ProjectDeepScanStatuses.Completed,
                    StringComparison.OrdinalIgnoreCase)
                    ? WorkerGroundingLevels.DeepScanGrounded
                    : WorkerGroundingLevels.WorkspaceGrounded
                : WorkerGroundingLevels.LiveContextOnly,
            GroundingSummary = safeBundle.Exists
                ? FirstNonEmpty(safeBundle.DeepScanSummary, safeBundle.Summary, safeOnboarding.Summary)
                : FirstNonEmpty(
                    safeOnboarding.Summary,
                    "Dang dung live Revit context; project context chua init."),
            GroundingRefs = allRefs
        };
    }

    /// <summary>
    /// Builds a <see cref="WorkerToolCard"/> representing a successfully completed
    /// project action (init apply or deep scan).
    /// </summary>
    private static WorkerToolCard BuildProjectToolCard(
        string toolName,
        string statusCode,
        string? summary,
        object payload,
        params string?[] artifactPaths)
    {
        return new WorkerToolCard
        {
            ToolName = toolName,
            StatusCode = FirstNonEmpty(statusCode, StatusCodes.ExecuteSucceeded),
            Succeeded = true,
            Summary = FirstNonEmpty(summary, toolName),
            PayloadJson = JsonUtil.Serialize(payload),
            ArtifactRefs = artifactPaths
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Stage = WorkerFlowStages.Scan,
            Progress = 100,
            WhyThisTool = "Project onboarding/read-side action duoc thuc thi truc tiep qua WorkerHost project endpoints.",
            Confidence = 0.94d,
            RecoveryHints = new List<string>
            {
                "Neu workspace state co dau hieu stale, refresh pane hoac mo lai session moi nhat.",
                "Neu active model thay doi, project context can duoc refresh theo model hien tai."
            },
            ExecutionTier = WorkerExecutionTiers.Tier0,
            AutoExecutionEligible = false
        };
    }

    /// <summary>
    /// Converts a raw <see cref="ProjectDeepScanResponse"/> into the richer
    /// <see cref="ProjectDeepScanReportResponse"/> DTO used by the dashboard cache.
    /// </summary>
    private static ProjectDeepScanReportResponse BuildProjectDashboardReportResponse(
        ProjectDeepScanResponse response)
    {
        var safeResponse = response ?? new ProjectDeepScanResponse();
        return new ProjectDeepScanReportResponse
        {
            StatusCode = safeResponse.StatusCode,
            Exists = !string.IsNullOrWhiteSpace(safeResponse.WorkspaceId),
            WorkspaceId = safeResponse.WorkspaceId,
            WorkspaceRootPath = safeResponse.WorkspaceRootPath,
            ReportPath = safeResponse.ReportPath,
            SummaryReportPath = safeResponse.SummaryReportPath,
            Report = safeResponse.Report ?? new ProjectDeepScanReport(),
            Summary = safeResponse.Summary,
            OnboardingStatus = safeResponse.OnboardingStatus ?? new OnboardingStatusDto()
        };
    }

    /// <summary>
    /// Merges an existing artifact-ref list with a variable set of additional
    /// nullable paths, deduplicating the result.
    /// </summary>
    private static List<string> MergeArtifactRefs(
        IEnumerable<string>? existing,
        IEnumerable<string?>? additional)
    {
        return (existing ?? Enumerable.Empty<string>())
            .Concat(
                (additional ?? Array.Empty<string?>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // =========================================================================
    // Private — static string utilities
    // =========================================================================

    /// <summary>
    /// Returns the first non-blank, trimmed string from <paramref name="values"/>,
    /// or <see cref="string.Empty"/> when all values are blank.
    /// </summary>
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

    /// <summary>
    /// Extracts the most descriptive error message available from a failed
    /// <see cref="ToolResponseEnvelope"/>, falling back to the supplied
    /// <paramref name="fallbackMessage"/>.
    /// </summary>
    private static string BuildToolFailureMessage(string fallbackMessage, ToolResponseEnvelope? response)
    {
        var diagnosticMessage = response?.Diagnostics?
            .Select(x => x?.Message?.Trim())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        return FirstNonEmpty(diagnosticMessage, response?.StatusCode, fallbackMessage);
    }

    /// <summary>
    /// Converts a display-name string into a URL-safe slug composed of
    /// lowercase alphanumeric characters and hyphens, with no leading or
    /// trailing hyphens and no consecutive hyphens.
    /// </summary>
    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value!
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var slug = new string(chars);

        while (slug.IndexOf("--", StringComparison.Ordinal) >= 0)
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Trim('-');
    }

    /// <summary>
    /// Strips the well-known <c>"default"</c> sentinel from a workspace ID so
    /// that callers can obtain a truly empty string and trigger auto-suggestion.
    /// </summary>
    private static string NormalizePreferredWorkspaceId(string? workspaceId)
    {
        return string.Equals(
            (workspaceId ?? string.Empty).Trim(),
            "default",
            StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : (workspaceId ?? string.Empty).Trim();
    }
}
