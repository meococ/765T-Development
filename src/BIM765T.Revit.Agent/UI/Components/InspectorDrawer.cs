using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class InspectorDrawer : Border
{
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly StackPanel _factsPanel;
    private readonly TextBlock _body;
    private readonly Border _closeButton;
    private readonly TextBlock _closeIcon;
    private readonly Border _headerIconContainer;
    private readonly TextBlock _headerIcon;
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _contentStack;
    private UIElement _detailHeader;
    private UIElement _narrativeHeader;

    internal InspectorDrawer()
    {
        Width = AppTheme.InspectorDrawerWidth;
        MaxWidth = AppTheme.InspectorDrawerWidth;
        Background = AppTheme.DrawerBackground;
        BorderBrush = AppTheme.DrawerBorder;
        BorderThickness = new Thickness(1, 0, 0, 0);
        Visibility = Visibility.Collapsed;
        Opacity = 0;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel
        {
            Margin = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceLG, AppTheme.SpaceLG, AppTheme.SpaceMD)
        };
        _closeIcon = new TextBlock
        {
            Text = IconLibrary.Close,
            FontFamily = AppTheme.FontIcon,
            FontSize = 10,
            Foreground = AppTheme.TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _closeButton = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = AppTheme.SurfaceElevated,
            Cursor = Cursors.Hand,
            Child = _closeIcon
        };
        _closeButton.MouseLeftButtonUp += (_, _) => Hide();
        DockPanel.SetDock(_closeButton, Dock.Right);
        header.Children.Add(_closeButton);

        var titleStack = new StackPanel();
        _headerIcon = new TextBlock
        {
            Text = IconLibrary.Inspector,
            FontFamily = AppTheme.FontIcon,
            FontSize = 14,
            Foreground = AppTheme.AccentBlue,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _headerIconContainer = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = AppTheme.SurfaceMuted,
            BorderBrush = AppTheme.SubtleBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM),
            Child = _headerIcon
        };
        titleStack.Children.Add(_headerIconContainer);
        _title = new TextBlock
        {
            Text = "Inspector",
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.SemiBold
        };
        _subtitle = new TextBlock
        {
            Text = "Chon card/message/evidence de xem chi tiet.",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            TextWrapping = TextWrapping.Wrap
        };
        titleStack.Children.Add(_title);
        titleStack.Children.Add(_subtitle);
        header.Children.Add(titleStack);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(AppTheme.SpaceLG, 0, AppTheme.SpaceLG, AppTheme.SpaceLG)
        };
        _contentStack = new StackPanel();
        _detailHeader = BuildSectionHeader(IconLibrary.Explain, "Inspector detail", "Facts, rationale, verification va evidence refs.", AppTheme.AccentBlue, AppTheme.SectionIconBlue);
        _contentStack.Children.Add(_detailHeader);
        _factsPanel = new StackPanel();
        _body = new TextBlock
        {
            Foreground = AppTheme.TextSecondary,
            FontSize = AppTheme.FontBody,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        _contentStack.Children.Add(_factsPanel);
        _contentStack.Children.Add(new Border { Height = AppTheme.SpaceSM, Opacity = 0 });
        _narrativeHeader = BuildSectionHeader(IconLibrary.Info, "Narrative", string.Empty, AppTheme.Info, AppTheme.SectionIconCyan);
        _contentStack.Children.Add(_narrativeHeader);
        _contentStack.Children.Add(_body);
        _scrollViewer.Content = _contentStack;
        Grid.SetRow(_scrollViewer, 1);
        root.Children.Add(_scrollViewer);

        Child = root;
    }

    internal void ApplyThemeRefresh()
    {
        Background = AppTheme.DrawerBackground;
        BorderBrush = AppTheme.DrawerBorder;
        _closeButton.Background = AppTheme.SurfaceElevated;
        _closeIcon.Foreground = AppTheme.TextSecondary;
        _headerIconContainer.Background = AppTheme.SurfaceMuted;
        _headerIconContainer.BorderBrush = AppTheme.SubtleBorder;
        _headerIcon.Foreground = AppTheme.AccentBlue;
        _title.Foreground = AppTheme.TextPrimary;
        _subtitle.Foreground = AppTheme.TextMuted;
        _body.Foreground = AppTheme.TextSecondary;
        RebuildSectionHeaders();
    }

    internal void Show(string title, string subtitle, string body, IEnumerable<KeyValuePair<string, string>>? facts = null)
    {
        _title.Text = string.IsNullOrWhiteSpace(title) ? "Inspector" : title;
        _subtitle.Text = string.IsNullOrWhiteSpace(subtitle) ? string.Empty : subtitle;
        _body.Text = string.IsNullOrWhiteSpace(body) ? "Khong co du lieu chi tiet." : body;
        _factsPanel.Children.Clear();

        foreach (var fact in facts ?? new List<KeyValuePair<string, string>>())
        {
            _factsPanel.Children.Add(UIFactory.DetailRow(fact.Key, fact.Value));
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            AnimationHelper.SlideIn(this, 12, AppTheme.AnimFast);
        }
        else
        {
            Opacity = 1;
        }
    }

    internal void Hide()
    {
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        AnimationHelper.SlideOut(this, 12, AppTheme.AnimFast);
    }

    private void RebuildSectionHeaders()
    {
        var detailIndex = _contentStack.Children.IndexOf(_detailHeader);
        if (detailIndex >= 0)
        {
            _contentStack.Children.RemoveAt(detailIndex);
            _detailHeader = BuildSectionHeader(IconLibrary.Explain, "Inspector detail", "Facts, rationale, verification va evidence refs.", AppTheme.AccentBlue, AppTheme.SectionIconBlue);
            _contentStack.Children.Insert(detailIndex, _detailHeader);
        }

        var narrativeIndex = _contentStack.Children.IndexOf(_narrativeHeader);
        if (narrativeIndex >= 0)
        {
            _contentStack.Children.RemoveAt(narrativeIndex);
            _narrativeHeader = BuildSectionHeader(IconLibrary.Info, "Narrative", string.Empty, AppTheme.Info, AppTheme.SectionIconCyan);
            _contentStack.Children.Insert(narrativeIndex, _narrativeHeader);
        }
    }

    private static UIElement BuildSectionHeader(string glyph, string title, string subtitle, System.Windows.Media.SolidColorBrush accent, System.Windows.Media.SolidColorBrush iconBackground)
    {
        return UIFactory.SectionWithIcon(glyph, title, subtitle, accent, iconBackground);
    }
}
