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

internal sealed class ActivityTab : UserControl
{
    private readonly Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> _onInspect;
    private readonly StackPanel _panel;
    private readonly TextBlock _summary;
    private readonly WrapPanel _stats;
    private DispatcherTimer? _timer;

    internal ActivityTab(Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> onInspect)
    {
        _onInspect = onInspect;
        Background = AppTheme.PageBackground;

        var root = UIFactory.TabRoot();
        root.Children.Add(UIFactory.PageHeader("Activity", "Session timeline, queue, runs, diagnostics nhe."));
        _summary = UIFactory.Caption("Journal dang cho tool calls.");
        root.Children.Add(_summary);
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

        var allEntries = runtime.Journal.GetRecent();
        var entries = allEntries
            .Skip(Math.Max(0, allEntries.Count - 30))
            .Reverse()
            .ToList();
        var successes = entries.Count(x => x.Succeeded);
        var failures = entries.Count(x => !x.Succeeded);
        var changed = entries.Sum(x => x.ChangedIds.Count);

        _stats.Children.Add(CreateStatPill("Recent", entries.Count, AppTheme.AccentBlue));
        _stats.Children.Add(CreateStatPill("Success", successes, AppTheme.Success));
        _stats.Children.Add(CreateStatPill("Failure", failures, failures > 0 ? AppTheme.Danger : AppTheme.TextSecondary));
        _stats.Children.Add(CreateStatPill("Changed", changed, AppTheme.Warning));

        _summary.Text = entries.Count == 0 ? "Chua co activity." : $"{entries.Count} operations gan nhat.";
        if (entries.Count == 0)
        {
            _panel.Children.Add(UIFactory.Card(new TextBlock
            {
                Text = "Khi worker hoac command palette chay tool, operation journal se hien o day.",
                Foreground = AppTheme.TextMuted,
                TextWrapping = TextWrapping.Wrap
            }, AppTheme.CardRadius));
            return;
        }

        foreach (var entry in entries)
        {
            var card = new ActivityRunCard(entry);
            card.Selected += ShowDetails;
            _panel.Children.Add(card);
        }
    }

    private void ShowDetails(OperationJournalEntry entry)
    {
        _onInspect(
            entry.ToolName,
            entry.StatusCode,
            string.IsNullOrWhiteSpace(entry.DiagnosticsSummary) ? entry.ResultSummary : entry.DiagnosticsSummary,
            new[]
            {
                new KeyValuePair<string, string>("Started", entry.StartedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Caller", entry.Caller),
                new KeyValuePair<string, string>("Changed", entry.ChangedIds.Count.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Correlation", entry.CorrelationId)
            });
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
