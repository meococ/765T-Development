using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class EmptyStateHero : Border
{
    private readonly WrapPanel _actions;
    private readonly WrapPanel _badges;

    internal EmptyStateHero(string title, string subtitle)
    {
        Background = AppTheme.CardBackground;
        BorderBrush = AppTheme.ResponseCardBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadiusLarge);
        Padding = new Thickness(AppTheme.SpaceXL);

        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(10),
            Background = AppTheme.BotAvatarBg,
            BorderBrush = AppTheme.BotAvatarBorder,
            BorderThickness = new Thickness(1),
            Effect = AppTheme.GlowBlue,
            Child = IconLibrary.Create(IconLibrary.WorkerChat, 20, AppTheme.AccentBlue)
        });
        stack.Children.Add(new Border { Height = AppTheme.SpaceSM, Opacity = 0 });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = 17,
            FontWeight = FontWeights.Bold
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontBody,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceLG),
            LineHeight = 20
        });

        _badges = new WrapPanel { Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD) };
        _badges.Children.Add(new StatusBadge("QUICK COMMANDS", AppTheme.AccentBlue));
        _badges.Children.Add(new StatusBadge("WORKFLOW SAFE", AppTheme.Warning));
        _badges.Children.Add(new StatusBadge("EVIDENCE READY", AppTheme.Success));
        stack.Children.Add(_badges);

        _actions = new WrapPanel();
        stack.Children.Add(_actions);
        Child = stack;
    }

    internal void SetActions(IEnumerable<Tuple<string, Action>> actions)
    {
        _actions.Children.Clear();
        foreach (var item in actions ?? Array.Empty<Tuple<string, Action>>())
        {
            _actions.Children.Add(new SuggestionChip(item.Item1, item.Item2));
        }
    }
}
