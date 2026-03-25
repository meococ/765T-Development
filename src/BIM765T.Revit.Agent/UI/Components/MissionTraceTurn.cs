using System;
using System.Windows;
using System.Windows.Controls;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class MissionTraceTurn : Border
{
    internal MissionTraceTurn(MissionTraceVm vm)
    {
        vm ??= new MissionTraceVm();
        var expanded = vm.IsExpanded;
        Background = AppTheme.SurfaceElevated;
        BorderBrush = AppTheme.CardBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadius);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD);
        Padding = new Thickness(AppTheme.SpaceMD);

        var stack = new StackPanel();

        var header = new DockPanel();
        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(vm.Title) ? "Mission Trace" : vm.Title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold
        };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);

        var stateBadge = new StatusBadge(
            string.IsNullOrWhiteSpace(vm.State) ? "IDLE" : vm.State.ToUpperInvariant(),
            ResolveAccent(vm.Stage));
        DockPanel.SetDock(stateBadge, Dock.Right);
        header.Children.Add(stateBadge);
        stack.Children.Add(header);

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
            var badgeRow = new WrapPanel
            {
                Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
            };
            foreach (var badge in vm.Badges)
            {
                badgeRow.Children.Add(new StatusBadge(badge, AppTheme.TextSecondary));
            }

            stack.Children.Add(badgeRow);
        }

        if (expanded)
        {
            var eventStack = new StackPanel
            {
                Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
            };
            foreach (var traceEvent in vm.Events)
            {
                var row = new Border
                {
                    BorderBrush = AppTheme.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    Background = AppTheme.CardBackground,
                    CornerRadius = new CornerRadius(AppTheme.ButtonRadius),
                    Padding = new Thickness(AppTheme.SpaceSM, AppTheme.SpaceXS, AppTheme.SpaceSM, AppTheme.SpaceXS),
                    Margin = new Thickness(0, 0, 0, AppTheme.SpaceXS)
                };

                var rowStack = new StackPanel();
                rowStack.Children.Add(new TextBlock
                {
                    Text = traceEvent.EventType,
                    Foreground = ResolveAccent(traceEvent.AccentKind),
                    FontSize = AppTheme.FontCaption,
                    FontWeight = FontWeights.SemiBold
                });
                rowStack.Children.Add(new TextBlock
                {
                    Text = traceEvent.Summary,
                    Foreground = AppTheme.TextSecondary,
                    FontSize = AppTheme.FontCaption,
                    FontWeight = AppTheme.WeightBody,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                row.Child = rowStack;
                eventStack.Children.Add(row);
            }

            stack.Children.Add(eventStack);
        }

        Child = stack;
    }

    private static System.Windows.Media.SolidColorBrush ResolveAccent(string? stage)
    {
        switch ((stage ?? string.Empty).Trim().ToLowerInvariant())
        {
            case BIM765T.Revit.Contracts.Platform.WorkerFlowStages.Plan:
                return AppTheme.AccentBlue;
            case BIM765T.Revit.Contracts.Platform.WorkerFlowStages.Preview:
            case BIM765T.Revit.Contracts.Platform.WorkerFlowStages.Approval:
                return AppTheme.Warning;
            case BIM765T.Revit.Contracts.Platform.WorkerFlowStages.Run:
                return AppTheme.AccentAlt;
            case BIM765T.Revit.Contracts.Platform.WorkerFlowStages.Verify:
            case BIM765T.Revit.Contracts.Platform.WorkerFlowStages.Done:
                return AppTheme.Success;
            case BIM765T.Revit.Contracts.Platform.WorkerFlowStages.Error:
                return AppTheme.Danger;
            default:
                return AppTheme.TextSecondary;
        }
    }
}
