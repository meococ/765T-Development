using System;

using System.Diagnostics.CodeAnalysis;

using System.Linq;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Threading;

using BIM765T.Revit.Agent.Config;

using BIM765T.Revit.Agent.Infrastructure;

using BIM765T.Revit.Agent.UI.Chat;

using BIM765T.Revit.Agent.UI.Components;

using BIM765T.Revit.Agent.UI.Tabs;

using BIM765T.Revit.Agent.UI.Theme;

using BIM765T.Revit.Contracts.Bridge;

using BIM765T.Revit.Contracts.Context;

using BIM765T.Revit.Contracts.Platform;

using BIM765T.Revit.Contracts.Serialization;



namespace BIM765T.Revit.Agent.UI;



[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "AgentPaneControl lives for the lifetime of the dockable pane.")]

public sealed partial class AgentPaneControl : UserControl

{

    private readonly AgentSettings _settings;

    private GlobalProgressBar _progressBar = null!;

    private DashboardSidebar _sidebar = null!;

    private PaneTopBar _topBar = null!;

    private Border _contentHost = null!;

    private InspectorDrawer _inspectorDrawer = null!;

    private WorkerTab _workerSurface = null!;

    private readonly WorkerHostMissionClient _missionClient;

    private readonly DispatcherTimer _ambientRefreshTimer;

    private bool _bootstrapped;

    private bool _ambientRefreshInFlight;

    private bool _sessionRailRefreshInFlight;

    private DateTime _lastAmbientRefreshUtc = DateTime.MinValue;

    private DateTime _lastBundleRefreshUtc = DateTime.MinValue;

    private DateTime _lastSessionRailRefreshUtc = DateTime.MinValue;

    private string _lastBundleWorkspaceId = string.Empty;

    private string _lastSessionRailSessionId = string.Empty;

    private ProjectContextBundleResponse? _cachedAmbientBundle;

    private bool _themeTransitionInFlight;



    internal AgentPaneControl(AgentSettings settings)

    {

        InitializeComponent();

        _settings = settings;

        _missionClient = new WorkerHostMissionClient();

        _ambientRefreshTimer = new DispatcherTimer

        {

            Interval = TimeSpan.FromSeconds(12)

        };

        _ambientRefreshTimer.Tick += HandleAmbientRefreshTick;

        AppTheme.SetMode(_settings.UiThemeMode);



        InternalToolClient.Initialize(Dispatcher);

        InternalToolClient.Instance.ToolStarted += OnToolStarted;

        InternalToolClient.Instance.ToolCompleted += OnToolCompleted;



        CreateShellComponents();

        BuildLayout();



        Loaded += OnFirstLoaded;

        Unloaded += OnUnloaded;

        PreviewKeyDown += OnPreviewKeyDown;

    }



    internal void SwitchToTab(int legacyIndex)

    {

        _workerSurface.FocusComposer();

        _inspectorDrawer.Hide();

    }



    private void CreateShellComponents(bool preserveWorkerSurface = false)

    {

        _progressBar = new GlobalProgressBar();

        _sidebar = new DashboardSidebar();

        _sidebar.NewTaskRequested += HandleNewTaskRequested;

        _sidebar.SessionRequested += HandleSessionRequested;
        _sidebar.QuickActionRequested += HandleSidebarQuickActionRequested;



        _topBar = new PaneTopBar(_settings);

        _topBar.ThemeToggleRequested += HandleThemeToggleRequested;

        _topBar.SetThemeMode(_settings.UiThemeMode);



        if (!preserveWorkerSurface || _workerSurface == null)

        {

            _workerSurface = new WorkerTab(_settings, HandleWorkerStateChanged, OpenInspector);

        }

        else

        {

            _workerSurface.ApplyThemeRefresh();

        }

        _contentHost = new Border

        {

            Background = AppTheme.PageBackground,

            Child = _workerSurface

        };



        _inspectorDrawer = new InspectorDrawer();

        _sidebar.SetFooterState(_settings.DefaultWorkerProfile?.PersonaId, _settings.LlmProfileLabel, _settings.UiThemeMode);

        _topBar.SetThemeToggleEnabled(!_themeTransitionInFlight && (_workerSurface?.IsBusy != true));

    }



    private void BuildLayout()

    {

        var root = RootGrid;

        root.Children.Clear();

        root.RowDefinitions.Clear();

        root.ColumnDefinitions.Clear();

        root.Background = AppTheme.PageBackground;



        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });



        Grid.SetRow(_progressBar, 0);

        root.Children.Add(_progressBar);



        var body = new Grid();

        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });



        Grid.SetColumn(_sidebar, 0);

        body.Children.Add(_sidebar);



        var main = new Grid();

        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });



        Grid.SetRow(_topBar, 0);

        main.Children.Add(_topBar);



        Grid.SetRow(_contentHost, 1);

        main.Children.Add(_contentHost);



        Grid.SetColumn(main, 1);

        body.Children.Add(main);



        Grid.SetColumn(_inspectorDrawer, 2);

        body.Children.Add(_inspectorDrawer);



        Grid.SetRow(body, 1);

        root.Children.Add(body);

    }



    private async void OnFirstLoaded(object sender, RoutedEventArgs e)

    {

        _ambientRefreshTimer.Start();

        if (_bootstrapped)

        {

            return;

        }



        _bootstrapped = true;

        await RefreshAmbientContextAsync(force: true);

        await RefreshSessionRailAsync(force: true);

    }



    private void OnUnloaded(object sender, RoutedEventArgs e)

    {

        _ambientRefreshTimer.Stop();

    }



    private void OnPreviewKeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)

        {

            e.Handled = true;

            _workerSurface.FocusComposer();

            return;

        }



        if (e.Key == Key.Escape)

        {

            _inspectorDrawer.Hide();

        }

    }



    private void OnToolStarted()

    {

        Dispatcher.BeginInvoke(new Action(_progressBar.Start), DispatcherPriority.Background);

    }



    private void OnToolCompleted()

    {

        Dispatcher.BeginInvoke(new Action(_progressBar.Stop), DispatcherPriority.Background);

    }



    private void HandleNewTaskRequested()

    {

        UiShellState.ClearSession();

        _workerSurface.StartNewTask();

        _ = RefreshSessionRailAsync(string.Empty, true);

    }



    private async void HandleSessionRequested(string sessionId)

    {

        if (await _workerSurface.OpenSessionAsync(sessionId))

        {

            await RefreshSessionRailAsync(sessionId, true);

        }

    }

    private void HandleSidebarQuickActionRequested(string actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey))
        {
            return;
        }

        _workerSurface?.RunShellAction(actionKey);
    }



    private void HandleThemeToggleRequested()

    {

        if (_themeTransitionInFlight)

        {

            return;

        }



        if (_workerSurface?.IsBusy == true)

        {

            _workerSurface.ShowToast("AI dang phan hoi. Hay doi xong roi doi theme de tranh crash Revit.", ToastType.Info);

            _topBar.SetThemeToggleEnabled(false);

            return;

        }



        _themeTransitionInFlight = true;

        _topBar.SetThemeToggleEnabled(false);



        try

        {

            _settings.UiThemeMode = AppTheme.ToggleMode();

            SettingsLoader.TrySave(_settings, out _);

            ApplyThemeRefresh();

        }

        catch (Exception ex)

        {

            if (AgentHost.TryGetCurrent(out var runtime))

            {

                runtime?.Logger.Error("Theme toggle failed inside AgentPaneControl.", ex);

            }



            _workerSurface?.ShowToast("Doi theme that bai. 765T da giu transcript hien tai an toan.", ToastType.Error);

        }

        finally

        {

            _themeTransitionInFlight = false;

            _topBar.SetThemeToggleEnabled(_workerSurface?.IsBusy != true);

        }

    }



    private void ApplyThemeRefresh()

    {

        RootGrid.Background = AppTheme.PageBackground;

        _topBar.SetThemeMode(_settings.UiThemeMode);

        _sidebar.SetFooterState(_settings.DefaultWorkerProfile?.PersonaId, _topBar.RuntimeLabel, _settings.UiThemeMode);

        _progressBar.ApplyThemeRefresh();

        _sidebar.ApplyThemeRefresh();

        _topBar.ApplyThemeRefresh();

        _contentHost.Background = AppTheme.PageBackground;

        _workerSurface.ApplyThemeRefresh();

        _inspectorDrawer.ApplyThemeRefresh();

    }



    private void HandleWorkerStateChanged(WorkerResponse response)

    {

        if (response == null)

        {

            return;

        }



        UiShellState.UpdateFromWorker(response);

        var workspaceId = FirstNonEmpty(

            response.OnboardingStatus?.WorkspaceId,

            response.WorkspaceId,

            response.ContextSummary?.WorkspaceId,

            UiShellState.CurrentWorkspaceId);

        var deepScanStatus = FirstNonEmpty(

            response.OnboardingStatus?.DeepScanStatus,

            UiShellState.CurrentDeepScanStatus,

            ProjectDeepScanStatuses.NotStarted);

        var documentTitle = FirstNonEmpty(response.ContextSummary?.DocumentTitle, UiShellState.CurrentDocumentTitle);

        var activeViewName = FirstNonEmpty(response.ContextSummary?.ActiveViewName, UiShellState.CurrentActiveViewName);

        UiShellState.UpdateAmbientDocument(response.ContextSummary?.DocumentKey, documentTitle, activeViewName);

        var contextStatus = ResolveTopBarContextStatus(

            response.OnboardingStatus?.InitStatus,

            deepScanStatus,

            response.ContextSummary?.GroundingLevel,

            response.ContextSummary?.GroundingSummary);



        _topBar.SetSessionTitle(ResolveSessionTitle(response), string.Empty);

        _topBar.SetDocumentContext(documentTitle, activeViewName, contextStatus);

        _topBar.SetWorkspace(workspaceId);

        _topBar.SetDeepScanStatus(deepScanStatus);

        _topBar.SetGuardedState(_settings.AllowWriteTools, !string.IsNullOrWhiteSpace(response.PendingApproval?.PendingActionId));

        _topBar.SetRuntimeState(true, !string.IsNullOrWhiteSpace(response.PendingApproval?.PendingActionId), WorkerSurfaceIds.Assistant);

        _topBar.SetRuntimeProfile(

            FirstNonEmpty(response.ConfiguredProvider, _settings.LlmProviderLabel),

            FirstNonEmpty(response.PlannerModel, _settings.LlmPlannerModel));

        _topBar.SetThemeMode(_settings.UiThemeMode);

        _topBar.SetThemeToggleEnabled(!_themeTransitionInFlight && (_workerSurface?.IsBusy != true));



        _sidebar.SetAmbient(BuildSidebarAmbientState(
            response,
            null,
            documentTitle,
            activeViewName,
            true,
            !string.IsNullOrWhiteSpace(response.PlannerModel) ? $"{response.ConfiguredProvider} {response.PlannerModel}" : _settings.LlmProfileLabel,
            response.ContextSummary?.SelectionCount ?? 0));

        _sidebar.SetFooterState(

            _settings.DefaultWorkerProfile?.PersonaId,

            !string.IsNullOrWhiteSpace(response.PlannerModel) ? $"{response.ConfiguredProvider} {response.PlannerModel}" : _settings.LlmProfileLabel,

            _settings.UiThemeMode);

        if (ShouldRefreshSessionRail(response))

        {

            _ = RefreshSessionRailAsync(response.SessionId, true);

        }



        if (_workerSurface?.IsBusy != true

            && (string.IsNullOrWhiteSpace(response.ContextSummary?.DocumentTitle) || string.IsNullOrWhiteSpace(response.ContextSummary?.ActiveViewName)))

        {

            _ = RefreshAmbientContextAsync();

        }

    }



    private void OpenInspector(string title, string subtitle, string body, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>? facts = null)

    {

        _inspectorDrawer.Show(title, subtitle, body, facts);

    }



    private async Task RefreshAmbientContextAsync(bool force = false)

    {

        if (_ambientRefreshInFlight)

        {

            return;

        }



        var now = DateTime.UtcNow;

        if (!force)

        {

            if (_workerSurface.IsBusy)

            {

                return;

            }



            if (now - _lastAmbientRefreshUtc < TimeSpan.FromSeconds(8))

            {

                return;

            }

        }



        _ambientRefreshInFlight = true;

        try

        {

            var taskContextResponse = await InternalToolClient.Instance.CallAsync(

                ToolNames.SessionGetTaskContext,

                JsonUtil.Serialize(new TaskContextRequest

                {

                    MaxRecentOperations = 1,

                    MaxRecentEvents = 1,

                    IncludeCapabilities = false,

                    IncludeToolCatalog = false

                }),

                false);

            var healthResponse = await InternalToolClient.Instance.CallAsync(ToolNames.SessionGetRuntimeHealth, "{}", false);



            var taskContext = taskContextResponse.Succeeded

                ? JsonUtil.DeserializeRequired<TaskContextResponse>(taskContextResponse.PayloadJson)

                : new TaskContextResponse();

            var document = taskContext.Document ?? new DocumentSummaryDto();

            var view = taskContext.ActiveContext ?? new CurrentContextDto();

            var selection = taskContext.Selection ?? new SelectionSummaryDto();

            var health = healthResponse.Succeeded

                ? JsonUtil.DeserializeRequired<SessionRuntimeHealthResponse>(healthResponse.PayloadJson)

                : new SessionRuntimeHealthResponse();

            var workerContext = new WorkerContextResponse

            {

                WorkspaceId = UiShellState.CurrentWorkspaceId

            };

            if (!string.IsNullOrWhiteSpace(UiShellState.LastSessionId) || string.IsNullOrWhiteSpace(workerContext.WorkspaceId))

            {

                var workerContextResponse = await InternalToolClient.Instance.CallAsync(

                    ToolNames.WorkerGetContext,

                    JsonUtil.Serialize(new WorkerContextRequest

                    {

                        SessionId = UiShellState.LastSessionId,

                        IncludeTaskContext = false,

                        IncludeDeltaSummary = false,

                        MaxRecentOperations = 1,

                        MaxRecentEvents = 1

                    }),

                    false);

                if (workerContextResponse.Succeeded)

                {

                    workerContext = JsonUtil.DeserializeRequired<WorkerContextResponse>(workerContextResponse.PayloadJson);

                }

            }

            WorkerHostGatewayStatus? gatewayStatus = null;

            try

            {

                gatewayStatus = await _missionClient.GetStatusAsync(default);

            }

            catch

            {

            }

            var workspaceId = UiShellState.ResolveWorkspaceId(UiShellState.CurrentWorkspaceId, workerContext.WorkspaceId);

            var bundle = await ResolveAmbientProjectBundleAsync(workspaceId);

            UiShellState.UpdateAmbient(workspaceId, bundle);

            UiShellState.UpdateAmbientDocument(

                FirstNonEmpty(document.DocumentKey, workerContext.TaskContext?.Document?.DocumentKey),

                FirstNonEmpty(document.Title, workerContext.TaskContext?.Document?.Title),

                FirstNonEmpty(view.ViewName, workerContext.TaskContext?.ActiveContext?.ViewName));



            var effectiveWorkspaceId = FirstNonEmpty(bundle?.WorkspaceId, workspaceId);

            var deepScanStatus = FirstNonEmpty(bundle?.DeepScanStatus, UiShellState.CurrentDeepScanStatus, ProjectDeepScanStatuses.NotStarted);

            var sessionTitle = string.Equals(UiShellState.CurrentShellMode, UiShellState.ShellModes.Transcript, StringComparison.OrdinalIgnoreCase)
                ? "Resume dashboard"
                : "765T Dashboard";
            var contextStatus = ResolveTopBarContextStatus(
                bundle?.Exists == true ? ProjectOnboardingStatuses.Initialized : UiShellState.CurrentInitStatus,

                deepScanStatus,

                bundle?.Exists == true && string.Equals(deepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)

                    ? WorkerGroundingLevels.DeepScanGrounded

                    : bundle?.Exists == true

                        ? WorkerGroundingLevels.WorkspaceGrounded

                        : WorkerGroundingLevels.LiveContextOnly,

                bundle?.Summary);



            _topBar.SetSessionTitle(sessionTitle, string.Empty);

            var effectiveViewName = FirstNonEmpty(view.ViewName, UiShellState.CurrentActiveViewName);

            UiShellState.UpdateAmbientDocument(

                FirstNonEmpty(document.DocumentKey, workerContext.TaskContext?.Document?.DocumentKey),

                document.Title,

                effectiveViewName);



            _topBar.SetDocumentContext(document.Title, effectiveViewName, contextStatus);

            _topBar.SetWorkspace(effectiveWorkspaceId);

            _topBar.SetDeepScanStatus(deepScanStatus);

            _topBar.SetRuntimeState(gatewayStatus != null || health.SupportsTaskRuntime, false, WorkerSurfaceIds.Assistant);

            _topBar.SetRuntimeProfile(

                FirstNonEmpty(gatewayStatus?.ConfiguredProvider, health.ConfiguredProvider, _settings.LlmProviderLabel),

                FirstNonEmpty(gatewayStatus?.PlannerModel, health.PlannerModel, _settings.LlmPlannerModel));

            _topBar.SetGuardedState(_settings.AllowWriteTools, false);

            _topBar.SetThemeMode(_settings.UiThemeMode);

            var runtimeProfileLabel = !string.IsNullOrWhiteSpace(gatewayStatus?.PlannerModel)

                ? FirstNonEmpty(gatewayStatus?.ConfiguredProvider, _settings.LlmProviderLabel) + " " + gatewayStatus!.PlannerModel

                : !string.IsNullOrWhiteSpace(health.PlannerModel)

                    ? FirstNonEmpty(health.ConfiguredProvider, _settings.LlmProviderLabel) + " " + health.PlannerModel

                    : _settings.LlmProfileLabel;

            if (health.RestartRequired)

            {

                runtimeProfileLabel = runtimeProfileLabel + " [RESTART]";

            }



            _sidebar.SetAmbient(BuildSidebarAmbientState(
                null,
                bundle,
                document.Title,
                effectiveViewName,
                gatewayStatus != null || health.SupportsTaskRuntime,
                runtimeProfileLabel,
                selection.Count));

            _sidebar.SetFooterState(

                _settings.DefaultWorkerProfile?.PersonaId,

                runtimeProfileLabel,

                _settings.UiThemeMode);

            _lastAmbientRefreshUtc = now;

        }

        catch

        {

            _topBar.SetRuntimeState(false, false, WorkerSurfaceIds.Assistant);

        }

        finally

        {

            _ambientRefreshInFlight = false;

        }

    }



    private async void HandleAmbientRefreshTick(object? sender, EventArgs e)

    {

        await RefreshAmbientContextAsync(force: false);

    }



    private async Task RefreshSessionRailAsync(string? currentSessionId = null, bool force = false)

    {

        if (_sessionRailRefreshInFlight)

        {

            return;

        }



        try

        {

            var requestedSessionId = FirstNonEmpty(currentSessionId, UiShellState.LastSessionId);

            var now = DateTime.UtcNow;

            if (!force

                && (_workerSurface.IsBusy || now - _lastSessionRailRefreshUtc < TimeSpan.FromSeconds(10))

                && string.Equals(requestedSessionId, _lastSessionRailSessionId, StringComparison.OrdinalIgnoreCase))

            {

                return;

            }



            _sessionRailRefreshInFlight = true;

            var response = await InternalToolClient.Instance.CallAsync(

                ToolNames.WorkerListSessions,

                JsonUtil.Serialize(new WorkerListSessionsRequest

                {

                    MaxResults = 8,

                    IncludeEnded = false

                }),

                false);

            if (!response.Succeeded)

            {

                return;

            }



            var sessions = JsonUtil.DeserializeRequired<System.Collections.Generic.List<WorkerSessionSummary>>(response.PayloadJson);

            var resolvedCurrentSessionId = string.Equals(UiShellState.CurrentShellMode, UiShellState.ShellModes.Transcript, StringComparison.OrdinalIgnoreCase)
                ? FirstNonEmpty(currentSessionId, UiShellState.LastSessionId)
                : string.Empty;

            var autoOpenedSessionId = await _workerSurface.TryAutoResumeLatestSessionAsync(sessions, UiShellState.CurrentDocumentKey, resolvedCurrentSessionId);

            var selectedSessionId = FirstNonEmpty(autoOpenedSessionId, resolvedCurrentSessionId, requestedSessionId);
            if (!string.Equals(UiShellState.CurrentShellMode, UiShellState.ShellModes.Transcript, StringComparison.OrdinalIgnoreCase))
            {
                selectedSessionId = string.Empty;
            }
            if (string.Equals(UiShellState.CurrentShellMode, UiShellState.ShellModes.Waiting, StringComparison.OrdinalIgnoreCase))
            {
                selectedSessionId = string.Empty;
            }
            _sidebar.SetSessions(sessions, selectedSessionId);

            _lastSessionRailRefreshUtc = now;

            _lastSessionRailSessionId = selectedSessionId;

        }

        catch

        {

        }

        finally

        {

            _sessionRailRefreshInFlight = false;

        }

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

            false);

        if (!response.Succeeded)

        {

            return null;

        }



        return JsonUtil.DeserializeRequired<ProjectContextBundleResponse>(response.PayloadJson);

    }



    private async Task<ProjectContextBundleResponse?> ResolveAmbientProjectBundleAsync(string workspaceId)

    {

        if (string.IsNullOrWhiteSpace(workspaceId))

        {

            _cachedAmbientBundle = null;

            _lastBundleWorkspaceId = string.Empty;

            _lastBundleRefreshUtc = DateTime.MinValue;

            return null;

        }



        var now = DateTime.UtcNow;

        if (_cachedAmbientBundle != null

            && string.Equals(_lastBundleWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)

            && now - _lastBundleRefreshUtc < TimeSpan.FromSeconds(20))

        {

            return _cachedAmbientBundle;

        }



        var bundle = await TryGetProjectContextBundleAsync(workspaceId);

        _cachedAmbientBundle = bundle;

        _lastBundleWorkspaceId = workspaceId;

        _lastBundleRefreshUtc = now;

        return bundle;

    }



    private bool ShouldRefreshSessionRail(WorkerResponse response)

    {

        if (response == null || string.IsNullOrWhiteSpace(response.SessionId))

        {

            return false;

        }



        if (!string.Equals(response.SessionId, _lastSessionRailSessionId, StringComparison.OrdinalIgnoreCase))

        {

            return true;

        }



        return IsTerminalMissionState(response.MissionStatus);

    }



    private static bool IsTerminalMissionState(string? missionStatus)

    {

        return string.Equals(missionStatus, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)

            || string.Equals(missionStatus, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)

            || string.Equals(missionStatus, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase)

            || string.Equals(missionStatus, WorkerMissionStates.Idle, StringComparison.OrdinalIgnoreCase);

    }



    private static SidebarAmbientState BuildSidebarAmbientState(

        WorkerResponse? response,

        ProjectContextBundleResponse? bundle,

        string? documentTitle,

        string? activeViewName,

        bool runtimeOnline,

        string? runtimeLabel,

        int selectionCount)

    {

        var safeResponse = response ?? new WorkerResponse();

        var safeBundle = bundle ?? new ProjectContextBundleResponse();

        var onboarding = safeResponse.OnboardingStatus ?? safeBundle.OnboardingStatus ?? new OnboardingStatusDto();

        var context = safeResponse.ContextSummary ?? new WorkerContextSummary();



        return new SidebarAmbientState

        {

            DocumentTitle = FirstNonEmpty(documentTitle, context.DocumentTitle, UiShellState.CurrentDocumentTitle),

            ActiveViewName = FirstNonEmpty(activeViewName, context.ActiveViewName, UiShellState.CurrentActiveViewName),

            WorkspaceId = FirstNonEmpty(

                safeBundle.WorkspaceId,

                onboarding.WorkspaceId,

                safeResponse.WorkspaceId,

                context.WorkspaceId,

                UiShellState.CurrentWorkspaceId),

            InitStatus = FirstNonEmpty(

                onboarding.InitStatus,

                safeBundle.Exists ? ProjectOnboardingStatuses.Initialized : string.Empty,

                UiShellState.CurrentInitStatus,

                ProjectOnboardingStatuses.NotInitialized),

            DeepScanStatus = FirstNonEmpty(

                safeBundle.DeepScanStatus,

                onboarding.DeepScanStatus,

                UiShellState.CurrentDeepScanStatus,

                ProjectDeepScanStatuses.NotStarted),

            RuntimeOnline = runtimeOnline,

            RuntimeLabel = runtimeLabel ?? string.Empty,

            SelectionCount = selectionCount,

            ProjectSummary = FirstNonEmpty(safeBundle.DeepScanSummary, safeBundle.Summary, context.ProjectSummary, onboarding.Summary),

            GroundingLevel = FirstNonEmpty(

                context.GroundingLevel,

                safeBundle.Exists

                    ? string.Equals(safeBundle.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)

                        ? WorkerGroundingLevels.DeepScanGrounded

                        : WorkerGroundingLevels.WorkspaceGrounded

                    : WorkerGroundingLevels.LiveContextOnly),

            GroundingSummary = FirstNonEmpty(context.GroundingSummary, safeBundle.DeepScanSummary, safeBundle.Summary, onboarding.Summary),

            PrimaryModelStatus = FirstNonEmpty(safeBundle.PrimaryModelStatus, onboarding.PrimaryModelStatus, context.ProjectPrimaryModelStatus),

            PendingUnknownCount = Math.Max(

                safeBundle.PendingUnknowns?.Count ?? 0,

                context.ProjectPendingUnknowns?.Count ?? 0),

            ReferenceCount = Math.Max(

                (safeBundle.TopStandardsRefs?.Count ?? 0) + (safeBundle.SourceRefs?.Count ?? 0) + (safeBundle.DeepScanRefs?.Count ?? 0),

                context.ProjectTopRefs?.Count ?? 0),

            FindingCount = safeBundle.DeepScanFindingCount

        };

    }



    private static string ResolveSessionTitle(WorkerResponse response)

    {

        var userPrompt = response.Messages.FindLast(x => string.Equals(x.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase))?.Content;

        if (!string.IsNullOrWhiteSpace(userPrompt))

        {

            var trimmed = userPrompt!.Trim();

            return trimmed.Length > 56 ? trimmed.Substring(0, 56) + "..." : trimmed;

        }



        if (!string.IsNullOrWhiteSpace(response.PlanSummary))

        {

            return response.PlanSummary.Trim();

        }



        return "765T Dashboard";

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



    private static string ResolveTopBarContextStatus(string? initStatus, string? deepScanStatus, string? groundingLevel, string? groundingSummary)

    {

        if (!string.Equals(initStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase))

        {

            return "Project context chua init";

        }



        if (!string.Equals(deepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))

        {

            return "Workspace grounded, deep scan pending";

        }



        if (string.Equals(groundingLevel, WorkerGroundingLevels.DeepScanGrounded, StringComparison.OrdinalIgnoreCase))

        {

            return "Deep scan grounded";

        }



        if (string.Equals(groundingLevel, WorkerGroundingLevels.WorkspaceGrounded, StringComparison.OrdinalIgnoreCase))

        {

            return "Workspace grounded";

        }



        return string.IsNullOrWhiteSpace(groundingSummary) ? "Grounded by workspace + deep scan" : groundingSummary!.Trim();

    }

}

