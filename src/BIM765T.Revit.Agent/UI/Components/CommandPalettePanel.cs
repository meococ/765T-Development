using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class CommandPalettePanel : Border
{
    private readonly TextBox _searchBox;
    private readonly TextBlock _searchHint;
    private readonly WrapPanel _pinnedPanel;
    private readonly WrapPanel _recentPanel;
    private readonly WrapPanel _recommendedPanel;
    private readonly StackPanel _resultsPanel;
    private readonly TextBlock _summary;
    private readonly List<(CommandAtlasMatch Match, Border Row)> _resultRows = new List<(CommandAtlasMatch, Border)>();
    private int _selectedResultIndex = -1;

    internal event Action<string>? SearchSubmitted;
    internal event Action<string>? PromptInvoked;
    internal event Action<CommandAtlasEntry>? EntryInvoked;
    internal event Action<CommandAtlasEntry>? EntryInspected;

    internal CommandPalettePanel()
    {
        Background = AppTheme.CardBackground;
        BorderBrush = AppTheme.CardBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadiusLarge);
        Padding = new Thickness(AppTheme.SpaceLG);

        var stack = new StackPanel();
        stack.Children.Add(UIFactory.SectionWithIcon(IconLibrary.Search, "Command Palette", "Keyboard-first. Search -> quick plan -> execute safe.", AppTheme.AccentBlue, AppTheme.SectionIconBlue));

        var searchHost = new Grid { Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM) };
        searchHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var inputBorder = new Border
        {
            Background = AppTheme.SurfaceElevated,
            BorderBrush = AppTheme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Padding = new Thickness(0)
        };

        var inputGrid = new Grid();
        _searchBox = new TextBox
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = AppTheme.TextPrimary,
            CaretBrush = AppTheme.AccentBlue,
            FontSize = AppTheme.FontBody,
            Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM + 2, AppTheme.SpaceMD, AppTheme.SpaceSM + 2)
        };
        _searchBox.GotFocus += (_, _) => UpdateSearchHintVisibility();
        _searchBox.LostFocus += (_, _) => UpdateSearchHintVisibility();
        _searchBox.TextChanged += (_, _) => UpdateSearchHintVisibility();
        _searchBox.GotFocus += (_, _) => inputBorder.BorderBrush = AppTheme.FocusRing;
        _searchBox.LostFocus += (_, _) => inputBorder.BorderBrush = AppTheme.CardBorder;
        _searchBox.PreviewKeyDown += SearchBoxOnPreviewKeyDown;
        inputGrid.Children.Add(_searchBox);

        _searchHint = new TextBlock
        {
            Text = "Go lenh, vi du: create sheet, duplicate view, /qc, /purge",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontBody,
            Margin = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM + 2, 0, 0),
            IsHitTestVisible = false
        };
        inputGrid.Children.Add(_searchHint);
        inputBorder.Child = inputGrid;
        Grid.SetColumn(inputBorder, 0);
        searchHost.Children.Add(inputBorder);

        var shortcutBadge = new Border
        {
            Background = AppTheme.PillBackground,
            BorderBrush = AppTheme.PillBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.BadgeRadius),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "Ctrl+K",
                Foreground = AppTheme.TextSecondary,
                FontSize = AppTheme.FontSmall,
                FontFamily = AppTheme.FontMono
            }
        };
        Grid.SetColumn(shortcutBadge, 1);
        searchHost.Children.Add(shortcutBadge);
        stack.Children.Add(searchHost);

        _summary = new TextBlock
        {
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM)
        };
        stack.Children.Add(_summary);

        stack.Children.Add(UIFactory.SectionWithIcon(IconLibrary.Pin, "Pinned", "Quick actions dung nhieu", AppTheme.AccentBlue, AppTheme.SectionIconBlue));
        _pinnedPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD) };
        stack.Children.Add(_pinnedPanel);

        stack.Children.Add(UIFactory.SectionWithIcon(IconLibrary.WorkerChat, "Recommended", "Theo workspace va task lane hien tai", AppTheme.AccentAlt, AppTheme.SectionIconViolet));
        _recommendedPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD) };
        stack.Children.Add(_recommendedPanel);

        stack.Children.Add(UIFactory.SectionWithIcon(IconLibrary.Clock, "Recent", "Lich su command trong pane", AppTheme.Warning, AppTheme.SectionIconAmber));
        _recentPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD) };
        stack.Children.Add(_recentPanel);

        stack.Children.Add(UIFactory.SectionWithIcon(IconLibrary.Search, "Results", "Atlas entries + quick execution policy", AppTheme.Accent, AppTheme.SectionIconCyan));
        _resultsPanel = new StackPanel();
        stack.Children.Add(_resultsPanel);

        Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        };

        UpdateSearchHintVisibility();
    }

    internal void FocusSearchBox()
    {
        _searchBox.Focus();
        _searchBox.SelectAll();
    }

    internal void SetQueryText(string query)
    {
        _searchBox.Text = query ?? string.Empty;
        UpdateSearchHintVisibility();
    }

    internal void SetSummary(string text)
    {
        _summary.Text = text ?? string.Empty;
    }

    internal void SetPinnedPrompts(IEnumerable<string> prompts)
    {
        RenderPromptStrip(_pinnedPanel, prompts, AppTheme.AccentBlue);
    }

    internal void SetRecentPrompts(IEnumerable<string> prompts)
    {
        RenderPromptStrip(_recentPanel, prompts, AppTheme.Warning);
    }

    internal void SetRecommendedPrompts(IEnumerable<string> prompts)
    {
        RenderPromptStrip(_recommendedPanel, prompts, AppTheme.AccentAlt);
    }

    internal void SetResults(IEnumerable<CommandAtlasMatch> matches)
    {
        _resultsPanel.Children.Clear();
        _resultRows.Clear();
        _selectedResultIndex = -1;

        foreach (var match in matches ?? Enumerable.Empty<CommandAtlasMatch>())
        {
            var row = CreateRow(match);
            _resultRows.Add((match, row));
            _resultsPanel.Children.Add(row);
        }

        if (_resultsPanel.Children.Count == 0)
        {
            _resultsPanel.Children.Add(UIFactory.Card(new TextBlock
            {
                Text = "Khong co ket qua atlas. Thu go ngan gon hon, slash command hoac prompt tu recommended.",
                Foreground = AppTheme.TextMuted,
                TextWrapping = TextWrapping.Wrap
            }, AppTheme.CardRadius));
            return;
        }

        SelectResult(0);
    }

    private void SearchBoxOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _resultRows.Count > 0)
        {
            e.Handled = true;
            SelectResult(Math.Min(_selectedResultIndex + 1, _resultRows.Count - 1));
            return;
        }

        if (e.Key == Key.Up && _resultRows.Count > 0)
        {
            e.Handled = true;
            SelectResult(Math.Max(_selectedResultIndex - 1, 0));
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (_selectedResultIndex >= 0 && _selectedResultIndex < _resultRows.Count && Keyboard.Modifiers == ModifierKeys.None)
            {
                EntryInvoked?.Invoke(_resultRows[_selectedResultIndex].Match.Entry);
                return;
            }

            SearchSubmitted?.Invoke(_searchBox.Text?.Trim() ?? string.Empty);
            return;
        }

        if (e.Key == Key.Escape)
        {
            _searchBox.Clear();
            _resultsPanel.Children.Clear();
            _resultRows.Clear();
            _selectedResultIndex = -1;
            UpdateSearchHintVisibility();
        }
    }

    private void UpdateSearchHintVisibility()
    {
        _searchHint.Visibility = string.IsNullOrWhiteSpace(_searchBox.Text) && !_searchBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RenderPromptStrip(WrapPanel panel, IEnumerable<string> prompts, System.Windows.Media.SolidColorBrush accent)
    {
        panel.Children.Clear();
        foreach (var prompt in prompts ?? Array.Empty<string>())
        {
            panel.Children.Add(new SuggestionChip(prompt, () => PromptInvoked?.Invoke(prompt), accent));
        }
    }

    private Border CreateRow(CommandAtlasMatch match)
    {
        var entry = match.Entry;
        var border = new Border
        {
            Background = AppTheme.ResponseCardBg,
            BorderBrush = AppTheme.ResponseCardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Padding = new Thickness(AppTheme.SpaceMD),
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel();
        var titleRow = new DockPanel();
        titleRow.Children.Add(new TextBlock
        {
            Text = entry.DisplayName,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold
        });
        var badge = new StatusBadge(entry.CoverageStatus.ToUpperInvariant(), ResolveCoverageBrush(entry.CoverageStatus));
        DockPanel.SetDock(badge, Dock.Right);
        titleRow.Children.Add(badge);
        stack.Children.Add(titleRow);
        stack.Children.Add(new TextBlock
        {
            Text = entry.Description,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, AppTheme.SpaceSM)
        });

        var facts = new WrapPanel();
        facts.Children.Add(MakeMetaPill(entry.CommandFamily, AppTheme.AccentBlue));
        facts.Children.Add(MakeMetaPill(entry.ExecutionMode, AppTheme.Info));
        facts.Children.Add(MakeMetaPill(entry.SafetyClass, ResolveSafetyBrush(entry.SafetyClass)));
        facts.Children.Add(MakeMetaPill(match.Reason, AppTheme.TextSecondary));
        stack.Children.Add(facts);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0) };
        actions.Children.Add(new SuggestionChip("Chay", () => EntryInvoked?.Invoke(entry), AppTheme.AccentBlue));
        actions.Children.Add(new SuggestionChip("Inspect", () => EntryInspected?.Invoke(entry), AppTheme.TextSecondary));
        if (entry.CanPreview)
        {
            actions.Children.Add(new SuggestionChip("Preview", () => EntryInvoked?.Invoke(entry), AppTheme.Warning));
        }

        stack.Children.Add(actions);
        border.Child = stack;

        border.MouseLeftButtonUp += (_, _) => EntryInvoked?.Invoke(entry);
        border.MouseEnter += (_, _) =>
        {
            var index = _resultRows.FindIndex(x => ReferenceEquals(x.Row, border));
            if (index >= 0)
            {
                SelectResult(index);
            }
        };

        return border;
    }

    private void SelectResult(int index)
    {
        if (index < 0 || index >= _resultRows.Count)
        {
            return;
        }

        _selectedResultIndex = index;
        for (var i = 0; i < _resultRows.Count; i++)
        {
            var row = _resultRows[i].Row;
            var isSelected = i == index;
            row.Background = isSelected ? AppTheme.SurfaceMuted : AppTheme.ResponseCardBg;
            row.BorderBrush = isSelected ? AppTheme.AccentBlue : AppTheme.ResponseCardBorder;
        }
    }

    private static Border MakeMetaPill(string text, System.Windows.Media.SolidColorBrush accent)
    {
        return new Border
        {
            Background = AppTheme.PillBackground,
            BorderBrush = AppTheme.FrozenAlpha(accent.Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.24),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.BadgeRadius),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, AppTheme.SpaceXS),
            Child = new TextBlock
            {
                Text = text,
                Foreground = accent,
                FontSize = AppTheme.FontSmall
            }
        };
    }

    private static System.Windows.Media.SolidColorBrush ResolveCoverageBrush(string status)
    {
        switch ((status ?? string.Empty).Trim().ToLowerInvariant())
        {
            case CommandCoverageStatuses.Verified: return AppTheme.Success;
            case CommandCoverageStatuses.Previewable: return AppTheme.Warning;
            case CommandCoverageStatuses.Executable: return AppTheme.Accent;
            default: return AppTheme.TextSecondary;
        }
    }

    private static System.Windows.Media.SolidColorBrush ResolveSafetyBrush(string safetyClass)
    {
        switch ((safetyClass ?? string.Empty).Trim().ToLowerInvariant())
        {
            case CommandSafetyClasses.ReadOnly:
            case CommandSafetyClasses.HarmlessUi:
                return AppTheme.Success;
            case CommandSafetyClasses.PreviewedMutation:
                return AppTheme.Warning;
            case CommandSafetyClasses.HighRiskMutation:
                return AppTheme.Danger;
            default:
                return AppTheme.TextSecondary;
        }
    }
}
