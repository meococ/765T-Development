using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class DashboardSidebar : Border
{
    private readonly StackPanel _sessionsPanel;
    private readonly TextBlock _sessionSummary;
    private readonly WrapPanel _footerBadges;
    private readonly TextBlock _brandTitle;
    private readonly TextBlock _brandSubtitle;
    private readonly TextBlock _sessionsHeader;
    private readonly Border _brandLogoContainer;
    private readonly TextBlock _brandLogoIcon;
    private readonly Border _newTaskButton;
    private readonly Border _footer;
    private readonly ScrollViewer _contentScroll;
    private readonly TextBlock _projectHeader;
    private readonly TextBlock _projectSummary;
    private readonly TextBlock _projectMeta;
    private readonly WrapPanel _projectBadges;
    private readonly WrapPanel _quickActions;
    private readonly TextBlock _readinessSummary;
    private readonly TextBlock _readinessScore;
    private readonly ProgressBar _readinessProgress;
    private readonly WrapPanel _runtimeBadges;
    private readonly Border _projectCard;
    private readonly Border _readinessCard;
    private readonly Border _runtimeCard;
    private readonly Border _quickActionsCard;
    private readonly Border _sessionsCard;
    private readonly List<TextBlock> _sectionLabels = new List<TextBlock>();
    private string _currentSessionId = string.Empty;
    private IReadOnlyList<WorkerSessionSummary> _sessionCache = Array.Empty<WorkerSessionSummary>();
    private string? _personaIdCache;
    private string? _providerLabelCache;
    private string? _themeModeCache;
    private SidebarAmbientState _ambientCache = new SidebarAmbientState();

    internal event Action? NewTaskRequested;
    internal event Action<string>? SessionRequested;
    internal event Action<string>? QuickActionRequested;

    internal DashboardSidebar()
    {
        Width = 248;
        Background = AppTheme.SidebarBackground;
        BorderBrush = AppTheme.SubtleBorder;
        BorderThickness = new Thickness(0, 0, 1, 0);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel
        {
            Margin = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceLG, AppTheme.SpaceMD, AppTheme.SpaceMD)
        };
        var brandRow = new DockPanel();
        _brandLogoContainer = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = AppTheme.BotAvatarBg,
            BorderBrush = AppTheme.BotAvatarBorder,
            BorderThickness = new Thickness(1),
        };
        _brandLogoIcon = new TextBlock
        {
            Text = IconLibrary.Building,
            FontFamily = AppTheme.FontIcon,
            FontSize = 16,
            Foreground = AppTheme.AccentBlue,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _brandLogoContainer.Child = _brandLogoIcon;
        brandRow.Children.Add(_brandLogoContainer);
        _brandTitle = new TextBlock
        {
            Text = "BIM765T",
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(AppTheme.SpaceSM, 5, 0, 0)
        };
        brandRow.Children.Add(_brandTitle);
        brand.Children.Add(brandRow);
        _brandSubtitle = new TextBlock
        {
            Text = "Flow shell cho Revit. Em giu context, session va project brief ngay trong pane.",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            FontWeight = AppTheme.WeightBody,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        brand.Children.Add(_brandSubtitle);
        Grid.SetRow(brand, 0);
        root.Children.Add(brand);

        _newTaskButton = CreateActionButton("New Task", IconLibrary.Play);
        _newTaskButton.Margin = new Thickness(AppTheme.SpaceMD, 0, AppTheme.SpaceMD, AppTheme.SpaceMD);
        _newTaskButton.MouseLeftButtonUp += (_, _) => NewTaskRequested?.Invoke();
        Grid.SetRow(_newTaskButton, 1);
        root.Children.Add(_newTaskButton);

        _contentScroll = new ScrollViewer
        {
            Margin = new Thickness(AppTheme.SpaceMD, 0, AppTheme.SpaceMD, AppTheme.SpaceMD),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent
        };
        var contentStack = new StackPanel();

        _sessionsHeader = new TextBlock
        {
            Text = "RECENT SESSIONS",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSmall,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM)
        };
        contentStack.Children.Add(_sessionsHeader);

        _sessionSummary = new TextBlock
        {
            Text = "No resumable sessions yet.",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM),
            TextWrapping = TextWrapping.Wrap
        };
        _sessionsPanel = new StackPanel();
        var sessionsInner = new StackPanel();
        sessionsInner.Children.Add(_sessionSummary);
        sessionsInner.Children.Add(_sessionsPanel);
        _sessionsCard = UIFactory.Card(sessionsInner, AppTheme.CardRadius);
        _sessionsCard.Margin = new Thickness(0, 0, 0, AppTheme.SpaceLG);
        contentStack.Children.Add(_sessionsCard);

        contentStack.Children.Add(CreateSectionLabel("PROJECT BRIEF"));
        _projectHeader = new TextBlock
        {
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _projectMeta = new TextBlock
        {
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0)
        };
        _projectSummary = new TextBlock
        {
            Foreground = AppTheme.TextSecondary,
            FontSize = AppTheme.FontCaption,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0),
            LineHeight = 18
        };
        _projectBadges = new WrapPanel
        {
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
        };
        var projectInner = new StackPanel();
        projectInner.Children.Add(_projectHeader);
        projectInner.Children.Add(_projectMeta);
        projectInner.Children.Add(_projectSummary);
        projectInner.Children.Add(_projectBadges);
        _projectCard = UIFactory.Card(projectInner, AppTheme.CardRadius);
        _projectCard.Margin = new Thickness(0, 0, 0, AppTheme.SpaceLG);
        contentStack.Children.Add(_projectCard);

        contentStack.Children.Add(CreateSectionLabel("QUICK ACTIONS"));
        _quickActions = new WrapPanel();
        _quickActionsCard = UIFactory.Card(_quickActions, AppTheme.CardRadius);
        _quickActionsCard.Margin = new Thickness(0, 0, 0, AppTheme.SpaceLG);
        contentStack.Children.Add(_quickActionsCard);

        contentStack.Children.Add(CreateSectionLabel("WORKSPACE READINESS"));
        var readinessInner = new StackPanel();
        var readinessRow = new Grid();
        readinessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readinessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _readinessProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 10,
            Background = AppTheme.SurfaceMuted,
            BorderBrush = AppTheme.SubtleBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        };
        Grid.SetColumn(_readinessProgress, 0);
        readinessRow.Children.Add(_readinessProgress);
        _readinessScore = new TextBlock
        {
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontCaption,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_readinessScore, 1);
        readinessRow.Children.Add(_readinessScore);
        readinessInner.Children.Add(readinessRow);
        _readinessSummary = new TextBlock
        {
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
        };
        readinessInner.Children.Add(_readinessSummary);
        _readinessCard = UIFactory.Card(readinessInner, AppTheme.CardRadius);
        _readinessCard.Margin = new Thickness(0, 0, 0, AppTheme.SpaceLG);
        contentStack.Children.Add(_readinessCard);

        contentStack.Children.Add(CreateSectionLabel("RUNTIME"));
        _runtimeBadges = new WrapPanel();
        _runtimeCard = UIFactory.Card(_runtimeBadges, AppTheme.CardRadius);
        contentStack.Children.Add(_runtimeCard);

        _contentScroll.Content = contentStack;
        Grid.SetRow(_contentScroll, 2);
        root.Children.Add(_contentScroll);

        _footer = new Border
        {
            BorderBrush = AppTheme.SubtleBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(AppTheme.SpaceMD)
        };
        _footerBadges = new WrapPanel();
        _footer.Child = _footerBadges;
        Grid.SetRow(_footer, 3);
        root.Children.Add(_footer);

        Child = root;
        SetSessions(Array.Empty<WorkerSessionSummary>(), string.Empty);
        SetAmbient(new SidebarAmbientState());
        SetFooterState(null, null, null);
    }

    internal void SetAmbient(SidebarAmbientState? state)
    {
        _ambientCache = state ?? new SidebarAmbientState();

        _projectHeader.Text = string.IsNullOrWhiteSpace(_ambientCache.DocumentTitle)
            ? "Waiting for active model"
            : _ambientCache.DocumentTitle;
        _projectMeta.Text = string.Join(" • ", new[]
            {
                string.IsNullOrWhiteSpace(_ambientCache.ActiveViewName) ? "No active view" : _ambientCache.ActiveViewName,
                string.IsNullOrWhiteSpace(_ambientCache.WorkspaceId) ? "workspace: default" : "workspace: " + _ambientCache.WorkspaceId,
                string.IsNullOrWhiteSpace(_ambientCache.PrimaryModelStatus) ? null : _ambientCache.PrimaryModelStatus
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        _projectSummary.Text = FirstNonEmpty(
            _ambientCache.ProjectSummary,
            _ambientCache.GroundingSummary,
            "Khoi dong luong chat tu model hien tai. Init workspace de them project-aware grounding.");

        _projectBadges.Children.Clear();
        _projectBadges.Children.Add(new StatusBadge(
            string.IsNullOrWhiteSpace(_ambientCache.InitStatus) ? "NOT_INITIALIZED" : _ambientCache.InitStatus.ToUpperInvariant(),
            ResolveAccentForLifecycle(_ambientCache.InitStatus)));
        _projectBadges.Children.Add(new StatusBadge(
            string.IsNullOrWhiteSpace(_ambientCache.DeepScanStatus) ? "NOT_STARTED" : _ambientCache.DeepScanStatus.ToUpperInvariant(),
            ResolveAccentForDeepScan(_ambientCache.DeepScanStatus)));
        _projectBadges.Children.Add(new StatusBadge(
            string.IsNullOrWhiteSpace(_ambientCache.GroundingLevel) ? "LIVE_CONTEXT_ONLY" : _ambientCache.GroundingLevel.ToUpperInvariant(),
            ResolveAccentForGrounding(_ambientCache.GroundingLevel)));
        if (_ambientCache.SelectionCount > 0)
        {
            _projectBadges.Children.Add(new StatusBadge($"{_ambientCache.SelectionCount} selected", AppTheme.Info));
        }

        _quickActions.Children.Clear();
        foreach (var action in BuildQuickActions(_ambientCache))
        {
            _quickActions.Children.Add(new SuggestionChip(
                action.Label,
                () => QuickActionRequested?.Invoke(action.ActionKey),
                ResolveActionAccent(action.ActionKey)));
        }

        var readinessScore = ComputeReadinessScore(_ambientCache);
        _readinessProgress.Value = readinessScore;
        _readinessProgress.Foreground = ResolveReadinessBrush(readinessScore);
        _readinessScore.Text = readinessScore + "/100";
        _readinessSummary.Text = BuildReadinessSummary(_ambientCache, readinessScore);

        _runtimeBadges.Children.Clear();
        _runtimeBadges.Children.Add(new StatusBadge(_ambientCache.RuntimeOnline ? "RUNTIME ONLINE" : "RUNTIME OFFLINE", _ambientCache.RuntimeOnline ? AppTheme.Success : AppTheme.Danger));
        _runtimeBadges.Children.Add(new StatusBadge(FirstNonEmpty(_ambientCache.RuntimeLabel, "RULE FIRST").ToUpperInvariant(), AppTheme.TextSecondary));
        if (_ambientCache.PendingUnknownCount > 0)
        {
            _runtimeBadges.Children.Add(new StatusBadge(_ambientCache.PendingUnknownCount + " unknowns", AppTheme.Warning));
        }

        if (_ambientCache.ReferenceCount > 0)
        {
            _runtimeBadges.Children.Add(new StatusBadge(_ambientCache.ReferenceCount + " refs", AppTheme.AccentBlue));
        }

        if (_ambientCache.FindingCount > 0)
        {
            _runtimeBadges.Children.Add(new StatusBadge(_ambientCache.FindingCount + " findings", AppTheme.Warning));
        }
    }

    internal void SetSessions(IEnumerable<WorkerSessionSummary>? sessions, string? currentSessionId)
    {
        _currentSessionId = currentSessionId?.Trim() ?? string.Empty;

        var safeSessions = (sessions ?? Array.Empty<WorkerSessionSummary>())
            .Where(x => !string.IsNullOrWhiteSpace(x?.SessionId))
            .Select(x => x!)
            .OrderByDescending(x => x.LastUpdatedUtc)
            .Take(8)
            .ToList();

        _sessionCache = safeSessions;
        _sessionsPanel.Children.Clear();

        _sessionSummary.Text = safeSessions.Count == 0
            ? "No resumable sessions yet."
            : $"{safeSessions.Count} sessions available for resume.";

        foreach (var session in safeSessions)
        {
            _sessionsPanel.Children.Add(CreateSessionRow(session));
        }
    }

    internal void SetFooterState(string? personaId, string? providerLabel, string? themeMode)
    {
        _personaIdCache = personaId;
        _providerLabelCache = providerLabel;
        _themeModeCache = themeMode;
        _footerBadges.Children.Clear();
        _footerBadges.Children.Add(new StatusBadge(
            string.IsNullOrWhiteSpace(personaId) ? "FREELANCER" : personaId!.Trim().Replace("_", " ").ToUpperInvariant(),
            AppTheme.AccentBlue));
        _footerBadges.Children.Add(new StatusBadge(
            string.IsNullOrWhiteSpace(providerLabel) ? "RULE FIRST" : providerLabel!.Trim().ToUpperInvariant(),
            AppTheme.TextSecondary));
        _footerBadges.Children.Add(new StatusBadge(
            string.IsNullOrWhiteSpace(themeMode) ? "DARK" : themeMode!.Trim().ToUpperInvariant(),
            AppTheme.IsDarkMode ? AppTheme.Info : AppTheme.Warning));
    }

    internal void ApplyThemeRefresh()
    {
        Background = AppTheme.SidebarBackground;
        BorderBrush = AppTheme.SubtleBorder;
        _contentScroll.Background = Brushes.Transparent;
        _brandLogoContainer.Background = AppTheme.BotAvatarBg;
        _brandLogoContainer.BorderBrush = AppTheme.BotAvatarBorder;
        _brandLogoIcon.Foreground = AppTheme.AccentBlue;
        _brandTitle.Foreground = AppTheme.TextPrimary;
        _brandSubtitle.Foreground = AppTheme.TextMuted;
        _sessionsHeader.Foreground = AppTheme.TextMuted;
        foreach (var label in _sectionLabels)
        {
            label.Foreground = AppTheme.TextMuted;
        }
        _sessionSummary.Foreground = AppTheme.TextMuted;
        _projectHeader.Foreground = AppTheme.TextPrimary;
        _projectMeta.Foreground = AppTheme.TextMuted;
        _projectSummary.Foreground = AppTheme.TextSecondary;
        _readinessSummary.Foreground = AppTheme.TextMuted;
        _readinessScore.Foreground = AppTheme.TextPrimary;
        _readinessProgress.Background = AppTheme.SurfaceMuted;
        _readinessProgress.BorderBrush = AppTheme.SubtleBorder;
        _footer.BorderBrush = AppTheme.SubtleBorder;
        _newTaskButton.Background = AppTheme.GradientAccent;
        _newTaskButton.BorderBrush = AppTheme.FrozenAlpha("#5B8CFF", 0.30);
        _sessionsCard.Background = AppTheme.CardBackground;
        _sessionsCard.BorderBrush = AppTheme.CardBorder;
        _projectCard.Background = AppTheme.CardBackground;
        _projectCard.BorderBrush = AppTheme.CardBorder;
        _quickActionsCard.Background = AppTheme.CardBackground;
        _quickActionsCard.BorderBrush = AppTheme.CardBorder;
        _readinessCard.Background = AppTheme.CardBackground;
        _readinessCard.BorderBrush = AppTheme.CardBorder;
        _runtimeCard.Background = AppTheme.CardBackground;
        _runtimeCard.BorderBrush = AppTheme.CardBorder;
        SetSessions(_sessionCache, _currentSessionId);
        SetAmbient(_ambientCache);
        SetFooterState(_personaIdCache, _providerLabelCache, _themeModeCache);
    }

    private Border CreateSessionRow(WorkerSessionSummary session)
    {
        var isActive = string.Equals(session.SessionId, _currentSessionId, StringComparison.Ordinal);
        var row = new Border
        {
            Background = isActive ? AppTheme.SidebarSelectedBg : AppTheme.SurfaceMuted,
            BorderBrush = isActive ? AppTheme.FocusRing : AppTheme.SubtleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.ButtonRadius),
            Padding = new Thickness(AppTheme.SpaceMD),
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = FirstNonEmpty(session.LastUserMessage, session.DocumentKey, "Untitled task"),
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{session.LastUpdatedUtc.ToLocalTime():HH:mm} • {session.Status.ToUpperInvariant()}",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            FontWeight = AppTheme.WeightBody,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0)
        });
        row.Child = stack;
        row.MouseLeftButtonUp += (_, _) => SessionRequested?.Invoke(session.SessionId);
        return row;
    }

    private Border CreateActionButton(string label, string glyph)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(AppTheme.ButtonRadius),
            Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM + 2, AppTheme.SpaceMD, AppTheme.SpaceSM + 2),
            Cursor = Cursors.Hand,
            Background = AppTheme.GradientAccent,
            BorderBrush = AppTheme.FrozenAlpha("#5B8CFF", 0.30),
            BorderThickness = new Thickness(1)
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(IconLibrary.Create(glyph, 12, Brushes.White));
        content.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0)
        });
        border.Child = content;
        return border;
    }

    private UIElement CreateSectionLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSmall,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM)
        };
        _sectionLabels.Add(label);
        return label;
    }

    private static IEnumerable<SidebarQuickAction> BuildQuickActions(SidebarAmbientState state)
    {
        var actions = new List<SidebarQuickAction>();
        if (!string.Equals(state.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase))
        {
            actions.Add(new SidebarQuickAction("init-workspace", "Init workspace"));
        }
        else if (!string.Equals(state.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            actions.Add(new SidebarQuickAction("deep-scan", "Run deep scan"));
        }

        actions.Add(new SidebarQuickAction("project-overview", "Project overview"));
        actions.Add(new SidebarQuickAction("smart-qc", "Smart QC"));
        actions.Add(new SidebarQuickAction("model-health", "Model health"));
        actions.Add(new SidebarQuickAction("review-model", "Review model"));
        return actions;
    }

    private static int ComputeReadinessScore(SidebarAmbientState state)
    {
        var score = 18;
        if (string.Equals(state.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase))
        {
            score += 34;
        }

        if (string.Equals(state.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }
        else if (string.Equals(state.DeepScanStatus, ProjectDeepScanStatuses.Partial, StringComparison.OrdinalIgnoreCase))
        {
            score += 18;
        }

        score += Math.Min(8, state.ReferenceCount * 2);
        score -= Math.Min(18, state.PendingUnknownCount * 3);
        score -= Math.Min(16, state.FindingCount * 2);
        return Math.Max(0, Math.Min(100, score));
    }

    private static string BuildReadinessSummary(SidebarAmbientState state, int score)
    {
        if (!string.Equals(state.InitStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase))
        {
            return "Pane dang dung live Revit context. Init workspace de bat Project Brief va grounded research.";
        }

        if (!string.Equals(state.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return "Workspace da san sang co ban. Chay deep scan de co findings, refs va brief sau hon.";
        }

        if (score >= 80)
        {
            return "Project context da kha day du de chat grounded, review nhanh va bat dau quick actions.";
        }

        if (state.PendingUnknownCount > 0 || state.FindingCount > 0)
        {
            return $"Context da co, nhung van con {state.PendingUnknownCount} unknowns va {state.FindingCount} findings can theo doi.";
        }

        return "Project context da on dinh. Co the tiep tuc review, explain va project-aware chat.";
    }

    private static SolidColorBrush ResolveReadinessBrush(int score)
    {
        if (score >= 80)
        {
            return AppTheme.Success;
        }

        if (score >= 50)
        {
            return AppTheme.Warning;
        }

        return AppTheme.Info;
    }

    private static SolidColorBrush ResolveAccentForLifecycle(string? initStatus)
    {
        return string.Equals(initStatus, ProjectOnboardingStatuses.Initialized, StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Success
            : AppTheme.Info;
    }

    private static SolidColorBrush ResolveAccentForDeepScan(string? deepScanStatus)
    {
        if (string.Equals(deepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return AppTheme.Success;
        }

        if (string.Equals(deepScanStatus, ProjectDeepScanStatuses.Partial, StringComparison.OrdinalIgnoreCase))
        {
            return AppTheme.Warning;
        }

        return AppTheme.AccentBlue;
    }

    private static SolidColorBrush ResolveAccentForGrounding(string? groundingLevel)
    {
        switch ((groundingLevel ?? string.Empty).Trim())
        {
            case WorkerGroundingLevels.DeepScanGrounded:
                return AppTheme.Success;
            case WorkerGroundingLevels.WorkspaceGrounded:
                return AppTheme.AccentBlue;
            default:
                return AppTheme.Info;
        }
    }

    private static SolidColorBrush ResolveActionAccent(string actionKey)
    {
        switch ((actionKey ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "init-workspace":
                return AppTheme.Info;
            case "deep-scan":
                return AppTheme.Warning;
            case "smart-qc":
            case "review-model":
                return AppTheme.Success;
            default:
                return AppTheme.AccentBlue;
        }
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

internal sealed class SidebarAmbientState
{
    internal string DocumentTitle { get; set; } = string.Empty;

    internal string ActiveViewName { get; set; } = string.Empty;

    internal string WorkspaceId { get; set; } = string.Empty;

    internal string InitStatus { get; set; } = ProjectOnboardingStatuses.NotInitialized;

    internal string DeepScanStatus { get; set; } = ProjectDeepScanStatuses.NotStarted;

    internal string RuntimeLabel { get; set; } = string.Empty;

    internal bool RuntimeOnline { get; set; }

    internal int SelectionCount { get; set; }

    internal string ProjectSummary { get; set; } = string.Empty;

    internal string GroundingLevel { get; set; } = WorkerGroundingLevels.LiveContextOnly;

    internal string GroundingSummary { get; set; } = string.Empty;

    internal string PrimaryModelStatus { get; set; } = string.Empty;

    internal int PendingUnknownCount { get; set; }

    internal int ReferenceCount { get; set; }

    internal int FindingCount { get; set; }
}

internal sealed class SidebarQuickAction
{
    internal SidebarQuickAction(string actionKey, string label)
    {
        ActionKey = actionKey;
        Label = label;
    }

    internal string ActionKey { get; }

    internal string Label { get; }
}
