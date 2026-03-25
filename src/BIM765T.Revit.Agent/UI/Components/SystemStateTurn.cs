using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class SystemStateTurn : Border
{
    internal SystemStateTurn(SystemStateTurnVm vm, Func<SystemTurnActionVm, bool> onAction)
    {
        vm ??= new SystemStateTurnVm();
        Background = AppTheme.CardBackground;
        BorderBrush = ResolveAccent(vm.TurnKind);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadius);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD);
        Padding = new Thickness(AppTheme.SpaceMD);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = vm.Title ?? string.Empty,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.Bold
        });
        if (!string.IsNullOrWhiteSpace(vm.Summary))
        {
            stack.Children.Add(new TextBlock
            {
                Text = vm.Summary,
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontCaption,
                FontWeight = AppTheme.WeightBody,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0)
            });
        }

        if (vm.Badges.Count > 0)
        {
            var badgeRow = new WrapPanel { Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0) };
            foreach (var badge in vm.Badges)
            {
                badgeRow.Children.Add(new StatusBadge(badge, AppTheme.TextSecondary));
            }

            stack.Children.Add(badgeRow);
        }

        if (vm.Actions.Count > 0)
        {
            var actions = new WrapPanel { Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0) };
            foreach (var action in vm.Actions)
            {
                actions.Children.Add(new SuggestionChip(action.Label, () => onAction(action), ResolveAccent(vm.TurnKind)));
            }

            stack.Children.Add(actions);
        }

        Child = stack;
    }

    private static System.Windows.Media.SolidColorBrush ResolveAccent(string? turnKind)
    {
        switch ((turnKind ?? string.Empty).Trim())
        {
            case SystemTurnKinds.Approval:
                return AppTheme.Warning;
            case SystemTurnKinds.Fallback:
                return AppTheme.AccentBlue;
            case SystemTurnKinds.Error:
                return AppTheme.Danger;
            default:
                return AppTheme.Info;
        }
    }
}
