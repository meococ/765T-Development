using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Agent.UI.Components;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Tabs;

internal sealed class EvidenceTab : UserControl
{
    private readonly Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> _onInspect;
    private readonly StackPanel _panel;
    private readonly TextBlock _header;
    private readonly WrapPanel _stats;
    private DispatcherTimer? _timer;

    internal EvidenceTab(Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> onInspect)
    {
        _onInspect = onInspect;
        Background = AppTheme.PageBackground;

        var root = UIFactory.TabRoot();
        root.Children.Add(UIFactory.PageHeader("Evidence", "Artifacts, diff summary, changed elements, verify result."));
        _header = UIFactory.Caption("Evidence bundle se hien o day sau moi workflow/preview/verify run.");
        root.Children.Add(_header);
        _stats = new WrapPanel { Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceSM) };
        root.Children.Add(_stats);

        _panel = new StackPanel();
        root.Children.Add(_panel);

        Content = UIFactory.ScrollContainer(root);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += (_, _) => Refresh();
        }
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
    }

    private void Refresh()
    {
        _panel.Children.Clear();
        _stats.Children.Clear();

        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null)
        {
            _panel.Children.Add(UIFactory.Card(new TextBlock
            {
                Text = "Runtime chua san sang.",
                Foreground = AppTheme.TextMuted
            }, AppTheme.CardRadius));
            return;
        }

        var runs = runtime.WorkflowRuntime.ListRuns().Take(20).ToList();
        var completed = runs.Count(x => string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase));
        var totalArtifacts = runs.Sum(x => x.Evidence.ArtifactKeys.Count);
        var totalChanged = runs.Sum(x => x.ChangedIds.Count);

        _stats.Children.Add(CreateStatPill("Runs", runs.Count, AppTheme.AccentBlue));
        _stats.Children.Add(CreateStatPill("Completed", completed, AppTheme.Success));
        _stats.Children.Add(CreateStatPill("Artifacts", totalArtifacts, AppTheme.Info));
        _stats.Children.Add(CreateStatPill("Changed", totalChanged, AppTheme.Warning));

        _header.Text = runs.Count == 0 ? "Chua co run nao." : $"{runs.Count} workflow runs gan nhat.";
        if (runs.Count == 0)
        {
            _panel.Children.Add(UIFactory.Card(new TextBlock
            {
                Text = "Khi worker tao preview/apply/verify, evidence se gom ve day de anh zoom, inspect va export nhanh.",
                Foreground = AppTheme.TextMuted,
                TextWrapping = TextWrapping.Wrap
            }, AppTheme.CardRadius));
            return;
        }

        foreach (var run in runs)
        {
            _panel.Children.Add(CreateRunCard(run));
        }
    }

    private Border CreateRunCard(WorkflowRun run)
    {
        var accent = string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase) ? AppTheme.Success : AppTheme.Warning;
        var stack = new StackPanel();
        var header = new DockPanel();
        header.Children.Add(new TextBlock
        {
            Text = run.WorkflowName,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold
        });
        var badge = new StatusBadge(run.Status.ToUpperInvariant(), accent);
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        stack.Children.Add(header);

        var meta = new WrapPanel { Margin = new Thickness(0, AppTheme.SpaceXS, 0, AppTheme.SpaceSM) };
        meta.Children.Add(new StatusBadge($"ARTIFACTS {run.Evidence.ArtifactKeys.Count}", AppTheme.Info));
        meta.Children.Add(new StatusBadge($"CHANGED {run.ChangedIds.Count}", AppTheme.Warning));
        meta.Children.Add(new StatusBadge($"CHECKPOINTS {run.Checkpoints.Count}", AppTheme.AccentBlue));
        stack.Children.Add(meta);

        if (run.Checkpoints.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = string.Join("  →  ", run.Checkpoints.Take(5).Select(x => $"{x.StepName}:{x.Status}")),
                Foreground = AppTheme.TextSecondary,
                FontFamily = AppTheme.FontMono,
                FontSize = AppTheme.FontCaption,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var card = UIFactory.Card(stack, AppTheme.CardRadius);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) =>
        {
            _onInspect(
                run.WorkflowName,
                run.Status,
                "Workflow evidence bundle.",
                new[]
                {
                    new KeyValuePair<string, string>("RunId", run.RunId),
                    new KeyValuePair<string, string>("Preview", run.PreviewRunId),
                    new KeyValuePair<string, string>("Mutation", run.MutationToolName),
                    new KeyValuePair<string, string>("Artifacts", run.Evidence.ArtifactKeys.Count.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>("Changed", run.ChangedIds.Count.ToString(CultureInfo.InvariantCulture))
                });
        };
        return card;
    }

    private static Border CreateStatPill(string label, int value, System.Windows.Media.SolidColorBrush accent)
    {
        return new Border
        {
            Background = AppTheme.PillBackground,
            BorderBrush = AppTheme.FrozenAlpha(accent.Color.ToString(CultureInfo.InvariantCulture), 0.24),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.BadgeRadius),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, AppTheme.SpaceXS),
            Padding = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = $"{label} {value.ToString(CultureInfo.InvariantCulture)}",
                Foreground = accent,
                FontSize = AppTheme.FontCaption,
                FontWeight = FontWeights.SemiBold
            }
        };
    }
}
