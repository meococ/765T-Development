using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Terminal-style execution log card — matches BIMAI_Execution_Log reference.
///
/// Layout:
/// ┌─ > ToolName ─────────────────────── ••• ┐
/// │ [System] Parsing intent...               │
/// │   [Revit API] Locating Titleblock...     │
/// │   ○ Creating sheets...  [24/50]          │
/// │ ✓ Successfully created 50 sheets in 2.4s │
/// │ >_                                       │
/// └──────────────────────────────────────────┘
/// </summary>
internal sealed class ToolResultCard : Border
{
    private readonly StackPanel _logLines;
    private bool _isExpanded = true;

    internal ToolResultCard(WorkerToolCard card)
    {
        Background = AppTheme.TerminalBg;
        BorderBrush = AppTheme.TerminalBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadius);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD);
        ClipToBounds = true;

        var container = new DockPanel();

        // ── Header bar ──
        var header = BuildHeader(card);
        DockPanel.SetDock(header, Dock.Top);
        container.Children.Add(header);

        // ── Log content area ──
        _logLines = new StackPanel
        {
            Margin = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceMD, AppTheme.SpaceLG, AppTheme.SpaceLG + 2)
        };

        // Parse and render log lines
        RenderLogLines(card);

        container.Children.Add(_logLines);
        Child = container;

        MouseEnter += (_, _) =>
        {
            BorderBrush = AppTheme.FrozenAlpha((card.Succeeded ? AppTheme.TerminalGreen : AppTheme.TerminalYellow).Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.35);
        };
        MouseLeave += (_, _) =>
        {
            BorderBrush = AppTheme.TerminalBorder;
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Header — collapsible title bar with dots
    // ══════════════════════════════════════════════════════════════
    private UIElement BuildHeader(WorkerToolCard card)
    {
        var header = new Border
        {
            Background = AppTheme.TerminalHeaderBg,
            BorderBrush = AppTheme.TerminalBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceMD, AppTheme.SpaceLG, AppTheme.SpaceMD),
            Cursor = Cursors.Hand
        };

        var dock = new DockPanel();

        // Right side: traffic light dots + status badge
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        dots.Children.Add(new Border
        {
            Background = card.Succeeded ? AppTheme.CardGlowGreen : AppTheme.CardGlowAmber,
            BorderBrush = AppTheme.FrozenAlpha((card.Succeeded ? AppTheme.TerminalGreen : AppTheme.TerminalYellow).Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.25),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.BadgeRadius),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            Child = new TextBlock
            {
                Text = card.Succeeded ? "DONE" : "REVIEW",
                Foreground = card.Succeeded ? AppTheme.TerminalGreen : AppTheme.TerminalYellow,
                FontFamily = AppTheme.FontMono,
                FontSize = AppTheme.FontSmall,
                FontWeight = FontWeights.SemiBold
            }
        });
        dots.Children.Add(CreateDot("#EF4444"));  // red
        dots.Children.Add(CreateDot("#F59E0B"));  // amber
        dots.Children.Add(CreateDot("#22C55E"));  // green
        DockPanel.SetDock(dots, Dock.Right);
        dock.Children.Add(dots);

        // Left side: chevron + title
        var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Chevron
        var chevron = new TextBlock
        {
            Text = "\u276F",
            Foreground = AppTheme.TerminalDimText,
            FontSize = AppTheme.FontSmall,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        };
        leftPanel.Children.Add(chevron);

        // Terminal icon
        leftPanel.Children.Add(new TextBlock
        {
            Text = "\u2583",
            Foreground = AppTheme.TerminalDimText,
            FontSize = AppTheme.FontSmall,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        });

        // Tool name
        leftPanel.Children.Add(new TextBlock
        {
            Text = FormatToolName(card.ToolName),
            Foreground = AppTheme.TextSecondary,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontTerminalLog,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Execution tier badge
        if (!string.IsNullOrWhiteSpace(card.ExecutionTier))
        {
            leftPanel.Children.Add(new Border
            {
                Background = GetTierBg(card.ExecutionTier),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = card.ExecutionTier,
                    Foreground = GetTierFg(card.ExecutionTier),
                    FontSize = AppTheme.FontSmall,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = AppTheme.FontMono
                }
            });
        }

        dock.Children.Add(leftPanel);
        header.Child = dock;

        // Toggle collapse
        header.MouseLeftButtonDown += (_, _) =>
        {
            _isExpanded = !_isExpanded;
            _logLines.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = _isExpanded ? "\u276F" : "\u276F";
            chevron.RenderTransform = _isExpanded
                ? new RotateTransform(90, 4, 5)
                : new RotateTransform(0, 4, 5);
        };

        return header;
    }

    // ══════════════════════════════════════════════════════════════
    //  Log Lines — colored terminal output
    // ══════════════════════════════════════════════════════════════
    private void RenderLogLines(WorkerToolCard card)
    {
        // Stage info
        if (!string.IsNullOrWhiteSpace(card.Stage))
        {
            _logLines.Children.Add(LogLine("[System]", $"Stage: {card.Stage}", AppTheme.TerminalDimText));
        }

        // WhyThisTool — reasoning
        if (!string.IsNullOrWhiteSpace(card.WhyThisTool))
        {
            _logLines.Children.Add(LogLine("[System]", card.WhyThisTool, AppTheme.TerminalDimText));
        }

        // Revit API call
        _logLines.Children.Add(LogLine("[Revit API]", $"Executing {card.ToolName}...", AppTheme.TerminalCyan));

        // Progress bar (if progress > 0)
        if (card.Progress > 0 && card.Progress < 1.0)
        {
            var percent = (int)(card.Progress * 100);
            _logLines.Children.Add(BuildProgressLine($"Processing... [{percent}%]", card.Progress));
        }

        // Status line — success or failure
        if (card.Succeeded)
        {
            _logLines.Children.Add(LogLine("\u2713", $"{card.Summary}", AppTheme.TerminalGreen));
        }
        else
        {
            _logLines.Children.Add(LogLine("\u2717", $"{card.Summary}", AppTheme.TerminalRed));

            // Recovery hints
            foreach (var hint in card.RecoveryHints)
            {
                _logLines.Children.Add(LogLine("  \u21B3", hint, AppTheme.TerminalYellow));
            }
        }

        // Confidence
        if (card.Confidence > 0)
        {
            var confColor = card.Confidence >= 0.7 ? AppTheme.TerminalGreen
                : card.Confidence >= 0.4 ? AppTheme.TerminalYellow
                : AppTheme.TerminalRed;
            _logLines.Children.Add(LogLine("[Confidence]", $"{card.Confidence:P0}", confColor));
        }

        // Artifacts
        if (card.ArtifactRefs.Count > 0)
        {
            foreach (var artifact in card.ArtifactRefs)
            {
                _logLines.Children.Add(LogLine("  \u2192", artifact, AppTheme.AccentBlue));
            }
        }

        // Bottom cursor prompt
        _logLines.Children.Add(new TextBlock
        {
            Text = ">_",
            Foreground = AppTheme.TerminalDimText,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontTerminalLog,
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
        });
    }

    // ──────────────────────────────────────────────────────────────
    //  Single log line — prefix + message
    // ──────────────────────────────────────────────────────────────
    private static UIElement LogLine(string prefix, string message, SolidColorBrush prefixColor)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };

        dock.Children.Add(new TextBlock
        {
            Text = prefix + " ",
            Foreground = prefixColor,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontTerminalLog,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top
        });

        dock.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = AppTheme.TerminalText,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontTerminalLog,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22
        });

        return dock;
    }

    // ──────────────────────────────────────────────────────────────
    //  Progress line with visual bar
    // ──────────────────────────────────────────────────────────────
    private static UIElement BuildProgressLine(string label, double progress)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

        // Label
        var dock = new DockPanel();
        dock.Children.Add(new TextBlock
        {
            Text = "\u25CB ",
            Foreground = AppTheme.TerminalYellow,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontTerminalLog,
            VerticalAlignment = VerticalAlignment.Center
        });
        dock.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = AppTheme.TerminalText,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontTerminalLog
        });
        stack.Children.Add(dock);

        // Progress bar
        var barBg = new Border
        {
            Height = 3,
            Background = AppTheme.SurfaceElevated,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(AppTheme.SpaceLG, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 240
        };

        var barFill = new Border
        {
            Height = 3,
            Background = AppTheme.TerminalCyan,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = progress * 240
        };

        var barGrid = new Grid();
        barGrid.Children.Add(barBg);
        barGrid.Children.Add(barFill);
        stack.Children.Add(barGrid);

        return stack;
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────
    private static Ellipse CreateDot(string hex)
    {
        return new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = AppTheme.Frozen(hex),
            Margin = new Thickness(0, 0, 6, 0)
        };
    }

    private static string FormatToolName(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "BIMAI_Execution_Log";
        // Convert dot notation to display name
        return toolName.Replace(".", "_");
    }

    private static SolidColorBrush GetTierBg(string tier)
    {
        return tier switch
        {
            "Tier0" or "Safe" => AppTheme.CardGlowGreen,
            "Tier1" or "Low" => AppTheme.CardGlowCyan,
            "Tier2" or "Destructive" => AppTheme.CardGlowAmber,
            _ => AppTheme.CardGlowViolet
        };
    }

    private static SolidColorBrush GetTierFg(string tier)
    {
        return tier switch
        {
            "Tier0" or "Safe" => AppTheme.TerminalGreen,
            "Tier1" or "Low" => AppTheme.TerminalCyan,
            "Tier2" or "Destructive" => AppTheme.TerminalYellow,
            _ => AppTheme.AccentAlt
        };
    }
}
