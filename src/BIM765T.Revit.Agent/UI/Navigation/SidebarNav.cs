using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Navigation;

/// <summary>
/// Icon sidebar navigation — 44px width, VS Code style.
/// v2: deeper bg, gradient selected indicator, glow hover, tooltips.
/// </summary>
internal sealed class SidebarNav : Border
{
    private readonly List<NavItem> _items = new List<NavItem>();
    private readonly StackPanel _topStack;
    private readonly StackPanel _bottomStack;
    private int _selectedIndex = -1;

    internal event Action<int>? SelectionChanged;

    internal SidebarNav()
    {
        Width = AppTheme.NavWidth;
        Background = AppTheme.SidebarBackground;
        BorderThickness = new Thickness(0, 0, 1, 0);
        BorderBrush = AppTheme.SubtleBorder;

        var dock = new DockPanel();

        _bottomStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        DockPanel.SetDock(_bottomStack, Dock.Bottom);
        dock.Children.Add(_bottomStack);

        _topStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0)
        };
        dock.Children.Add(_topStack);

        Child = dock;
    }

    internal void AddItem(string iconChar, string tooltip, int tabIndex)
    {
        var item = new NavItem(iconChar, tooltip, tabIndex);
        item.Clicked += OnItemClicked;
        _items.Add(item);
        _topStack.Children.Add(item);
    }

    internal void AddBottomItem(string iconChar, string tooltip, int tabIndex)
    {
        var item = new NavItem(iconChar, tooltip, tabIndex);
        item.Clicked += OnItemClicked;
        _items.Add(item);
        _bottomStack.Children.Add(item);

        if (_bottomStack.Children.Count == 1)
        {
            _bottomStack.Children.Insert(0, new Border
            {
                Height = 1,
                Background = AppTheme.SubtleBorder,
                Margin = new Thickness(AppTheme.SpaceSM + 2, AppTheme.SpaceXS, AppTheme.SpaceSM + 2, AppTheme.SpaceXS)
            });
        }
    }

    internal void Select(int tabIndex)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].TabIndex == tabIndex)
            {
                SetSelected(i);
                return;
            }
        }
    }

    internal int SelectedTabIndex => _selectedIndex >= 0 && _selectedIndex < _items.Count
        ? _items[_selectedIndex].TabIndex : 0;

    private void OnItemClicked(NavItem item)
    {
        var idx = _items.IndexOf(item);
        if (idx < 0 || idx == _selectedIndex) return;
        SetSelected(idx);
        SelectionChanged?.Invoke(item.TabIndex);
    }

    private void SetSelected(int index)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
            _items[_selectedIndex].SetSelected(false);

        _selectedIndex = index;

        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
            _items[_selectedIndex].SetSelected(true);
    }

    // ──────────────────────────────────────────────────────────────
    //  Nav Item — icon + gradient indicator + hover glow
    // ──────────────────────────────────────────────────────────────
    private sealed class NavItem : Border
    {
        private readonly Border _indicator;
        private readonly Border _iconHost;
        private readonly string _iconChar;
        private bool _isSelected;

        internal int TabIndex { get; }
        internal event Action<NavItem>? Clicked;

        internal NavItem(string iconChar, string tooltip, int tabIndex)
        {
            _iconChar = iconChar;
            TabIndex = tabIndex;
            Width = AppTheme.NavWidth;
            Height = 42;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            ToolTip = new ToolTip
            {
                Content = tooltip,
                Background = AppTheme.SurfaceElevated,
                Foreground = AppTheme.TextPrimary,
                BorderBrush = AppTheme.CardBorder,
                Padding = new Thickness(AppTheme.SpaceSM + 2, AppTheme.SpaceXS, AppTheme.SpaceSM + 2, AppTheme.SpaceXS),
                FontSize = AppTheme.FontSecondary,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Right
            };

            var grid = new Grid();

            // Gradient indicator bar (cyan → purple)
            _indicator = new Border
            {
                Width = 3,
                Height = 20,
                Background = AppTheme.GradientCyanPurple,
                CornerRadius = new CornerRadius(0, 2, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0
            };
            grid.Children.Add(_indicator);

            // Icon
            _iconHost = new Border
            {
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = IconLibrary.Create(_iconChar, 18, AppTheme.TextMuted)
            };
            grid.Children.Add(_iconHost);

            Child = grid;

            // Hover — subtle glow
            MouseEnter += (_, __) =>
            {
                if (!_isSelected)
                {
                    AnimationHelper.AnimateBackground(this, AppTheme.ColorFrom("#1C1C20"), AppTheme.AnimFast);
                    _iconHost.Child = IconLibrary.Create(_iconChar, 18, AppTheme.TextPrimary);
                }
            };
            MouseLeave += (_, __) =>
            {
                if (!_isSelected)
                {
                    AnimationHelper.AnimateBackground(this, Colors.Transparent, AppTheme.AnimFast);
                    _iconHost.Child = IconLibrary.Create(_iconChar, 18, AppTheme.TextMuted);
                }
            };
            MouseLeftButtonUp += (_, __) => Clicked?.Invoke(this);
        }

        internal void SetSelected(bool selected)
        {
            _isSelected = selected;
            if (selected)
            {
                AnimationHelper.AnimateBackground(this, AppTheme.SidebarSelectedBg.Color, AppTheme.AnimFast);
                _iconHost.Child = IconLibrary.Create(_iconChar, 18, AppTheme.AccentBlue);
                var fadeIn = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(AppTheme.AnimFast)));
                _indicator.BeginAnimation(OpacityProperty, fadeIn);
            }
            else
            {
                AnimationHelper.AnimateBackground(this, Colors.Transparent, AppTheme.AnimFast);
                _iconHost.Child = IconLibrary.Create(_iconChar, 18, AppTheme.TextMuted);
                var fadeOut = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(AppTheme.AnimFast)));
                _indicator.BeginAnimation(OpacityProperty, fadeOut);
            }
        }
    }
}
