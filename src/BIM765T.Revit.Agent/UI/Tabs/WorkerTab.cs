using System;



using System.Collections.Generic;



using System.Diagnostics;



using System.Diagnostics.CodeAnalysis;



using System.IO;



using System.Linq;



using System.Globalization;



using System.Text;



using System.Threading;



using System.Threading.Tasks;



using System.Windows;



using System.Windows.Controls;



using System.Windows.Threading;



using BIM765T.Revit.Agent.Config;



using BIM765T.Revit.Agent.UI.Chat;



using BIM765T.Revit.Agent.UI.Components;



using BIM765T.Revit.Agent.UI.Theme;



using BIM765T.Revit.Contracts.Bridge;



using BIM765T.Revit.Contracts.Common;



using BIM765T.Revit.Contracts.Platform;



using BIM765T.Revit.Contracts.Serialization;







namespace BIM765T.Revit.Agent.UI.Tabs;







[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WorkerTab lives for the lifetime of the dockable pane.")]



internal sealed class WorkerTab : UserControl



{



    private readonly AgentSettings _settings;



    private readonly Action<WorkerResponse> _onStateChanged;



    private readonly Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> _onInspect;



    private readonly WorkerHostMissionClient _missionClient;



    private readonly ChatSessionStore _chatStore;



    private readonly ChatTimelineProjector _projector;



    private readonly Grid _rootLayout;

    private readonly StackPanel _toastArea;

    private readonly StackPanel _timelinePanel;

    private Border _emptyStateCard;

    private readonly ScrollViewer _timelineScroll;



    private readonly ComposerBar _composer;



    private readonly DispatcherTimer _streamRenderTimer;



    private bool _isBusy;



    private bool _bootstrapped;



    private bool _streamRenderPending;

    private bool _themeRefreshQueued;



    private CancellationTokenSource? _requestCts;



    private CancellationTokenSource? _missionStreamCts;



    private string _lastRenderSignature = string.Empty;



    private int _lastRenderedEntryCount;



    private DateTime _lastRenderUtc = DateTime.MinValue;

    private int _missionStreamReconnectAttempts;

    private ProjectContextBundleResponse? _projectDashboardBundle;

    private ProjectDeepScanReportResponse? _projectDashboardReport;

    private static readonly TimeSpan StreamingRenderInterval = TimeSpan.FromMilliseconds(260);







    internal WorkerTab(



        AgentSettings settings,



        Action<WorkerResponse> onStateChanged,



        Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> onInspect)



    {



        _settings = settings;



        _onStateChanged = onStateChanged;



        _onInspect = onInspect;



        _missionClient = new WorkerHostMissionClient();



        _chatStore = new ChatSessionStore();



        _projector = new ChatTimelineProjector();



        Background = AppTheme.PageBackground;

        _rootLayout = new Grid
        {
            Background = AppTheme.PageBackground
        };

        var root = _rootLayout;



        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });



        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });



        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });







        _toastArea = new StackPanel



        {



            Margin = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceLG, AppTheme.SpaceLG, 0)



        };



        Grid.SetRow(_toastArea, 0);



        root.Children.Add(_toastArea);







        _timelinePanel = new StackPanel



        {



            Margin = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceMD, AppTheme.SpaceLG, AppTheme.SpaceLG)



        };



        _emptyStateCard = BuildEmptyStateCard();



        _timelinePanel.Children.Add(_emptyStateCard);



        _timelineScroll = new ScrollViewer



        {



            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,



            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,



            Background = AppTheme.PageBackground,



            Content = _timelinePanel



        };



        Grid.SetRow(_timelineScroll, 1);



        root.Children.Add(_timelineScroll);







        _composer = new ComposerBar();



        _composer.Submitted += SendCurrentMessage;



        Grid.SetRow(_composer, 2);



        root.Children.Add(_composer);







        _streamRenderTimer = new DispatcherTimer



        {



            Interval = StreamingRenderInterval



        };



        _streamRenderTimer.Tick += (_, __) =>



        {



            _streamRenderTimer.Stop();



            if (!_streamRenderPending)



            {



                return;



            }







            RenderCurrentState();



        };







        Content = _rootLayout;



        Loaded += OnLoaded;



        RenderCurrentState();



    }







    internal bool IsBusy => _isBusy || _chatStore.IsBusy;







    internal void FocusComposer()



    {



        _composer.FocusInput();



    }



    internal void ApplyThemeRefresh()



    {



        Background = AppTheme.PageBackground;



        _rootLayout.Background = AppTheme.PageBackground;



        _timelineScroll.Background = AppTheme.PageBackground;



        _composer.ApplyThemeRefresh();

        ToastNotification.ApplyThemeRefresh(_toastArea);



        _lastRenderSignature = string.Empty;



        QueueThemeRefresh();



    }







    internal void ShowToast(string message, ToastType type = ToastType.Info)

    {

        Dispatcher.BeginInvoke(

            new Action(() => ToastNotification.Show(_toastArea, FirstNonEmpty(message, "765T"), type)),

            DispatcherPriority.Background);

    }

    private void QueueThemeRefresh()
    {
        if (_themeRefreshQueued)
        {
            return;
        }

        _themeRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _themeRefreshQueued = false;
            RequestStreamingRender(forceImmediate: true);
        }), DispatcherPriority.Background);
    }



    internal void StartNewTask()



    {



        CancelPendingRequest();



        _chatStore.Reset();



        UiShellState.ClearSession();



        RenderCurrentState();



        _ = ShowOnboardingStateAsync();



        FocusComposer();



    }



    internal void RunShellAction(string actionKey)

    {

        var normalized = Slugify(actionKey);

        if (string.IsNullOrWhiteSpace(normalized))

        {

            return;

        }



        var actions = BuildBaseShellActions();

        if (actions.TryGetValue(normalized, out var action))

        {

            action();

            return;

        }



        var prompts = BuildSlashPrompts(_projector.Project(_chatStore));

        if (prompts.TryGetValue(normalized, out action))

        {

            action();

        }

    }



    internal Task<bool> OpenSessionAsync(string sessionId)



    {



        return TryRestoreSessionAsync(sessionId);



    }







    internal async Task<string> TryAutoResumeLatestSessionAsync(IEnumerable<WorkerSessionSummary>? sessions, string? ambientDocumentKey, string? currentSessionId)



    {



        if (_isBusy || !CanAutoResumeLatestSession(currentSessionId))



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







        var preferred = candidates.FirstOrDefault(x =>



            !string.IsNullOrWhiteSpace(ambientDocumentKey)



            && string.Equals(x.DocumentKey, ambientDocumentKey, StringComparison.OrdinalIgnoreCase))



            ?? candidates[0];



        if (string.IsNullOrWhiteSpace(preferred.SessionId)



            || string.Equals(preferred.SessionId, FirstNonEmpty(currentSessionId, _chatStore.SessionId), StringComparison.OrdinalIgnoreCase))



        {



            return string.Empty;



        }







        return await TryRestoreSessionAsync(preferred.SessionId).ConfigureAwait(true)



            ? preferred.SessionId



            : string.Empty;



    }







    private async void OnLoaded(object sender, RoutedEventArgs e)



    {



        if (_bootstrapped)



        {



            return;



        }







        _bootstrapped = true;



        await RestoreOrShowOnboardingAsync();



    }







    private async Task RestoreOrShowOnboardingAsync()



    {



        if (await TryRestoreMissionAsync(UiShellState.LastMissionId))



        {



            return;



        }







        if (await TryRestoreSessionAsync(UiShellState.LastSessionId))



        {



            return;



        }







        var sessionsResponse = await InternalToolClient.Instance.CallAsync(



            ToolNames.WorkerListSessions,



            JsonUtil.Serialize(new WorkerListSessionsRequest



            {



                MaxResults = 6,



                IncludeEnded = false



            }),



            false);



        if (sessionsResponse.Succeeded)



        {



            var sessions = JsonUtil.DeserializeRequired<List<WorkerSessionSummary>>(sessionsResponse.PayloadJson);



            foreach (var session in sessions.Where(x => !string.IsNullOrWhiteSpace(x?.SessionId)).OrderByDescending(x => x.LastUpdatedUtc))



            {



                if (await TryRestoreSessionAsync(session.SessionId))



                {



                    return;



                }



            }



        }







        await ShowOnboardingStateAsync();



    }







    private async Task<bool> TryRestoreMissionAsync(string missionId)



    {



        if (string.IsNullOrWhiteSpace(missionId))



        {



            return false;



        }







        try



        {



            var response = await _missionClient.GetMissionAsync(missionId, CancellationToken.None).ConfigureAwait(true);



            ApplyMissionResponse(response);



            return !string.IsNullOrWhiteSpace(response.MissionId);



        }



        catch



        {



            UiShellState.RememberMission(string.Empty);



            return false;



        }



    }







    private async Task<bool> TryRestoreSessionAsync(string sessionId)



    {



        if (string.IsNullOrWhiteSpace(sessionId))



        {



            return false;



        }







        var sessionResponse = await InternalToolClient.Instance.CallAsync(



            ToolNames.WorkerGetSession,



            JsonUtil.Serialize(new WorkerSessionRequest { SessionId = sessionId }),



            false);



        if (!sessionResponse.Succeeded)



        {



            UiShellState.ClearSession(sessionId);



            return false;



        }







        var worker = JsonUtil.DeserializeRequired<WorkerResponse>(sessionResponse.PayloadJson);



        _chatStore.SeedFromWorkerResponse(worker);

        await RefreshProjectDashboardStateAsync(

            FirstNonEmpty(worker.WorkspaceId, worker.OnboardingStatus?.WorkspaceId, worker.ContextSummary?.WorkspaceId),

            null,

            null).ConfigureAwait(true);


        UiShellState.RememberSession(worker.SessionId);



        UiShellState.RememberMission(worker.MissionId);



        RenderCurrentState();



        return true;



    }







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



            false);







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



                false);



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



            InitStatus = bundle?.Exists == true ? ProjectOnboardingStatuses.Initialized : ProjectOnboardingStatuses.NotInitialized,



            DeepScanStatus = FirstNonEmpty(bundle?.DeepScanStatus, ProjectDeepScanStatuses.NotStarted),



            PrimaryModelStatus = FirstNonEmpty(bundle?.PrimaryModelStatus, context.ProjectPrimaryModelStatus, ProjectPrimaryModelStatuses.NotRequested),



            Summary = FirstNonEmpty(bundle?.Summary, context.ProjectSummary, "Chat đã sẵn sàng. Init workspace để mở project-aware context cho model này.")



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



                    ? string.Equals(bundle.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)



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

        await RefreshProjectDashboardStateAsync(onboarding.WorkspaceId, bundle, null).ConfigureAwait(true);



        RenderCurrentState();



    }







    private async void SendCurrentMessage(string message)



    {



        var trimmedMessage = (message ?? string.Empty).Trim();



        if (_isBusy || string.IsNullOrWhiteSpace(trimmedMessage))



        {



            return;



        }







        var missionId = Guid.NewGuid().ToString("N");



        _isBusy = true;



        _chatStore.BeginUserTurn(trimmedMessage);



        _chatStore.BeginMissionStream(missionId);



        _composer.ClearInput();



        RenderCurrentState();



        _requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, _settings.RequestTimeoutSeconds)));



        try



        {



            await _missionClient.EnsureAvailableAsync(_requestCts.Token).ConfigureAwait(true);



            StartMissionEventStream(missionId);



            var response = await _missionClient.SubmitChatAsync(new WorkerHostChatRequest



            {



                MissionId = missionId,



                SessionId = _chatStore.SessionId,



                Message = trimmedMessage,



                PersonaId = _settings.DefaultWorkerProfile?.PersonaId ?? WorkerPersonas.RevitWorker,



                ContinueMission = true



            }, _requestCts.Token).ConfigureAwait(true);







            ApplyMissionResponse(response);



        }



        catch (Exception ex)



        {



            CancelMissionEventStream();



            HandleMissionFailure(ex.Message);



        }



        finally



        {



            _isBusy = false;



            _requestCts?.Dispose();



            _requestCts = null;



            RenderCurrentState();



        }



    }







    private void ApplyMissionResponse(WorkerHostMissionResponse response)



    {



        _chatStore.ApplyMissionResponse(response);



        UiShellState.RememberSession(_chatStore.SessionId);



        UiShellState.RememberMission(_chatStore.MissionId);



        RenderCurrentState();



        StartMissionEventStreamIfNeeded(response);



    }







    private void HandleMissionFailure(string message)



    {



        ToastNotification.Show(_toastArea, FirstNonEmpty(message, "WorkerHost request failed."), ToastType.Error);



        var worker = _chatStore.LatestWorkerResponse ?? new WorkerResponse();



        var pendingUserMessage = _chatStore.PendingUserMessage;



        var lastUserMessage = worker.Messages.LastOrDefault(x => string.Equals(x.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase))?.Content;



        if (!string.IsNullOrWhiteSpace(pendingUserMessage)



            && !string.Equals((lastUserMessage ?? string.Empty).Trim(), pendingUserMessage.Trim(), StringComparison.Ordinal))



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



    }







    private void RenderCurrentState()



    {



        var vm = _projector.Project(_chatStore);



        var wasNearBottom = IsNearBottom();



        var suggestionPrompts = BuildSlashPrompts(vm);



        var footerText = ResolveFooter(vm);







        if (ShouldPublishWorkerState(vm.LatestWorkerResponse))



        {



            _onStateChanged(vm.LatestWorkerResponse);



        }







        var renderSignature = BuildRenderSignature(vm, footerText, suggestionPrompts.Keys);



        if (string.Equals(renderSignature, _lastRenderSignature, StringComparison.Ordinal))



        {



            _streamRenderPending = false;



            _streamRenderTimer.Stop();



            return;



        }







        ReplaceEmptyStateCard();

        _timelinePanel.Children.Clear();



        _emptyStateCard.Visibility = vm.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;



        if (_emptyStateCard.Visibility == Visibility.Visible)



        {



            _timelinePanel.Children.Add(_emptyStateCard);



        }







        foreach (var entry in vm.Entries)



        {



            _timelinePanel.Children.Add(BuildTimelineEntry(entry));



        }







        _composer.SetContextItems(Array.Empty<string>());



        _composer.SetSuggestionPrompts(suggestionPrompts.Select(x => Tuple.Create(x.Key, x.Value)));



        _composer.SetFooterText(footerText);







        _lastRenderSignature = renderSignature;



        _lastRenderUtc = DateTime.UtcNow;



        _streamRenderPending = false;



        _streamRenderTimer.Stop();



        var shouldAutoScroll = wasNearBottom || vm.Entries.Count > _lastRenderedEntryCount;



        _lastRenderedEntryCount = vm.Entries.Count;



        if (shouldAutoScroll)



        {



            _timelineScroll.ScrollToEnd();



        }



    }







    private UIElement BuildTimelineEntry(TimelineEntryVm entry)



    {



        switch (entry.Kind)



        {



            case TimelineEntryKinds.MissionTraceTurn:



                return new MissionTraceTurn(entry.Trace);



            case TimelineEntryKinds.SystemStateTurn:



                return new SystemStateTurn(entry.SystemTurn, HandleSystemAction);



            case TimelineEntryKinds.ArtifactRow:



                return new ArtifactAttachmentRow(entry.Artifact, HandleArtifactAction);



            default:



                return new MessageBubble(entry.Message);



        }



    }







    private bool HandleSystemAction(SystemTurnActionVm action)



    {



        switch (action.ActionKind)



        {



            case SystemTurnActionKinds.Approve:



                _ = ExecuteMissionCommandAsync("approve");



                return true;



            case SystemTurnActionKinds.Reject:



                _ = ExecuteMissionCommandAsync("reject");



                return true;



            case SystemTurnActionKinds.Resume:



                _ = ExecuteMissionCommandAsync("resume");



                return true;



            case SystemTurnActionKinds.InitWorkspace:



                _ = InitializeWorkspaceAsync();



                return true;



            case SystemTurnActionKinds.RunDeepScan:



                _ = RunProjectDeepScanAsync();



                return true;



            case SystemTurnActionKinds.ApplyInRevit:



                SendCurrentMessage(action.CommandText);



                return true;



            case SystemTurnActionKinds.OpenArtifact:



                OpenPath(action.Path);



                return true;



            case SystemTurnActionKinds.CopyPath:



                CopyToClipboard(action.Path);



                return true;



            default:



                return false;



        }



    }







    private async Task RunProjectInitApplyAsync()



    {



        if (_isBusy)



        {



            return;



        }







        BeginLocalToolAction();



        try



        {



            var taskContext = await GetActiveTaskContextAsync().ConfigureAwait(true);



            var document = taskContext.Document ?? new DocumentSummaryDto();



            var primaryRevitFilePath = (document.PathName ?? string.Empty).Trim();



            if (string.IsNullOrWhiteSpace(primaryRevitFilePath))



            {



                ReportLocalActionFailure("Model hiện tại chưa có đường dẫn lưu. Hãy lưu file trước khi khởi tạo workspace.", "PROJECT_INIT_PATH_REQUIRED");



                return;



            }







            var sourceRootPath = Path.GetDirectoryName(primaryRevitFilePath) ?? string.Empty;



            if (string.IsNullOrWhiteSpace(sourceRootPath))



            {



                ReportLocalActionFailure("Không thể xác định thư mục nguồn cho model hiện tại.", "PROJECT_INIT_SOURCE_ROOT_MISSING");



                return;



            }







            var workspaceIdHint = ResolveActionWorkspaceId(document);



            var displayName = FirstNonEmpty(document.Title, Path.GetFileNameWithoutExtension(primaryRevitFilePath), workspaceIdHint);



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



                ReportToolActionFailure("Không thể chuẩn bị project context cho model hiện tại.", previewEnvelope);



                return;



            }







            var preview = JsonUtil.DeserializeRequired<ProjectInitPreviewResponse>(previewEnvelope.PayloadJson);



            var effectiveWorkspaceId = FirstNonEmpty(preview.WorkspaceId, preview.SuggestedWorkspaceId, workspaceIdHint);



            if (preview.WorkspaceExists



                || string.Equals(preview.OnboardingStatus.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase))



            {



                var existingBundle = await TryGetProjectContextBundleAsync(effectiveWorkspaceId).ConfigureAwait(true);



                UiShellState.UpdateAmbient(effectiveWorkspaceId, existingBundle);



                ApplyProjectStateLocally(



                    existingBundle,



                    existingBundle?.OnboardingStatus ?? preview.OnboardingStatus,



                    $"Workspace '{effectiveWorkspaceId}' đã sẵn sàng. Chat sẽ dùng project context hiện có cho các lượt tiếp theo.",



                    "PROJECT_INIT_READY");



                ToastNotification.Show(_toastArea, $"Workspace '{effectiveWorkspaceId}' đã sẵn sàng.", ToastType.Success);



                return;



            }







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



                ReportToolActionFailure("Khởi tạo workspace thất bại.", applyEnvelope);



                return;



            }







            var apply = JsonUtil.DeserializeRequired<ProjectInitApplyResponse>(applyEnvelope.PayloadJson);



            UiShellState.UpdateAmbient(apply.WorkspaceId, apply.ContextBundle);



            ApplyProjectStateLocally(



                apply.ContextBundle,



                apply.OnboardingStatus,



                $"Đã khởi tạo workspace '{apply.WorkspaceId}'. Chat giờ có project context gắn với model hiện tại.",



                "PROJECT_INIT_APPLIED");



            ToastNotification.Show(_toastArea, $"Workspace '{apply.WorkspaceId}' đã được khởi tạo.", ToastType.Success);



        }



        catch (Exception ex)



        {



            ReportLocalActionFailure(FirstNonEmpty(ex.Message, "Khởi tạo workspace thất bại."), "PROJECT_INIT_ERROR");



        }



        finally



        {



            EndLocalToolAction();



        }



    }







    private async Task<TaskContextResponse> GetActiveTaskContextAsync()



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



            throw new InvalidOperationException(BuildToolFailureMessage("Không thể đọc active document context.", response));



        }







        var context = JsonUtil.DeserializeRequired<TaskContextResponse>(response.PayloadJson);



        UiShellState.UpdateAmbientContext(context.Document?.Title, context.ActiveContext?.ViewName);



        return context;



    }







    private void CacheProjectDashboardState(string? workspaceId, ProjectContextBundleResponse? bundle, ProjectDeepScanReportResponse? report)

    {

        var effectiveWorkspaceId = FirstNonEmpty(bundle?.WorkspaceId, report?.WorkspaceId, workspaceId);

        if (string.IsNullOrWhiteSpace(effectiveWorkspaceId))

        {

            if (bundle == null && report == null)

            {

                _projectDashboardBundle = null;

                _projectDashboardReport = null;

            }

            _lastRenderSignature = string.Empty;

            return;

        }



        if (bundle != null)

        {

            _projectDashboardBundle = bundle;

        }

        else if (!string.Equals(_projectDashboardBundle?.WorkspaceId, effectiveWorkspaceId, StringComparison.OrdinalIgnoreCase))

        {

            _projectDashboardBundle = null;

        }



        if (report != null)

        {

            _projectDashboardReport = report;

        }

        else if (!string.Equals(_projectDashboardReport?.WorkspaceId, effectiveWorkspaceId, StringComparison.OrdinalIgnoreCase))

        {

            _projectDashboardReport = null;

        }



        _lastRenderSignature = string.Empty;

    }



    private async Task RefreshProjectDashboardStateAsync(string? workspaceId, ProjectContextBundleResponse? preferBundle, ProjectDeepScanReportResponse? preferReport)

    {

        var effectiveWorkspaceId = FirstNonEmpty(preferBundle?.WorkspaceId, preferReport?.WorkspaceId, workspaceId);

        CacheProjectDashboardState(effectiveWorkspaceId, preferBundle, preferReport);

        if (string.IsNullOrWhiteSpace(effectiveWorkspaceId))

        {

            return;

        }



        if (preferBundle == null)

        {

            var resolvedBundle = await TryGetProjectContextBundleAsync(effectiveWorkspaceId).ConfigureAwait(true);

            if (resolvedBundle != null)

            {

                _projectDashboardBundle = resolvedBundle;

            }

        }



        var effectiveBundle = preferBundle ?? _projectDashboardBundle;

        if (preferReport == null

            && effectiveBundle != null

            && string.Equals(effectiveBundle.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))

        {

            try

            {

                var report = await _missionClient.GetProjectDeepScanAsync(effectiveWorkspaceId, CancellationToken.None).ConfigureAwait(true);

                if (report != null && report.Exists)

                {

                    _projectDashboardReport = report;

                }

            }

            catch

            {

            }

        }



        _lastRenderSignature = string.Empty;

    }



    private static ProjectDeepScanReportResponse BuildProjectDashboardReportResponse(ProjectDeepScanResponse response)

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



    private async Task<ProjectContextBundleResponse?> TryGetProjectContextBundleAsync(string workspaceId)



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







    private void ApplyProjectStateLocally(ProjectContextBundleResponse? bundle, OnboardingStatusDto? onboarding, string systemMessage, string statusCode)



    {



        CommitLocalWorkerMutation(worker =>



        {



            var effectiveOnboarding = onboarding ?? bundle?.OnboardingStatus ?? worker.OnboardingStatus ?? new OnboardingStatusDto();



            var effectiveWorkspaceId = FirstNonEmpty(bundle?.WorkspaceId, effectiveOnboarding.WorkspaceId, worker.WorkspaceId);



            worker.WorkspaceId = effectiveWorkspaceId;



            worker.OnboardingStatus = effectiveOnboarding;



            worker.ContextSummary.DocumentTitle = FirstNonEmpty(worker.ContextSummary.DocumentTitle, UiShellState.CurrentDocumentTitle);



            worker.ContextSummary.ActiveViewName = FirstNonEmpty(worker.ContextSummary.ActiveViewName, UiShellState.CurrentActiveView);



            worker.ContextSummary.WorkspaceId = FirstNonEmpty(effectiveWorkspaceId, worker.ContextSummary.WorkspaceId);



            worker.ContextSummary.ProjectSummary = FirstNonEmpty(bundle?.Summary, effectiveOnboarding.Summary, worker.ContextSummary.ProjectSummary);



            worker.ContextSummary.ProjectPrimaryModelStatus = FirstNonEmpty(bundle?.PrimaryModelStatus, effectiveOnboarding.PrimaryModelStatus, worker.ContextSummary.ProjectPrimaryModelStatus);



            if (bundle != null)



            {



                worker.ContextSummary.ProjectPendingUnknowns = bundle.PendingUnknowns?.ToList() ?? new List<string>();



                worker.ContextSummary.ProjectTopRefs = (bundle.SourceRefs ?? new List<ProjectContextRef>())



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

            FirstNonEmpty(bundle?.WorkspaceId, onboarding?.WorkspaceId, UiShellState.CurrentWorkspaceId),

            bundle,

            null);

    }



    private void ReportToolActionFailure(string fallbackMessage, ToolResponseEnvelope response)



    {



        ReportLocalActionFailure(



            BuildToolFailureMessage(fallbackMessage, response),



            FirstNonEmpty(response?.StatusCode, "TOOL_ACTION_FAILED"));



    }







    private void ReportLocalActionFailure(string message, string statusCode)



    {



        ToastNotification.Show(_toastArea, message, ToastType.Error);



        CommitLocalWorkerMutation(worker =>



        {



            worker.ContextSummary.DocumentTitle = FirstNonEmpty(worker.ContextSummary.DocumentTitle, UiShellState.CurrentDocumentTitle);



            worker.ContextSummary.ActiveViewName = FirstNonEmpty(worker.ContextSummary.ActiveViewName, UiShellState.CurrentActiveView);



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







    private void CommitLocalWorkerMutation(Action<WorkerResponse> mutate)



    {



        var worker = _chatStore.LatestWorkerResponse ?? new WorkerResponse();



        worker.Messages ??= new List<WorkerChatMessage>();



        worker.ContextSummary ??= new WorkerContextSummary();



        worker.OnboardingStatus ??= new OnboardingStatusDto();



        mutate(worker);



        _chatStore.SeedFromWorkerResponse(worker);



        RenderCurrentState();



    }







    private void BeginLocalToolAction()



    {



        _isBusy = true;



        _chatStore.SetBusy(true);



        RenderCurrentState();



    }







    private void EndLocalToolAction()



    {



        _isBusy = false;



        _chatStore.SetBusy(false);



        RenderCurrentState();



    }







    private string ResolveActionWorkspaceId(DocumentSummaryDto document)



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







    private static string ResolveContextRefLabel(ProjectContextRef reference)



    {



        return FirstNonEmpty(reference?.Title, reference?.RelativePath, reference?.SourcePath);



    }







    private static string BuildToolFailureMessage(string fallbackMessage, ToolResponseEnvelope? response)



    {



        var diagnosticMessage = response?.Diagnostics?



            .Select(x => x?.Message?.Trim())



            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));



        return FirstNonEmpty(diagnosticMessage, response?.StatusCode, fallbackMessage);



    }







    private void HandleArtifactAction(ArtifactAttachmentVm artifact, bool open)



    {



        if (open)



        {



            OpenPath(artifact.Path);



        }



        else



        {



            CopyToClipboard(artifact.Path);



        }



    }







    private async Task ExecuteMissionCommandAsync(string commandName)



    {



        if (_isBusy || string.IsNullOrWhiteSpace(_chatStore.MissionId))



        {



            return;



        }







        _isBusy = true;



        _chatStore.SetBusy(true);



        RenderCurrentState();



        _requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, _settings.RequestTimeoutSeconds)));



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



                    response = await _missionClient.ApproveAsync(_chatStore.MissionId, request, _requestCts.Token).ConfigureAwait(true);



                    break;



                case "reject":



                    response = await _missionClient.RejectAsync(_chatStore.MissionId, request, _requestCts.Token).ConfigureAwait(true);



                    break;



                default:



                    response = await _missionClient.ResumeAsync(_chatStore.MissionId, request, _requestCts.Token).ConfigureAwait(true);



                    break;



            }







            ApplyMissionResponse(response);



        }



        catch (Exception ex)



        {



            HandleMissionFailure(ex.Message);



        }



        finally



        {



            _isBusy = false;



            _requestCts?.Dispose();



            _requestCts = null;



            RenderCurrentState();



        }



    }

    private Dictionary<string, Action> BuildBaseShellActions()

    {

        return new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)

        {

            ["new-task"] = StartNewTask,

            ["init-workspace"] = () => _ = InitializeWorkspaceAsync(),

            ["smart-qc"] = () => SendCurrentMessage("smart qc"),

            ["model-health"] = () => SendCurrentMessage("model health"),

            ["project-overview"] = () => SendCurrentMessage("tong quan project context hien tai"),

            ["review-model"] = () => SendCurrentMessage("review model"),

            ["create-sheet"] = () => SendCurrentMessage("create sheet"),

            ["deep-scan"] = () => _ = RunProjectDeepScanAsync()

        };

    }



    private IDictionary<string, Action> BuildSlashPrompts(ChatSessionVm vm)

    {

        var actions = BuildBaseShellActions();

        foreach (var suggestion in vm.LatestWorkerResponse.SuggestedCommands.Take(4))

        {

            var key = Slugify(suggestion.Label);

            if (!string.IsNullOrWhiteSpace(key) && !actions.ContainsKey(key))

            {

                actions[key] = () => SendCurrentMessage(suggestion.Label);

            }

        }

        return actions;

    }



    private string ResolveFooter(ChatSessionVm vm)



    {



        if (vm.IsBusy)



        {



            return "Đang đọc context, kiểm tra an toàn và chuẩn bị phản hồi trong cùng một luồng chat.";



        }







        var onboarding = vm.LatestWorkerResponse.OnboardingStatus ?? new OnboardingStatusDto();



        if (!string.Equals(onboarding.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase))



        {



            var workspaceId = FirstNonEmpty(onboarding.WorkspaceId, vm.LatestWorkerResponse.WorkspaceId, UiShellState.CurrentWorkspaceId, "default");



            return $"Project context chưa sẵn sàng. Dùng /init-workspace để khởi tạo workspace '{workspaceId}'.";



        }







        if (!string.Equals(onboarding.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))



        {



            return "Workspace đã có context cơ bản. Dùng /deep-scan để bổ sung pattern, evidence và findings sâu hơn.";



        }







        if (!string.IsNullOrWhiteSpace(vm.LatestWorkerResponse.ResponseModel))



        {



            var providerLabel = FirstNonEmpty(vm.LatestWorkerResponse.ConfiguredProvider, vm.LatestMissionResponse.ConfiguredProvider);



            var modelLabel = FirstNonEmpty(



                vm.LatestWorkerResponse.ResponseModel,



                vm.LatestMissionResponse.ResponseModel,



                vm.LatestWorkerResponse.PlannerModel,



                vm.LatestMissionResponse.PlannerModel);



            if (string.Equals(vm.LatestWorkerResponse.NarrationMode, WorkerNarrationModes.LlmEnhanced, StringComparison.OrdinalIgnoreCase))



            {



                return $"{providerLabel} {modelLabel} • trả lời: LLM";



            }







            if (string.Equals(vm.LatestWorkerResponse.NarrationMode, WorkerNarrationModes.LlmFallback, StringComparison.OrdinalIgnoreCase))



            {



                return $"{providerLabel} {modelLabel} • trả lời: rule fallback";



            }



        }







        if (!string.IsNullOrWhiteSpace(vm.LatestMissionResponse.PlannerModel))



        {



            return $"{vm.LatestMissionResponse.ConfiguredProvider} {vm.LatestMissionResponse.PlannerModel} • {vm.LatestMissionResponse.ReasoningMode}";



        }







        return "Chat tập trung một luồng. Dùng / để mở slash commands hoặc giao mục tiêu trực tiếp bằng tiếng Việt.";



    }

    private void ReplaceEmptyStateCard()

    {

        var previous = _emptyStateCard;

        var replacement = BuildEmptyStateCard();

        var existingIndex = previous is null ? -1 : _timelinePanel.Children.IndexOf(previous);

        _emptyStateCard = replacement;

        if (existingIndex >= 0)

        {

            _timelinePanel.Children.RemoveAt(existingIndex);

            var insertIndex = Math.Min(existingIndex, _timelinePanel.Children.Count);

            _timelinePanel.Children.Insert(insertIndex, _emptyStateCard);

        }

    }



    private Border BuildEmptyStateCard()

    {

        var vm = _projector.Project(_chatStore);

        var state = BuildProjectBriefState(vm);

        return new ProjectBriefCard(state, RunShellAction);

    }



    private ProjectBriefCardState BuildProjectBriefState(ChatSessionVm vm)

    {

        var worker = vm?.LatestWorkerResponse ?? new WorkerResponse();

        var onboarding = worker.OnboardingStatus ?? new OnboardingStatusDto();

        var context = worker.ContextSummary ?? new WorkerContextSummary();

        var bundle = _projectDashboardBundle ?? new ProjectContextBundleResponse();

        var reportResponse = _projectDashboardReport ?? new ProjectDeepScanReportResponse();

        var report = reportResponse.Exists ? reportResponse.Report ?? new ProjectDeepScanReport() : new ProjectDeepScanReport();

        var initialized = string.Equals(onboarding.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase)

            || bundle.Exists;

        var deepScanCompleted = string.Equals(onboarding.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)

            || string.Equals(bundle.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)

            || reportResponse.Exists;

        var workspaceId = FirstNonEmpty(bundle.WorkspaceId, reportResponse.WorkspaceId, onboarding.WorkspaceId, worker.WorkspaceId, context.WorkspaceId, UiShellState.CurrentWorkspaceId, "default");

        var viewName = FirstNonEmpty(context.ActiveViewName, UiShellState.CurrentActiveViewName, "Active view pending");

        var summary = FirstNonEmpty(report.Summary, bundle.DeepScanSummary, bundle.Summary, context.ProjectSummary, onboarding.Summary,

            initialized

                ? "Workspace da san sang. Em co the dung project context de explain, review va project-aware chat."

                : "Em dang dung live Revit context. Bam Init workspace de mo project-aware grounding va Project Brief.");

        var state = new ProjectBriefCardState

        {

            Title = !initialized

                ? "Project onboarding needed"

                : deepScanCompleted

                    ? "765T Project Brief"

                    : "Project context ready",

            Subtitle = !initialized

                ? $"{FirstNonEmpty(context.DocumentTitle, UiShellState.CurrentDocumentTitle, "Model hien tai")} ? {viewName}"

                : deepScanCompleted

                    ? $"Workspace '{workspaceId}' da co brief grounded tu context bundle va deep scan."

                    : $"Workspace '{workspaceId}' da co context co ban. Chay deep scan de bo sung findings va evidence.",

            Summary = summary,

            ReadinessLabel = "WORKSPACE READINESS",

            ReadinessScore = ComputeProjectBriefReadinessScore(initialized, deepScanCompleted, bundle, report, context),

            ReadinessSummary = BuildProjectBriefReadinessSummary(initialized, deepScanCompleted, bundle, report, context),

            ReadinessAccentKind = deepScanCompleted ? "success" : initialized ? "warning" : "info"

        };



        state.Badges.Add(new ProjectBriefBadge

        {

            Label = (initialized ? "INITIALIZED" : "NOT_INITIALIZED"),

            AccentKind = initialized ? "success" : "info"

        });

        state.Badges.Add(new ProjectBriefBadge

        {

            Label = (deepScanCompleted ? "DEEP SCAN READY" : FirstNonEmpty(onboarding.DeepScanStatus, bundle.DeepScanStatus, ProjectDeepScanStatuses.NotStarted).Replace("_", " ").ToUpperInvariant()),

            AccentKind = deepScanCompleted ? "success" : initialized ? "warning" : "info"

        });

        state.Badges.Add(new ProjectBriefBadge

        {

            Label = FirstNonEmpty(context.GroundingLevel, initialized ? WorkerGroundingLevels.WorkspaceGrounded : WorkerGroundingLevels.LiveContextOnly).ToUpperInvariant(),

            AccentKind = deepScanCompleted ? "success" : initialized ? "info" : "warning"

        });

        state.Badges.Add(new ProjectBriefBadge

        {

            Label = (worker.ConfiguredProvider ?? string.Empty).Length > 0 ? FirstNonEmpty(worker.ConfiguredProvider, _settings.LlmProviderLabel).ToUpperInvariant() : FirstNonEmpty(_settings.LlmProviderLabel, "RULE FIRST").ToUpperInvariant(),

            AccentKind = "violet"

        });



        state.Metrics.Add(new ProjectBriefMetric { Label = "Workspace", Value = workspaceId });

        state.Metrics.Add(new ProjectBriefMetric { Label = "Active view", Value = viewName });

        state.Metrics.Add(new ProjectBriefMetric

        {

            Label = "Refs",

            Value = Math.Max(context.GroundingRefs?.Count ?? 0, (bundle.SourceRefs?.Count ?? 0) + (bundle.TopStandardsRefs?.Count ?? 0) + (bundle.DeepScanRefs?.Count ?? 0)).ToString(CultureInfo.InvariantCulture)

        });

        state.Metrics.Add(new ProjectBriefMetric

        {

            Label = deepScanCompleted ? "Findings" : "Selection",

            Value = deepScanCompleted

                ? Math.Max(bundle.DeepScanFindingCount, report.Findings?.Count ?? 0).ToString(CultureInfo.InvariantCulture)

                : Math.Max(0, context.SelectionCount).ToString(CultureInfo.InvariantCulture)

        });



        AppendUniqueItems(state.Highlights, report.Strengths, 3);

        AppendUniqueItems(state.Highlights, context.ProjectTopRefs?.Select(x => "Ref: " + x), 3);

        AppendUniqueItems(state.Highlights, bundle.SourceRefs?.Select(ResolveContextRefLabel).Select(x => "Source: " + x), 3);



        AppendUniqueItems(state.AttentionItems, report.Weaknesses, 2);

        AppendUniqueItems(state.AttentionItems, report.PendingUnknowns, 3);

        AppendUniqueItems(state.AttentionItems, context.ProjectPendingUnknowns, 3);

        if (state.AttentionItems.Count == 0)

        {

            if (!initialized)

            {

                state.AttentionItems.Add("Project context chua init nen em moi grounded theo live Revit context.");

            }

            else if (!deepScanCompleted)

            {

                state.AttentionItems.Add("Deep scan chua xong nen chua co full findings, strengths va issue buckets.");

            }

            else

            {

                state.AttentionItems.Add("Chua thay them blocker lon. Co the tiep tuc review, explain va quick actions read-first.");

            }

        }



        if (!initialized)

        {

            state.Actions.Add(new ProjectBriefAction { ActionKey = "init-workspace", Label = "Init workspace", AccentKind = "info" });

        }

        else if (!deepScanCompleted)

        {

            state.Actions.Add(new ProjectBriefAction { ActionKey = "deep-scan", Label = "Run deep scan", AccentKind = "warning" });

        }

        state.Actions.Add(new ProjectBriefAction { ActionKey = "project-overview", Label = "Project overview", AccentKind = "info" });

        state.Actions.Add(new ProjectBriefAction { ActionKey = "smart-qc", Label = "Smart QC", AccentKind = "success" });

        state.Actions.Add(new ProjectBriefAction { ActionKey = "review-model", Label = "Review model", AccentKind = "success" });

        return state;

    }



    private static int ComputeProjectBriefReadinessScore(

        bool initialized,

        bool deepScanCompleted,

        ProjectContextBundleResponse bundle,

        ProjectDeepScanReport report,

        WorkerContextSummary context)

    {

        var score = initialized ? 52 : 20;

        if (deepScanCompleted)

        {

            score += 24;

        }

        else if (initialized)

        {

            score += 10;

        }

        score += Math.Min(8, Math.Max(context.GroundingRefs?.Count ?? 0, context.ProjectTopRefs?.Count ?? 0) * 2);

        score -= Math.Min(18, Math.Max(bundle.DeepScanFindingCount, report.Findings?.Count ?? 0) * 2);

        score -= Math.Min(12, Math.Max(bundle.PendingUnknowns?.Count ?? 0, context.ProjectPendingUnknowns?.Count ?? 0) * 3);

        return Math.Max(0, Math.Min(100, score));

    }



    private static string BuildProjectBriefReadinessSummary(

        bool initialized,

        bool deepScanCompleted,

        ProjectContextBundleResponse bundle,

        ProjectDeepScanReport report,

        WorkerContextSummary context)

    {

        if (!initialized)

        {

            return "Dang dung live Revit context. Init workspace de bat Project Brief, grounding refs va project-aware chat.";

        }

        if (!deepScanCompleted)

        {

            return "Workspace da co context co ban. Chay deep scan de co strengths, weaknesses, findings va evidence refs ro rang hon.";

        }

        var findingCount = Math.Max(bundle.DeepScanFindingCount, report.Findings?.Count ?? 0);

        var unknownCount = Math.Max(bundle.PendingUnknowns?.Count ?? 0, context.ProjectPendingUnknowns?.Count ?? 0);

        if (findingCount > 0 || unknownCount > 0)

        {

            return $"Brief da grounded, nhung van con {findingCount} findings va {unknownCount} unknowns can theo doi tiep.";

        }

        return "Brief da grounded bang workspace + deep scan. Co the bat dau explain, review va quick actions read-first.";

    }



    private static void AppendUniqueItems(List<string> target, IEnumerable<string>? items, int limit)

    {

        if (target == null || items == null || limit <= 0)

        {

            return;

        }

        foreach (var item in items)

        {

            var trimmed = (item ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || target.Contains(trimmed, StringComparer.OrdinalIgnoreCase))

            {

                continue;

            }

            target.Add(trimmed);

            if (target.Count >= limit)

            {

                break;

            }

        }

    }



    private bool CanAutoResumeLatestSession(string? currentSessionId)



    {



        if (!string.IsNullOrWhiteSpace(FirstNonEmpty(currentSessionId, _chatStore.SessionId)))



        {



            return false;



        }







        var worker = _chatStore.LatestWorkerResponse ?? new WorkerResponse();



        return (worker.Messages?.Count ?? 0) == 0 || worker.OnboardingStatus != null;



    }







    private async Task InitializeWorkspaceAsync()



    {



        await RunProjectActionAsync(



            async cancellationToken =>



            {



                var document = await ResolveActiveDocumentAsync().ConfigureAwait(true);



                if (document == null)



                {



                    throw new InvalidOperationException("Khong lay duoc active document de chay project init.");



                }







                if (string.IsNullOrWhiteSpace(document.PathName))



                {



                    throw new InvalidOperationException("Model hien tai chua co file path. Hay save model truoc khi init workspace.");



                }







                var sourceRootPath = Path.GetDirectoryName(document.PathName);



                if (string.IsNullOrWhiteSpace(sourceRootPath))



                {



                    throw new InvalidOperationException("Khong resolve duoc source root tu model hien tai.");



                }







                var preview = await _missionClient.PreviewProjectInitAsync(new ProjectInitPreviewRequest



                {



                    SourceRootPath = sourceRootPath,



                    WorkspaceId = NormalizePreferredWorkspaceId(UiShellState.CurrentWorkspaceId),



                    DisplayName = FirstNonEmpty(Path.GetFileName(sourceRootPath), document.Title),



                    PrimaryRevitFilePath = document.PathName



                }, cancellationToken).ConfigureAwait(true);







                if (!preview.IsValid)



                {



                    throw new InvalidOperationException(FirstNonEmpty(preview.Summary, preview.Errors.FirstOrDefault(), "project.init_preview bi block."));



                }







                var workspaceId = FirstNonEmpty(



                    NormalizePreferredWorkspaceId(UiShellState.CurrentWorkspaceId),



                    preview.SuggestedWorkspaceId,



                    preview.WorkspaceId);



                var apply = await _missionClient.ApplyProjectInitAsync(new ProjectInitApplyRequest



                {



                    SourceRootPath = sourceRootPath,



                    WorkspaceId = workspaceId,



                    DisplayName = FirstNonEmpty(preview.WorkspaceId, preview.SuggestedWorkspaceId, Path.GetFileName(sourceRootPath), document.Title),



                    FirmPackIds = preview.FirmPackIds?.ToList() ?? new List<string>(),



                    PrimaryRevitFilePath = document.PathName,



                    IncludeLivePrimaryModelSummary = true,



                    AllowExistingWorkspaceOverwrite = false



                }, cancellationToken).ConfigureAwait(true);







                ApplyProjectOperationState(



                    FirstNonEmpty(apply.Summary, $"Workspace `{apply.WorkspaceId}` da duoc init cho model hien tai."),



                    BuildProjectContextSummary(document, apply.ContextBundle, apply.OnboardingStatus),



                    apply.OnboardingStatus,



                    BuildProjectToolCard(ToolNames.ProjectInitApply, apply.StatusCode, apply.Summary, apply, apply.ProjectContextPath, apply.ManifestReportPath, apply.SummaryReportPath, apply.ProjectBriefPath, apply.PrimaryModelReportPath),



                    apply.ProjectContextPath,



                    apply.ManifestReportPath,



                    apply.SummaryReportPath,



                    apply.ProjectBriefPath,



                    apply.PrimaryModelReportPath);



            await RefreshProjectDashboardStateAsync(apply.WorkspaceId, apply.ContextBundle, null).ConfigureAwait(true);

            }).ConfigureAwait(true);



    }







    private async Task RunProjectDeepScanAsync()



    {



        await RunProjectActionAsync(



            async cancellationToken =>



            {



                var document = await ResolveActiveDocumentAsync().ConfigureAwait(true);



                if (document == null)



                {



                    throw new InvalidOperationException("Khong lay duoc active document de chay project deep scan.");



                }







                var workspaceId = FirstNonEmpty(



                    NormalizePreferredWorkspaceId(UiShellState.CurrentWorkspaceId),



                    _chatStore.LatestWorkerResponse?.OnboardingStatus?.WorkspaceId,



                    _chatStore.LatestWorkerResponse?.WorkspaceId);



                if (string.IsNullOrWhiteSpace(workspaceId))



                {



                    throw new InvalidOperationException("Workspace chua init. Bam Init workspace truoc khi chay deep scan.");



                }







                var response = await _missionClient.RunProjectDeepScanAsync(workspaceId, new ProjectDeepScanRequest



                {



                    WorkspaceId = workspaceId



                }, cancellationToken).ConfigureAwait(true);







                ApplyProjectOperationState(



                    FirstNonEmpty(response.Summary, response.Report?.Summary, $"Project Brain deep scan da hoan tat cho workspace `{workspaceId}`."),



                    BuildProjectContextSummary(document, response.ContextBundle, response.OnboardingStatus),



                    response.OnboardingStatus,



                    BuildProjectToolCard(ToolNames.ProjectDeepScan, response.StatusCode, response.Summary, response, response.ReportPath, response.SummaryReportPath),



                    response.ReportPath,



                    response.SummaryReportPath);



            await RefreshProjectDashboardStateAsync(response.WorkspaceId, response.ContextBundle, BuildProjectDashboardReportResponse(response)).ConfigureAwait(true);

            }).ConfigureAwait(true);



    }







    private async Task RunProjectActionAsync(Func<CancellationToken, Task> action)



    {



        if (_isBusy)



        {



            return;



        }







        _isBusy = true;



        _chatStore.SetBusy(true);



        RenderCurrentState();



        _requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(45, _settings.RequestTimeoutSeconds)));



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



            RenderCurrentState();



        }



    }







    private async Task<DocumentSummaryDto?> ResolveActiveDocumentAsync()



    {



        var response = await InternalToolClient.Instance.CallAsync(ToolNames.DocumentGetActive, "{}", false).ConfigureAwait(true);



        if (!response.Succeeded)



        {



            return null;



        }







        return JsonUtil.DeserializeRequired<DocumentSummaryDto>(response.PayloadJson);



    }







    private void ApplyProjectOperationState(



        string message,



        WorkerContextSummary contextSummary,



        OnboardingStatusDto onboardingStatus,



        WorkerToolCard toolCard,



        params string?[] artifactPaths)



    {



        var worker = CloneLatestWorkerResponse();



        worker.ContextSummary = contextSummary ?? worker.ContextSummary ?? new WorkerContextSummary();



        worker.OnboardingStatus = onboardingStatus ?? worker.OnboardingStatus ?? new OnboardingStatusDto();



        worker.WorkspaceId = FirstNonEmpty(onboardingStatus?.WorkspaceId, contextSummary?.WorkspaceId, worker.WorkspaceId);



        worker.Messages.Add(new WorkerChatMessage



        {



            Role = WorkerMessageRoles.Worker,



            Content = FirstNonEmpty(message, "Project action completed."),



            TimestampUtc = DateTime.UtcNow,



            ToolName = toolCard.ToolName,



            StatusCode = toolCard.StatusCode



        });



        worker.ToolCards = worker.ToolCards ?? new List<WorkerToolCard>();



        worker.ToolCards.Add(toolCard);



        worker.ArtifactRefs = MergeArtifactRefs(worker.ArtifactRefs, artifactPaths);



        worker.ConfiguredProvider = FirstNonEmpty(worker.ConfiguredProvider, _settings.LlmProviderLabel);



        worker.PlannerModel = FirstNonEmpty(worker.PlannerModel, _settings.LlmPlannerModel);



        worker.ResponseModel = FirstNonEmpty(worker.ResponseModel, _settings.LlmResponseModel);



        _chatStore.SeedFromWorkerResponse(worker);



    }







    private void ApplyProjectActionFailure(string? message)



    {



        ToastNotification.Show(_toastArea, FirstNonEmpty(message, "Project action failed."), ToastType.Error);



        var worker = CloneLatestWorkerResponse();



        worker.Messages.Add(new WorkerChatMessage



        {



            Role = WorkerMessageRoles.System,



            Content = FirstNonEmpty(message, "Project action failed."),



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



            ResponseText = FirstNonEmpty(message, "Project action failed.")



        });



    }







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







    private static WorkerContextSummary BuildProjectContextSummary(DocumentSummaryDto document, ProjectContextBundleResponse? bundle, OnboardingStatusDto? onboardingStatus)



    {



        var safeBundle = bundle ?? new ProjectContextBundleResponse();



        var safeOnboarding = onboardingStatus ?? new OnboardingStatusDto();



        return new WorkerContextSummary



        {



            DocumentKey = document?.DocumentKey ?? string.Empty,



            DocumentTitle = document?.Title ?? string.Empty,



            WorkspaceId = FirstNonEmpty(safeBundle.WorkspaceId, safeOnboarding.WorkspaceId),



            ProjectSummary = FirstNonEmpty(safeBundle.DeepScanSummary, safeBundle.Summary, safeOnboarding.Summary),



            ProjectPrimaryModelStatus = FirstNonEmpty(safeBundle.PrimaryModelStatus, safeOnboarding.PrimaryModelStatus),



            ProjectTopRefs = (safeBundle.TopStandardsRefs ?? new List<ProjectContextRef>())



                .Concat(safeBundle.SourceRefs ?? new List<ProjectContextRef>())



                .Concat(safeBundle.DeepScanRefs ?? new List<ProjectContextRef>())



                .Select(x => FirstNonEmpty(x.RelativePath, x.Title, x.RefId))



                .Where(x => !string.IsNullOrWhiteSpace(x))



                .Take(8)



                .ToList(),



            ProjectPendingUnknowns = safeBundle.PendingUnknowns?.ToList() ?? new List<string>(),



            GroundingLevel = safeBundle.Exists



                ? string.Equals(safeBundle.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)



                    ? WorkerGroundingLevels.DeepScanGrounded



                    : WorkerGroundingLevels.WorkspaceGrounded



                : WorkerGroundingLevels.LiveContextOnly,



            GroundingSummary = safeBundle.Exists



                ? FirstNonEmpty(safeBundle.DeepScanSummary, safeBundle.Summary, safeOnboarding.Summary)



                : FirstNonEmpty(safeOnboarding.Summary, "Dang dung live Revit context; project context chua init."),



            GroundingRefs = (safeBundle.TopStandardsRefs ?? new List<ProjectContextRef>())



                .Concat(safeBundle.SourceRefs ?? new List<ProjectContextRef>())



                .Concat(safeBundle.DeepScanRefs ?? new List<ProjectContextRef>())



                .Select(x => FirstNonEmpty(x.RelativePath, x.Title, x.RefId))



                .Where(x => !string.IsNullOrWhiteSpace(x))



                .Take(8)



                .ToList()



        };



    }







    private static WorkerToolCard BuildProjectToolCard(string toolName, string statusCode, string? summary, object payload, params string?[] artifactPaths)



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







    private static List<string> MergeArtifactRefs(IEnumerable<string>? existing, IEnumerable<string?>? additional)



    {



        return (existing ?? Enumerable.Empty<string>())



            .Concat((additional ?? Array.Empty<string?>())



                .Where(x => !string.IsNullOrWhiteSpace(x))



                .Select(x => x!.Trim()))



            .Distinct(StringComparer.OrdinalIgnoreCase)



            .ToList();



    }







    private static string NormalizePreferredWorkspaceId(string? workspaceId)



    {



        return string.Equals((workspaceId ?? string.Empty).Trim(), "default", StringComparison.OrdinalIgnoreCase)



            ? string.Empty



            : (workspaceId ?? string.Empty).Trim();



    }







    private void CancelPendingRequest()



    {



        try



        {



            _requestCts?.Cancel();



        }



        catch



        {



        }







        try



        {



            _missionStreamCts?.Cancel();



        }



        catch



        {



        }







        _streamRenderTimer.Stop();



        _streamRenderPending = false;



    }







    private void OpenPath(string path)



    {



        try



        {



            Process.Start(new ProcessStartInfo



            {



                FileName = path,



                UseShellExecute = true



            });



        }



        catch



        {



            ToastNotification.Show(_toastArea, "Cannot open artifact path.", ToastType.Warning);



        }



    }







    private void CopyToClipboard(string text)



    {



        try



        {



            Clipboard.SetText(text);



            ToastNotification.Show(_toastArea, "Path copied.", ToastType.Success);



        }



        catch



        {



            ToastNotification.Show(_toastArea, "Cannot copy path.", ToastType.Warning);



        }



    }







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







    private bool IsNearBottom()



    {



        return _timelineScroll.ScrollableHeight <= 0



            || _timelineScroll.VerticalOffset >= Math.Max(0, _timelineScroll.ScrollableHeight - 48);



    }







    private static string BuildRenderSignature(ChatSessionVm vm, string footerText, IEnumerable<string> suggestionKeys)



    {



        var builder = new StringBuilder();



        builder.Append(vm.SessionId).Append('|')



            .Append(vm.MissionId).Append('|')



            .Append(vm.IsBusy).Append('|')



            .Append(footerText).Append('|')



            .Append(string.Join(",", suggestionKeys ?? Array.Empty<string>()));







        foreach (var message in vm.LatestWorkerResponse.Messages ?? new List<WorkerChatMessage>())



        {



            builder.Append("|msg:")



                .Append(message.Role).Append(':')



                .Append(message.Content).Append(':')



                .Append(message.TimestampUtc.Ticks);



        }







        foreach (var missionEvent in vm.LatestMissionResponse.Events ?? new List<WorkerHostMissionEvent>())



        {



            builder.Append("|evt:")



                .Append(missionEvent.Version).Append(':')



                .Append(missionEvent.EventType).Append(':')



                .Append(missionEvent.Terminal);



        }







        var onboarding = vm.LatestWorkerResponse.OnboardingStatus;



        if (onboarding != null)



        {



            builder.Append("|onboarding:")



                .Append(onboarding.WorkspaceId).Append(':')



                .Append(onboarding.InitStatus).Append(':')



                .Append(onboarding.DeepScanStatus).Append(':')



                .Append(onboarding.Summary);



        }







        builder.Append("|narration:")



            .Append(vm.LatestWorkerResponse.NarrationMode).Append(':')



            .Append(vm.LatestWorkerResponse.NarrationDiagnostics);







        return builder.ToString();



    }







    private static bool ShouldPublishWorkerState(WorkerResponse response)



    {



        if (response == null)



        {



            return false;



        }







        if (!string.IsNullOrWhiteSpace(response.SessionId)



            || !string.IsNullOrWhiteSpace(response.MissionId)



            || !string.IsNullOrWhiteSpace(response.WorkspaceId))



        {



            return true;



        }







        var onboarding = response.OnboardingStatus;



        return onboarding != null



            && (!string.IsNullOrWhiteSpace(onboarding.WorkspaceId)



                || !string.IsNullOrWhiteSpace(onboarding.InitStatus)



                || !string.IsNullOrWhiteSpace(onboarding.DeepScanStatus)



                || !string.IsNullOrWhiteSpace(onboarding.Summary));



    }







    private void StartMissionEventStreamIfNeeded(WorkerHostMissionResponse response)



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







    private void StartMissionEventStream(string missionId, bool resetReconnectAttempts = true)



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







    private async Task StreamMissionEventsAsync(string missionId, CancellationToken cancellationToken)



    {



        try



        {



            await _missionClient.StreamMissionEventsAsync(



                missionId,



                async missionEvent =>



                {



                    await Dispatcher.InvokeAsync(() =>



                    {



                        _chatStore.ApplyMissionEvent(missionEvent);



                        RequestStreamingRender(missionEvent.Terminal || RequiresSnapshotHydration(missionEvent.EventType));



                    }, DispatcherPriority.Background);







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



        }



        catch (Exception ex)



        {



            if (!cancellationToken.IsCancellationRequested



                && _missionStreamReconnectAttempts < 2)



            {



                _missionStreamReconnectAttempts++;



                await Dispatcher.InvokeAsync(() => ToastNotification.Show(_toastArea, "Dang reconnect luong event AI...", ToastType.Info), DispatcherPriority.Background, CancellationToken.None);



                try



                {



                    await Task.Delay(TimeSpan.FromMilliseconds(500 * _missionStreamReconnectAttempts), cancellationToken).ConfigureAwait(false);



                    StartMissionEventStream(missionId, resetReconnectAttempts: false);



                    return;



                }



                catch (OperationCanceledException)



                {



                }



            }



            await Dispatcher.InvokeAsync(() => ToastNotification.Show(_toastArea, FirstNonEmpty(ex.Message, "Mission event stream failed."), ToastType.Warning), DispatcherPriority.Background, CancellationToken.None);



        }



    }







    private async Task RefreshMissionSnapshotAsync(string missionId, bool cancelStreamAfterHydration)



    {



        try



        {



            var snapshot = await _missionClient.GetMissionAsync(missionId, CancellationToken.None).ConfigureAwait(false);



            await Dispatcher.InvokeAsync(() => ApplyMissionResponse(snapshot), DispatcherPriority.Background);



        }



        catch



        {



        }



        finally



        {



            if (cancelStreamAfterHydration)



            {



                CancelMissionEventStream();



            }



        }



    }







    private void CancelMissionEventStream()



    {



        try



        {



            _missionStreamCts?.Cancel();



        }



        catch



        {



        }



        finally



        {



            _missionStreamCts?.Dispose();



            _missionStreamCts = null;



        }



    }







    private void RequestStreamingRender(bool forceImmediate)



    {



        if (forceImmediate)



        {



            RenderCurrentState();



            return;



        }







        _streamRenderPending = true;



        var elapsed = DateTime.UtcNow - _lastRenderUtc;



        if (elapsed >= StreamingRenderInterval || _lastRenderUtc == DateTime.MinValue)



        {



            RenderCurrentState();



            return;



        }







        _streamRenderTimer.Interval = StreamingRenderInterval - elapsed;



        if (!_streamRenderTimer.IsEnabled)



        {



            _streamRenderTimer.Start();



        }



    }







    private static bool RequiresSnapshotHydration(string? eventType)



    {



        return string.Equals(eventType, "ApprovalRequested", StringComparison.OrdinalIgnoreCase)



            || string.Equals(eventType, "TaskBlocked", StringComparison.OrdinalIgnoreCase)



            || string.Equals(eventType, "TaskCanceled", StringComparison.OrdinalIgnoreCase)



            || string.Equals(eventType, "TaskCompleted", StringComparison.OrdinalIgnoreCase);



    }



}




