using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// ApprovalCard v3 — Terminal + AI Chat aesthetic.
///
/// Layout:
/// ┌ left accent bar ┌──────────────────────────────────────┐
/// │ (gradient)       │ ⚠ Pending approval       [Tier 1]  │
/// │                  │ Tool: `create_sheets`               │
/// │                  │ Summary text...                     │
/// │                  │ Confidence ▓▓▓▓▓▓▓░░░ 72%          │
/// │                  │ Expires in: 2:45                    │
/// │                  │ [✓ Đồng ý]  [✗ Từ chối]            │
/// └──────────────────┘──────────────────────────────────────┘
/// </summary>
internal sealed class ApprovalCard : Border
{
    private static readonly SolidColorBrush Tier0Color  = AppTheme.Success;
    private static readonly SolidColorBrush Tier1Color  = AppTheme.Accent;
    private static readonly SolidColorBrush Tier2Color  = AppTheme.Warning;
    private static readonly SolidColorBrush HighRiskColor = AppTheme.Danger;

    private static readonly SolidColorBrush Tier0Bg    = AppTheme.CardGlowGreen;
    private static readonly SolidColorBrush Tier1Bg    = AppTheme.AccentDim;
    private static readonly SolidColorBrush Tier2Bg    = AppTheme.CardGlowAmber;
    private static readonly SolidColorBrush HighRiskBg = AppTheme.Frozen("#4C0519");

    private readonly DispatcherTimer? _countdownTimer;
    private readonly DateTime? _expiresUtc;
    private readonly TextBlock? _countdownLabel;
    private bool _expired;

    internal ApprovalCard(PendingApprovalRef pending, Action onApprove, Action onReject)
    {
        var (tierColor, tierBg, tierLabel) = ResolveTier(pending.ExecutionTier);

        // Card styling — glass-like bg with left accent border
        Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceMD);
        CornerRadius = new CornerRadius(AppTheme.CardRadius);
        Background = AppTheme.ResponseCardBg;
        BorderBrush = AppTheme.ResponseCardBorder;
        BorderThickness = new Thickness(1);
        ClipToBounds = true;

        _expiresUtc = pending.ExpiresUtc;

        // Outer grid: accent bar + content
        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });  // accent bar
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left accent gradient bar
        var accentBar = new Border
        {
            Background = tierColor,
            CornerRadius = new CornerRadius(2, 0, 0, 2)
        };
        Grid.SetColumn(accentBar, 0);
        outerGrid.Children.Add(accentBar);

        // Content stack
        var stack = new StackPanel
        {
            Margin = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceMD, AppTheme.SpaceLG, AppTheme.SpaceMD)
        };

        // Row 1: Header + Tier badge
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "\u26A0",
            Foreground = tierColor,
            FontSize = AppTheme.FontBody,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Pending approval",
            Foreground = tierColor,
            FontSize = AppTheme.FontCaption,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(headerPanel, 0);
        headerRow.Children.Add(headerPanel);

        // Tier badge
        var tierBadge = new Border
        {
            Background = tierBg,
            BorderBrush = AppTheme.FrozenAlpha(tierColor.Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.40),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.BadgeRadius),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = tierLabel,
                Foreground = tierColor,
                FontSize = AppTheme.FontSmall,
                FontWeight = FontWeights.SemiBold,
                FontFamily = AppTheme.FontMono
            }
        };
        Grid.SetColumn(tierBadge, 1);
        headerRow.Children.Add(tierBadge);
        stack.Children.Add(headerRow);

        // Row 2: Tool name
        if (!string.IsNullOrWhiteSpace(pending.ToolName))
        {
            var toolRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
            };
            toolRow.Children.Add(new TextBlock
            {
                Text = "Tool: ",
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontSecondary,
                VerticalAlignment = VerticalAlignment.Center
            });
            toolRow.Children.Add(UIFactory.InlineCode(pending.ToolName));
            stack.Children.Add(toolRow);
        }

        // Row 3: Summary
        stack.Children.Add(new TextBlock
        {
            Text = pending.Summary ?? string.Empty,
            Foreground = AppTheme.TextPrimary,
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = AppTheme.FontBody,
            LineHeight = AppTheme.LineHeightRelaxed
        });

        // Row 4: Recovery hint
        if (!string.IsNullOrWhiteSpace(pending.RecoveryHint))
        {
            stack.Children.Add(new TextBlock
            {
                Text = pending.RecoveryHint,
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontSecondary,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        // Row 5: Confidence bar
        if (TryGetConfidence(pending, out var confidence) && confidence > 0)
        {
            stack.Children.Add(BuildConfidenceBar(confidence));
        }

        // Row 6: Countdown
        if (pending.ExpiresUtc.HasValue)
        {
            _countdownLabel = new TextBlock
            {
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontCaption,
                FontFamily = AppTheme.FontMono,
                Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
            };
            UpdateCountdownLabel();
            stack.Children.Add(_countdownLabel);

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, __) => UpdateCountdownLabel();
            _countdownTimer.Start();
            Unloaded += (_, __) => _countdownTimer.Stop();
        }

        // Row 7: Action buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, AppTheme.SpaceMD, 0, 0)
        };
        buttons.Children.Add(CreateActionBtn("\u2713", "\u0110\u1ED3ng \u00FD", AppTheme.Success, onApprove));
        buttons.Children.Add(CreateActionBtn("\u2717", "T\u1EEB ch\u1ED1i", AppTheme.Danger, onReject, isOutline: true));
        stack.Children.Add(buttons);

        Grid.SetColumn(stack, 1);
        outerGrid.Children.Add(stack);

        Child = outerGrid;
    }

    // ── Countdown ──
    private void UpdateCountdownLabel()
    {
        if (_countdownLabel == null || !_expiresUtc.HasValue || _expired) return;

        var remaining = _expiresUtc.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _countdownLabel.Text = "\u23F0 Expired";
            _countdownLabel.Foreground = AppTheme.Danger;
            _countdownTimer?.Stop();
            _expired = true;
            return;
        }

        _countdownLabel.Text = $"\u23F0 {(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
        _countdownLabel.Foreground = remaining.TotalSeconds < 60 ? AppTheme.Danger : AppTheme.TextMuted;
    }

    // ── Confidence Bar ──
    private static UIElement BuildConfidenceBar(double confidence)
    {
        var clamped = Math.Max(0.0, Math.Min(1.0, confidence));
        var container = new StackPanel { Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0) };

        var labelRow = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        var pct = new TextBlock
        {
            Text = $"{(int)(clamped * 100)}%",
            Foreground = AppTheme.Accent,
            FontSize = AppTheme.FontCaption,
            FontWeight = FontWeights.SemiBold,
            FontFamily = AppTheme.FontMono,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(pct, Dock.Right);
        labelRow.Children.Add(pct);
        labelRow.Children.Add(new TextBlock
        {
            Text = "Confidence",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption
        });
        container.Children.Add(labelRow);

        // Track
        var fillColor = clamped >= 0.70 ? AppTheme.Success : clamped >= 0.40 ? AppTheme.Warning : AppTheme.Danger;
        var fillGrid = new Grid();
        fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clamped, GridUnitType.Star) });
        fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0 - clamped, GridUnitType.Star) });

        var track = new Border
        {
            Height = 4,
            Background = AppTheme.SurfaceElevated,
            CornerRadius = new CornerRadius(2)
        };

        var fill = new Border
        {
            Background = fillColor,
            CornerRadius = new CornerRadius(2),
            Height = 4,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ((SolidColorBrush)fillColor).Color,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.40
            }
        };
        Grid.SetColumn(fill, 0);
        fillGrid.Children.Add(fill);
        track.Child = fillGrid;
        container.Children.Add(track);

        return container;
    }

    // ── Tier Resolution ──
    private static (SolidColorBrush color, SolidColorBrush bg, string label) ResolveTier(string executionTier)
    {
        if (string.IsNullOrWhiteSpace(executionTier))
            return (Tier0Color, Tier0Bg, "Safe");

        var t = executionTier.Trim().ToLowerInvariant();
        if (t.Contains("tier0") || t.Contains("read"))  return (Tier0Color, Tier0Bg, "Safe");
        if (t.Contains("tier1") || t.Contains("low"))   return (Tier1Color, Tier1Bg, "Low Risk");
        if (t.Contains("tier2") || t.Contains("destructive")) return (Tier2Color, Tier2Bg, "Destructive");
        if (t.Contains("high")) return (HighRiskColor, HighRiskBg, "High Risk");
        return (Tier0Color, Tier0Bg, "Safe");
    }

    private static bool TryGetConfidence(PendingApprovalRef pending, out double confidence)
    {
        confidence = 0;
        return false;
    }

    // ── Modern Action Button ──
    private static Border CreateActionBtn(string icon, string label, SolidColorBrush color, Action action, bool isOutline = false)
    {
        var border = new Border
        {
            Background = isOutline ? Brushes.Transparent : AppTheme.FrozenAlpha(color.Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.15),
            BorderBrush = AppTheme.FrozenAlpha(color.Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.40),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.ButtonRadius),
            Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceSM, AppTheme.SpaceLG, AppTheme.SpaceSM),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            Cursor = Cursors.Hand
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = icon + " ",
            Foreground = color,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = color,
            FontSize = AppTheme.FontSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        border.Child = content;

        border.MouseEnter += (_, _) =>
        {
            border.Background = AppTheme.FrozenAlpha(color.Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.25);
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background = isOutline ? Brushes.Transparent : AppTheme.FrozenAlpha(color.Color.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.15);
        };
        border.MouseLeftButtonUp += (_, _) => action();

        return border;
    }
}
