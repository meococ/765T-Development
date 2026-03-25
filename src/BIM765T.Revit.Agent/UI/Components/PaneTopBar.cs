using System;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using BIM765T.Revit.Agent.Config;

using BIM765T.Revit.Agent.UI.Theme;



namespace BIM765T.Revit.Agent.UI.Components;



internal sealed class PaneTopBar : Border

{

    private readonly Grid _grid;

    private readonly TextBlock _sessionTitle;

    private readonly TextBlock _documentText;

    private readonly TextBlock _viewText;

    private readonly StatusBadge _runtimeBadge;

    private readonly StatusBadge _guardBadge;

    private readonly Border _themeToggle;

    private readonly TextBlock _themeToggleIcon;

    private readonly TextBlock _themeToggleLabel;

    private string _runtimeLabel;
    private bool _themeToggleEnabled = true;
    private string _sessionTitleCache = "765T";
    private string _documentTitleCache = "No active model";
    private string _activeViewCache = string.Empty;
    private string _contextStatusCache = string.Empty;
    private bool _runtimeConnected = true;
    private bool _hasPendingApproval;
    private bool _allowWrite = true;
    private string _themeModeCache = UiThemeModes.Dark;
    private string _providerCache = string.Empty;
    private string _plannerModelCache = string.Empty;


    internal event Action? ThemeToggleRequested;

    internal string RuntimeLabel => _runtimeLabel;



    internal PaneTopBar(AgentSettings settings)

    {

        _runtimeLabel = settings.LlmProfileLabel;

        Background = AppTheme.CardBackground;

        BorderBrush = AppTheme.SubtleBorder;

        BorderThickness = new Thickness(0, 0, 0, 1);

        Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceMD, AppTheme.SpaceLG, AppTheme.SpaceMD);



        _grid = new Grid();

        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });



        var left = new StackPanel();

        _sessionTitle = new TextBlock

        {

            Text = "765T",

            Foreground = AppTheme.TextPrimary,

            FontSize = AppTheme.FontTitle,

            FontWeight = FontWeights.Bold

        };

        _documentText = new TextBlock

        {

            Text = "No active model",

            Foreground = AppTheme.TextSecondary,

            FontSize = AppTheme.FontBody,

            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0),

            TextWrapping = TextWrapping.Wrap

        };

        _viewText = new TextBlock

        {

            Text = "Waiting for active view context.",

            Foreground = AppTheme.TextMuted,

            FontSize = AppTheme.FontCaption,

            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0)

        };

        left.Children.Add(_sessionTitle);

        left.Children.Add(_documentText);

        left.Children.Add(_viewText);

        Grid.SetColumn(left, 0);

        _grid.Children.Add(left);



        var right = new StackPanel

        {

            Orientation = Orientation.Horizontal,

            HorizontalAlignment = HorizontalAlignment.Right,

            VerticalAlignment = VerticalAlignment.Top

        };

        _runtimeBadge = new StatusBadge(settings.LlmConfigured ? settings.LlmProfileLabel : "RULE FIRST", settings.LlmConfigured ? AppTheme.Success : AppTheme.Warning);

        _guardBadge = new StatusBadge(settings.AllowWriteTools ? "WRITE ON" : "GUARDED", settings.AllowWriteTools ? AppTheme.Success : AppTheme.Warning);



        _themeToggleLabel = new TextBlock

        {

            Text = AppTheme.IsDarkMode ? "LIGHT" : "DARK",

            Foreground = AppTheme.TextPrimary,

            FontSize = AppTheme.FontCaption,

            FontWeight = FontWeights.SemiBold,

            VerticalAlignment = VerticalAlignment.Center

        };
        _themeToggleIcon = new TextBlock
        {
            Text = ResolveThemeToggleGlyph(_themeModeCache),
            FontFamily = AppTheme.FontIcon,
            FontSize = 12,
            Foreground = AppTheme.AccentBlue,
            VerticalAlignment = VerticalAlignment.Center
        };

        _themeToggle = new Border

        {

            Background = AppTheme.SurfaceElevated,

            BorderBrush = AppTheme.CardBorder,

            BorderThickness = new Thickness(1),

            CornerRadius = new CornerRadius(8),

            Padding = new Thickness(AppTheme.SpaceSM, 5, AppTheme.SpaceSM, 5),

            Cursor = Cursors.Hand,

            Child = new StackPanel

            {

                Orientation = Orientation.Horizontal,

                Children =

                {
                    _themeToggleIcon,

                    new Border { Width = AppTheme.SpaceXS, Background = Brushes.Transparent },

                    _themeToggleLabel

                }

            }

        };

        _themeToggle.MouseLeftButtonUp += (_, _) =>
        {
            if (_themeToggleEnabled)
            {
                ThemeToggleRequested?.Invoke();
            }
        };
        _themeToggle.MouseEnter += (_, _) => _themeToggle.BorderBrush = AppTheme.FocusRing;

        _themeToggle.MouseLeave += (_, _) => _themeToggle.BorderBrush = AppTheme.CardBorder;



        right.Children.Add(_runtimeBadge);

        right.Children.Add(_guardBadge);

        right.Children.Add(_themeToggle);

        Grid.SetColumn(right, 1);

        _grid.Children.Add(right);



        Child = _grid;

    }



    internal void SetSessionTitle(string title, string subtitle)

    {

        _sessionTitleCache = string.IsNullOrWhiteSpace(title) ? "765T" : title.Trim();

        _sessionTitle.Text = _sessionTitleCache;

    }



    internal void SetDocumentContext(string documentTitle, string activeView, string? contextStatus = null)

    {

        _documentTitleCache = string.IsNullOrWhiteSpace(documentTitle) ? "No active model" : documentTitle.Trim();

        _activeViewCache = string.IsNullOrWhiteSpace(activeView) ? string.Empty : activeView.Trim();

        _contextStatusCache = string.IsNullOrWhiteSpace(contextStatus) ? string.Empty : contextStatus!.Trim();

        _documentText.Text = _documentTitleCache;

        var view = _activeViewCache;

        var status = _contextStatusCache;

        if (!string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(view))

        {

            _viewText.Text = status + " • " + view;

            return;

        }



        if (!string.IsNullOrWhiteSpace(status))

        {

            _viewText.Text = status;

            return;

        }



        _viewText.Text = string.IsNullOrWhiteSpace(view) ? "Waiting for active view context." : view;

    }



    internal void SetWorkspace(string workspaceId)

    {

        // Workspace status is shown as a compact system turn when it matters.

    }



    internal void SetDeepScanStatus(string deepScanStatus)

    {

        // Deep scan state is shown as a compact system turn when it matters.

    }



    internal void SetRuntimeState(bool connected, bool hasPendingApproval, string shellMode)

    {

        _runtimeConnected = connected;

        _hasPendingApproval = hasPendingApproval;

        var runtimeText = connected ? _runtimeLabel : "OFFLINE";

        _runtimeBadge.Update(runtimeText, connected ? AppTheme.Success : AppTheme.Danger);

        if (hasPendingApproval)

        {

            _guardBadge.Update("APPROVAL", AppTheme.Warning);

        }

    }



    internal void SetGuardedState(bool allowWrite, bool hasPendingApproval)

    {

        _allowWrite = allowWrite;

        _hasPendingApproval = hasPendingApproval;

        if (hasPendingApproval)

        {

            _guardBadge.Update("APPROVAL", AppTheme.Warning);

            return;

        }



        _guardBadge.Update(allowWrite ? "WRITE ON" : "GUARDED", allowWrite ? AppTheme.Success : AppTheme.Warning);

    }



    internal void SetThemeMode(string mode)

    {

        _themeModeCache = AppTheme.NormalizeMode(mode);

        _themeToggleLabel.Text = string.Equals(_themeModeCache, UiThemeModes.Dark, StringComparison.OrdinalIgnoreCase) ? "LIGHT" : "DARK";
        _themeToggleIcon.Text = ResolveThemeToggleGlyph(_themeModeCache);
        _themeToggleIcon.Foreground = AppTheme.AccentBlue;

        _themeToggle.BorderBrush = _themeToggleEnabled ? AppTheme.CardBorder : AppTheme.SubtleBorder;

    }



    internal void SetRuntimeProfile(string provider, string plannerModel)

    {

        _providerCache = provider ?? string.Empty;

        _plannerModelCache = plannerModel ?? string.Empty;

        _runtimeLabel = string.IsNullOrWhiteSpace(_plannerModelCache)

            ? FirstNonEmpty(_providerCache, _runtimeLabel, "RULE FIRST")

            : FirstNonEmpty(_providerCache, "RULE FIRST") + " " + _plannerModelCache;

        _runtimeBadge.Update(_runtimeLabel, AppTheme.Success);

    }

    internal void SetThemeToggleEnabled(bool enabled)

    {

        _themeToggleEnabled = enabled;

        _themeToggle.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;

        _themeToggle.Opacity = enabled ? 1.0 : 0.56;

        _themeToggle.BorderBrush = enabled ? AppTheme.CardBorder : AppTheme.SubtleBorder;

    }

    internal void ApplyThemeRefresh()

    {

        Background = AppTheme.CardBackground;

        BorderBrush = AppTheme.SubtleBorder;

        _sessionTitle.Foreground = AppTheme.TextPrimary;

        _documentText.Foreground = AppTheme.TextSecondary;

        _viewText.Foreground = AppTheme.TextMuted;

        _themeToggle.Background = AppTheme.SurfaceElevated;

        _themeToggleIcon.Foreground = AppTheme.AccentBlue;

        _themeToggleLabel.Foreground = AppTheme.TextPrimary;

        SetSessionTitle(_sessionTitleCache, string.Empty);

        SetDocumentContext(_documentTitleCache, _activeViewCache, _contextStatusCache);

        SetRuntimeProfile(_providerCache, _plannerModelCache);

        SetRuntimeState(_runtimeConnected, _hasPendingApproval, string.Empty);

        SetGuardedState(_allowWrite, _hasPendingApproval);

        SetThemeMode(_themeModeCache);

        SetThemeToggleEnabled(_themeToggleEnabled);

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

    private static string ResolveThemeToggleGlyph(string themeMode)
    {
        return string.Equals(themeMode, UiThemeModes.Dark, StringComparison.OrdinalIgnoreCase)
            ? IconLibrary.Warning
            : IconLibrary.Info;
    }

}

