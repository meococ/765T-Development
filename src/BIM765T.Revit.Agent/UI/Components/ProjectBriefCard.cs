using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class ProjectBriefCard : Border
{
    internal ProjectBriefCard(ProjectBriefCardState state, Action<string> onAction)
    {
        var safeState = state ?? new ProjectBriefCardState();
        onAction ??= _ => { };

        Background = AppTheme.CardBackground;
        BorderBrush = AppTheme.ResponseCardBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadiusLarge);
        Padding = new Thickness(AppTheme.SpaceXL);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceLG);

        var root = new StackPanel();

        root.Children.Add(BuildHeader(safeState));

        if (safeState.Badges.Count > 0)
        {
            var badges = new WrapPanel
            {
                Margin = new Thickness(0, AppTheme.SpaceMD, 0, 0)
            };
            foreach (var badge in safeState.Badges)
            {
                badges.Children.Add(new StatusBadge(
                    badge.Label,
                    ResolveAccent(badge.AccentKind)));
            }

            root.Children.Add(badges);
        }

        if (!string.IsNullOrWhiteSpace(safeState.Summary))
        {
            root.Children.Add(new TextBlock
            {
                Text = safeState.Summary,
                Foreground = AppTheme.TextSecondary,
                FontSize = AppTheme.FontBody,
                FontWeight = AppTheme.WeightBody,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = AppTheme.LineHeightRelaxed,
                Margin = new Thickness(0, AppTheme.SpaceMD, 0, 0)
            });
        }

        root.Children.Add(BuildReadinessBlock(safeState));

        var insightGrid = new Grid
        {
            Margin = new Thickness(0, AppTheme.SpaceLG, 0, 0)
        };
        insightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        insightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(BuildListSection("Understood", safeState.Highlights, AppTheme.Success), 0);
        insightGrid.Children.Add(BuildListSection("Understood", safeState.Highlights, AppTheme.Success));

        var attention = BuildListSection("Needs attention", safeState.AttentionItems, AppTheme.Warning);
        Grid.SetColumn(attention, 1);
        insightGrid.Children.Add(attention);
        root.Children.Add(insightGrid);

        if (safeState.Metrics.Count > 0)
        {
            root.Children.Add(BuildMetricsGrid(safeState));
        }

        if (safeState.Actions.Count > 0)
        {
            var actionHost = new WrapPanel
            {
                Margin = new Thickness(0, AppTheme.SpaceLG, 0, 0)
            };
            foreach (var action in safeState.Actions)
            {
                actionHost.Children.Add(new SuggestionChip(
                    action.Label,
                    () => onAction(action.ActionKey),
                    ResolveAccent(action.AccentKind)));
            }

            root.Children.Add(actionHost);
        }

        Child = root;
    }

    private static UIElement BuildHeader(ProjectBriefCardState state)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconHost = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(12),
            Background = AppTheme.BotAvatarBg,
            BorderBrush = AppTheme.BotAvatarBorder,
            BorderThickness = new Thickness(1),
            Effect = AppTheme.GlowBlue,
            Child = IconLibrary.Create(IconLibrary.WorkerChat, 22, AppTheme.AccentBlue)
        };
        Grid.SetColumn(iconHost, 0);
        header.Children.Add(iconHost);

        var textStack = new StackPanel
        {
            Margin = new Thickness(AppTheme.SpaceMD, 0, 0, 0)
        };
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(state.Title) ? "765T Worker" : state.Title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.Bold
        });
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(state.Subtitle)
                ? "Em dang tong hop project context cho model hien tai."
                : state.Subtitle,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontBody,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0)
        });
        Grid.SetColumn(textStack, 1);
        header.Children.Add(textStack);

        return header;
    }

    private static UIElement BuildReadinessBlock(ProjectBriefCardState state)
    {
        var score = Math.Max(0, Math.Min(100, state.ReadinessScore));
        var section = new StackPanel
        {
            Margin = new Thickness(0, AppTheme.SpaceLG, 0, 0)
        };
        section.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(state.ReadinessLabel)
                ? "WORKSPACE READINESS"
                : state.ReadinessLabel,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSmall,
            FontWeight = FontWeights.SemiBold
        });

        var progressRow = new Grid
        {
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
        };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = score,
            Height = 10,
            Foreground = ResolveAccent(state.ReadinessAccentKind),
            Background = AppTheme.SurfaceMuted,
            BorderBrush = AppTheme.SubtleBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, AppTheme.SpaceMD, 0)
        };
        Grid.SetColumn(progress, 0);
        progressRow.Children.Add(progress);

        var scoreText = new TextBlock
        {
            Text = score.ToString(CultureInfo.InvariantCulture) + "/100",
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontCaption,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(scoreText, 1);
        progressRow.Children.Add(scoreText);
        section.Children.Add(progressRow);

        if (!string.IsNullOrWhiteSpace(state.ReadinessSummary))
        {
            section.Children.Add(new TextBlock
            {
                Text = state.ReadinessSummary,
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontCaption,
                FontWeight = AppTheme.WeightBody,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0)
            });
        }

        return section;
    }

    private static UIElement BuildListSection(string title, IReadOnlyList<string> items, SolidColorBrush accent)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(0, 0, AppTheme.SpaceLG, 0)
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontCaption,
            FontWeight = FontWeights.SemiBold
        });

        var safeItems = (items ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(4)
            .ToList();
        if (safeItems.Count == 0)
        {
            safeItems.Add("Chua co them evidence noi bat cho muc nay.");
        }

        foreach (var item in safeItems)
        {
            var row = new DockPanel
            {
                Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0),
                LastChildFill = true
            };
            row.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = accent,
                Margin = new Thickness(0, 6, AppTheme.SpaceSM, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            row.Children.Add(new TextBlock
            {
                Text = item,
                Foreground = AppTheme.TextSecondary,
                FontSize = AppTheme.FontCaption,
                FontWeight = AppTheme.WeightBody,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(row);
        }

        return UIFactory.Card(stack, AppTheme.CardRadius);
    }

    private static UIElement BuildMetricsGrid(ProjectBriefCardState state)
    {
        var host = new UniformGrid
        {
            Columns = Math.Min(4, Math.Max(2, state.Metrics.Count)),
            Margin = new Thickness(0, AppTheme.SpaceLG, 0, 0)
        };

        foreach (var metric in state.Metrics.Take(4))
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(0, 0, AppTheme.SpaceMD, 0)
            };
            stack.Children.Add(new TextBlock
            {
                Text = metric.Label,
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontSmall,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = metric.Value,
                Foreground = AppTheme.TextPrimary,
                FontSize = AppTheme.FontBody,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            host.Children.Add(UIFactory.Card(stack, AppTheme.CardRadius));
        }

        return host;
    }

    private static SolidColorBrush ResolveAccent(string? accentKind)
    {
        switch ((accentKind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "success":
                return AppTheme.Success;
            case "warning":
                return AppTheme.Warning;
            case "danger":
            case "error":
                return AppTheme.Danger;
            case "info":
                return AppTheme.Info;
            case "violet":
                return AppTheme.AccentAlt;
            default:
                return AppTheme.AccentBlue;
        }
    }
}

internal sealed class ProjectBriefCardState
{
    internal string Title { get; set; } = string.Empty;

    internal string Subtitle { get; set; } = string.Empty;

    internal string Summary { get; set; } = string.Empty;

    internal string ReadinessLabel { get; set; } = string.Empty;

    internal int ReadinessScore { get; set; }

    internal string ReadinessSummary { get; set; } = string.Empty;

    internal string ReadinessAccentKind { get; set; } = "info";

    internal List<ProjectBriefBadge> Badges { get; } = new List<ProjectBriefBadge>();

    internal List<string> Highlights { get; } = new List<string>();

    internal List<string> AttentionItems { get; } = new List<string>();

    internal List<ProjectBriefMetric> Metrics { get; } = new List<ProjectBriefMetric>();

    internal List<ProjectBriefAction> Actions { get; } = new List<ProjectBriefAction>();
}

internal sealed class ProjectBriefBadge
{
    internal string Label { get; set; } = string.Empty;

    internal string AccentKind { get; set; } = "info";
}

internal sealed class ProjectBriefMetric
{
    internal string Label { get; set; } = string.Empty;

    internal string Value { get; set; } = string.Empty;
}

internal sealed class ProjectBriefAction
{
    internal string ActionKey { get; set; } = string.Empty;

    internal string Label { get; set; } = string.Empty;

    internal string AccentKind { get; set; } = "info";
}
