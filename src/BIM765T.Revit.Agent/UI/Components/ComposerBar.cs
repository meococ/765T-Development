using System;

using System.Collections.Generic;

using System.Linq;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using BIM765T.Revit.Agent.UI.Theme;



namespace BIM765T.Revit.Agent.UI.Components;



internal sealed class ComposerBar : Border

{

    private readonly WrapPanel _chips;

    private readonly TextBox _input;

    private readonly TextBlock _placeholder;

    private readonly TextBlock _footer;

    private readonly Border _shell;

    private readonly Border _sendButton;

    private readonly TextBlock _sendIcon;

    private readonly List<Tuple<string, Action>> _slashPrompts = new List<Tuple<string, Action>>();



    internal event Action<string>? Submitted;



    internal ComposerBar()

    {

        Background = AppTheme.PageBackground;

        BorderBrush = AppTheme.SubtleBorder;

        BorderThickness = new Thickness(0, 1, 0, 0);

        Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceSM, AppTheme.SpaceLG, AppTheme.SpaceLG);



        var stack = new StackPanel();



        _chips = new WrapPanel

        {

            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM),

            Visibility = Visibility.Collapsed

        };

        stack.Children.Add(_chips);



        var grid = new Grid();

        _shell = new Border
        {

            Background = AppTheme.SurfaceElevated,

            BorderBrush = AppTheme.CardBorder,

            BorderThickness = new Thickness(1),

            CornerRadius = new CornerRadius(AppTheme.CardRadius)

        };



        var inputGrid = new Grid();

        _input = new TextBox

        {

            Background = System.Windows.Media.Brushes.Transparent,

            BorderThickness = new Thickness(0),

            Foreground = AppTheme.TextPrimary,

            CaretBrush = AppTheme.AccentBlue,

            Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceMD, 56, AppTheme.SpaceMD),

            FontSize = AppTheme.FontBody,

            FontFamily = AppTheme.FontUI,

            AcceptsReturn = true,

            TextWrapping = TextWrapping.Wrap,

            MinHeight = 52,

            MaxHeight = 140,

            VerticalScrollBarVisibility = ScrollBarVisibility.Auto

        };

        _input.GotFocus += (_, _) => UpdatePlaceholder();

        _input.LostFocus += (_, _) => UpdatePlaceholder();

        _input.TextChanged += (_, _) =>

        {

            UpdatePlaceholder();

            RefreshSlashPrompts();

        };

        _input.GotFocus += (_, _) => _shell.BorderBrush = AppTheme.FocusRing;

        _input.LostFocus += (_, _) => _shell.BorderBrush = AppTheme.CardBorder;

        inputGrid.Children.Add(_input);



        _placeholder = new TextBlock

        {

            Text = "G\u00F5 task b\u1EB1ng ti\u1EBFng Vi\u1EC7t... v\u00ED d\u1EE5: t\u1EA1o 50 sheet, preview purge, /view 3d",

            Foreground = AppTheme.TextMuted,

            FontSize = AppTheme.FontBody,

            Margin = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceMD, 56, AppTheme.SpaceMD),

            IsHitTestVisible = false,

            TextWrapping = TextWrapping.Wrap

        };

        inputGrid.Children.Add(_placeholder);

        _shell.Child = inputGrid;
        grid.Children.Add(_shell);

        _sendIcon = new TextBlock
        {
            Text = IconLibrary.Play,
            FontFamily = AppTheme.FontIcon,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _sendButton = new Border
        {
            Width = 36,
            Height = 36,
            Background = AppTheme.GradientAccent,
            CornerRadius = new CornerRadius(AppTheme.ButtonRadius),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, AppTheme.SpaceSM),
            Cursor = Cursors.Hand,
            Child = _sendIcon
        };
        _sendButton.MouseEnter += (_, _) =>
        {
            _sendButton.Opacity = 0.92;
            AnimationHelper.ScaleTo(_sendButton, 1.04, AppTheme.AnimFast);
        };
        _sendButton.MouseLeave += (_, _) =>
        {
            _sendButton.Opacity = 1.0;
            AnimationHelper.ScaleTo(_sendButton, 1.0, AppTheme.AnimFast);
        };
        _sendButton.MouseLeftButtonUp += (_, _) => Submit();
        grid.Children.Add(_sendButton);



        _input.PreviewKeyDown += (s, e) =>

        {

            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)

            {

                e.Handled = true;

                Submit();

            }

        };



        stack.Children.Add(grid);

        _footer = new TextBlock

        {

            Text = "Enter \u0111\u1EC3 g\u1EEDi \u2022 Shift+Enter xu\u1ED1ng d\u00F2ng \u2022 G\u00F5 / \u0111\u1EC3 m\u1EDF slash commands",

            Foreground = AppTheme.TextMuted,

            FontSize = AppTheme.FontCaption,

            Margin = new Thickness(AppTheme.SpaceXS, AppTheme.SpaceSM, 0, 0)

        };

        stack.Children.Add(_footer);

        Child = stack;

        UpdatePlaceholder();

    }



    internal void SetSuggestionPrompts(IEnumerable<Tuple<string, Action>> prompts)

    {

        _slashPrompts.Clear();

        _slashPrompts.AddRange(prompts ?? Array.Empty<Tuple<string, Action>>());

        RefreshSlashPrompts();

    }



    internal void SetContextItems(IEnumerable<string> items)

    {

        // Context pills are hidden in the primary chat UX.

    }



    internal void SetFooterText(string text)

    {

        _footer.Text = text;

    }



    internal void ClearInput()

    {

        _input.Clear();

        UpdatePlaceholder();

        RefreshSlashPrompts();

    }



    internal void FocusInput()

    {

        _input.Focus();

        _input.CaretIndex = _input.Text?.Length ?? 0;

    }

    internal void ApplyThemeRefresh()

    {

        Background = AppTheme.PageBackground;

        BorderBrush = AppTheme.SubtleBorder;

        Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceSM, AppTheme.SpaceLG, AppTheme.SpaceLG);

        _shell.Background = AppTheme.SurfaceElevated;

        _shell.BorderBrush = _input.IsKeyboardFocused ? AppTheme.FocusRing : AppTheme.CardBorder;

        _input.Foreground = AppTheme.TextPrimary;

        _input.CaretBrush = AppTheme.AccentBlue;

        _placeholder.Foreground = AppTheme.TextMuted;

        _footer.Foreground = AppTheme.TextMuted;

        _sendButton.Background = AppTheme.GradientAccent;

        _sendIcon.Foreground = System.Windows.Media.Brushes.White;

        RefreshSlashPrompts();

        UpdatePlaceholder();

    }



    private void UpdatePlaceholder()

    {

        _placeholder.Visibility = string.IsNullOrWhiteSpace(_input.Text) && !_input.IsKeyboardFocused

            ? Visibility.Visible

            : Visibility.Collapsed;

    }



    private void RefreshSlashPrompts()

    {

        var text = (_input.Text ?? string.Empty).TrimStart();

        if (!text.StartsWith("/", StringComparison.Ordinal))

        {

            _chips.Children.Clear();

            _chips.Visibility = Visibility.Collapsed;

            return;

        }



        var query = text.Substring(1).Trim();

        var matches = _slashPrompts

            .Where(x => string.IsNullOrWhiteSpace(query) || x.Item1.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)

            .Take(6)

            .ToList();

        _chips.Children.Clear();

        foreach (var prompt in matches)

        {

            _chips.Children.Add(new SuggestionChip("/" + prompt.Item1, () => prompt.Item2()));

        }



        _chips.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    }



    private void Submit()

    {

        var text = _input.Text?.Trim();

        if (string.IsNullOrWhiteSpace(text))

        {

            return;

        }



        Submitted?.Invoke(text!);

    }

}

