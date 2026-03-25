using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BIM765T.Revit.Agent.UI.Theme;

/// <summary>
/// Shared UI element factory — tạo các element chuẩn hóa style.
/// Thay thế CreateCard(), CreateSectionHeader(), Spacer() duplicate ở nhiều tab.
/// </summary>
internal static class UIFactory
{
    // ── Text Elements ──

    /// <summary>Title text — 16px SemiBold, primary color.</summary>
    internal static TextBlock Title(string text) => new TextBlock
    {
        Text = text,
        Foreground = AppTheme.TextPrimary,
        FontSize = AppTheme.FontTitle,
        FontWeight = FontWeights.SemiBold
    };

    /// <summary>Section header — 15px SemiBold, primary color.</summary>
    internal static TextBlock SectionTitle(string text) => new TextBlock
    {
        Text = text,
        Foreground = AppTheme.TextPrimary,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold
    };

    /// <summary>Page title — 17px SemiBold, primary color.</summary>
    internal static TextBlock PageTitle(string text) => new TextBlock
    {
        Text = text,
        Foreground = AppTheme.TextPrimary,
        FontSize = 17,
        FontWeight = FontWeights.SemiBold
    };

    /// <summary>Body text — 13px Medium, primary color.</summary>
    internal static TextBlock Body(string text) => new TextBlock
    {
        Text = text,
        Foreground = AppTheme.TextPrimary,
        FontSize = AppTheme.FontBody,
        FontWeight = AppTheme.WeightBody,
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>Secondary/hint text — 12px Medium, muted color.</summary>
    internal static TextBlock Secondary(string text) => new TextBlock
    {
        Text = text,
        Foreground = AppTheme.TextMuted,
        FontSize = AppTheme.FontSecondary,
        FontWeight = AppTheme.WeightBody,
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>Caption text — 11px Medium, muted color.</summary>
    internal static TextBlock Caption(string text) => new TextBlock
    {
        Text = text,
        Foreground = AppTheme.TextMuted,
        FontSize = AppTheme.FontCaption,
        FontWeight = AppTheme.WeightBody,
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>Monospace text — Consolas, muted color.</summary>
    internal static TextBlock Mono(string text, double fontSize = 10) => new TextBlock
    {
        Text = text,
        Foreground = AppTheme.TextMuted,
        FontSize = fontSize,
        FontFamily = AppTheme.FontMono,
        TextWrapping = TextWrapping.Wrap
    };

    // ── Layout Elements ──

    /// <summary>Section block — title + subtitle, standard margins.</summary>
    internal static UIElement SectionBlock(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, AppTheme.SpaceSM) };
        stack.Children.Add(SectionTitle(title));
        var sub = Secondary(subtitle);
        sub.Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0);
        stack.Children.Add(sub);
        return stack;
    }

    /// <summary>Page header — page title + subtitle, standard margins.</summary>
    internal static UIElement PageHeader(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM) };
        stack.Children.Add(PageTitle(title));
        var sub = Secondary(subtitle);
        sub.Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0);
        stack.Children.Add(sub);
        return stack;
    }

    /// <summary>Card container — dark bg, border, corner radius.</summary>
    internal static Border Card(UIElement child, double radius = 0)
    {
        return new Border
        {
            Background = AppTheme.CardBackground,
            BorderBrush = AppTheme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius > 0 ? radius : AppTheme.CardRadiusLarge),
            Padding = new Thickness(AppTheme.SpaceMD),
            Child = child
        };
    }

    /// <summary>
    /// Elevated card — card with subtle drop shadow for visual hierarchy.
    /// Dùng cho hero cards, status cards, important sections.
    /// Lưu ý: DropShadowEffect dùng software rendering — chỉ dùng cho ≤5 cards/tab.
    /// </summary>
    internal static Border ElevatedCard(UIElement child, double radius = 0)
    {
        return new Border
        {
            Background = AppTheme.CardBackground,
            BorderBrush = AppTheme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius > 0 ? radius : AppTheme.CardRadiusLarge),
            Padding = new Thickness(AppTheme.SpaceMD),
            Child = child,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                ShadowDepth = 2,
                Opacity = 0.35,
                Direction = 270  // shadow drops down
            }
        };
    }

    /// <summary>Info card — title + body + accent underline.</summary>
    internal static Border InfoCard(string title, string body, SolidColorBrush accentBrush)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new Border
        {
            Width = 44,
            Height = 3,
            Background = accentBrush,
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceSM + 2),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = AppTheme.TextMuted,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        });
        return Card(stack);
    }

    /// <summary>Invisible spacer — for vertical spacing between elements.</summary>
    internal static Border Spacer(double height) => new Border { Height = height, Opacity = 0 };

    /// <summary>Thin horizontal divider line.</summary>
    internal static Border Divider() => new Border
    {
        Height = 1,
        Background = AppTheme.CardBorder,
        Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceSM)
    };

    /// <summary>Category label with accent bar — for grouping tools.</summary>
    internal static UIElement CategoryLabel(string label, SolidColorBrush accentColor)
    {
        var dock = new DockPanel { Margin = new Thickness(0, AppTheme.SpaceXS, 0, AppTheme.SpaceSM) };
        dock.Children.Add(new Border
        {
            Width = 4,
            Height = 18,
            Background = accentColor,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        });
        dock.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return dock;
    }

    /// <summary>Pre-configured dark ScrollViewer for tab content.</summary>
    internal static ScrollViewer ScrollContainer(UIElement content)
    {
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = AppTheme.PageBackground,
            Content = content
        };
    }

    /// <summary>Tab root StackPanel with standard margins.</summary>
    internal static StackPanel TabRoot() => new StackPanel
    {
        Margin = new Thickness(AppTheme.SpaceMD)
    };

    /// <summary>Icon visual using the premium vector icon set when available, otherwise glyph fallback.</summary>
    internal static FrameworkElement Icon(string mdl2Char, double size = 14, Brush? color = null)
    {
        return IconLibrary.Create(mdl2Char, size, color ?? AppTheme.TextPrimary);
    }

    /// <summary>Detail row — label: value, dùng trong ActivityTab detail panel.</summary>
    internal static UIElement DetailRow(string label, string value)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        dock.Children.Add(new TextBlock
        {
            Text = label + ": ",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            FontWeight = FontWeights.SemiBold,
            Width = 70
        });
        dock.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontCaption,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return dock;
    }

    /// <summary>Small colored badge — used for diagnostic counts, status.</summary>
    internal static Border Badge(string text, SolidColorBrush color)
    {
        return new Border
        {
            Background = color,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(2, 0, 0, 0),
            Child = new TextBlock
            {
                Text = text,
                Foreground = AppTheme.PageBackground,
                FontSize = AppTheme.FontSmall,
                FontWeight = FontWeights.Bold
            }
        };
    }

    // ══════════════════════════════════════════════════════════════
    // Rich UI components (BIMAI-level quality)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Rich card with colored left accent border + glow background.
    /// Giống style card của BIMAI: colored icon circle + title + body.
    /// </summary>
    internal static Border RichCard(string iconChar, string title, string body, SolidColorBrush accentColor, SolidColorBrush glowBg)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Colored icon circle (left)
        var iconCircle = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = glowBg,
            Margin = new Thickness(0, 0, AppTheme.SpaceMD, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = iconChar,
                FontFamily = AppTheme.FontIcon,
                FontSize = 16,
                Foreground = accentColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconCircle, 0);
        grid.Children.Add(iconCircle);

        // Title + body (right)
        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = accentColor,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceXS)
        });
        textStack.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontBody,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = AppTheme.LineHeightRelaxed
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        return new Border
        {
            Background = AppTheme.CardBackground,
            BorderBrush = glowBg,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Padding = new Thickness(AppTheme.SpaceLG),
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM),
            Child = grid
        };
    }

    /// <summary>
    /// Section header with colored icon circle + title + subtitle.
    /// Giống header sections của BIMAI.
    /// </summary>
    internal static UIElement SectionWithIcon(string iconChar, string title, string subtitle, SolidColorBrush accentColor, SolidColorBrush iconBg)
    {
        var dock = new DockPanel { Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceMD) };

        var iconCircle = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = iconBg,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = IconLibrary.Create(iconChar, 13, accentColor)
        };
        DockPanel.SetDock(iconCircle, Dock.Left);
        dock.Children.Add(iconCircle);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontCaption,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }
        dock.Children.Add(textStack);
        return dock;
    }

    /// <summary>
    /// Inline code badge — monospace text on dark bg with border.
    /// Giống `FilteredElementCollector` trong screenshots.
    /// </summary>
    internal static Border InlineCode(string code)
    {
        return new Border
        {
            Background = AppTheme.InlineCodeBg,
            BorderBrush = AppTheme.InlineCodeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = code,
                Foreground = AppTheme.InlineCodeText,
                FontFamily = AppTheme.FontMono,
                FontSize = AppTheme.FontInlineCode,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    /// <summary>
    /// Tag badge with colored border — giống tag chips "Wall_Module_TypeA".
    /// </summary>
    internal static Border TagBadge(string text, SolidColorBrush borderColor)
    {
        return new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = borderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.PillRadius),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, AppTheme.SpaceSM),
            Child = new TextBlock
            {
                Text = text,
                Foreground = borderColor,
                FontSize = AppTheme.FontSecondary,
                FontWeight = FontWeights.SemiBold
            }
        };
    }

    /// <summary>
    /// Terminal/execution log block — dark bg, monospace, colored prefix.
    /// Giống BIMAI_Execution_Log style.
    /// </summary>
    internal static Border TerminalBlock(UIElement content)
    {
        return new Border
        {
            Background = AppTheme.TerminalBg,
            BorderBrush = AppTheme.TerminalBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Padding = new Thickness(AppTheme.SpaceLG),
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM),
            Child = content
        };
    }

    /// <summary>Terminal log line — prefix colored, message monospace.</summary>
    internal static UIElement TerminalLine(string prefix, string message, SolidColorBrush prefixColor)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        dock.Children.Add(new TextBlock
        {
            Text = prefix + " ",
            Foreground = prefixColor,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top
        });
        dock.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = AppTheme.TerminalText,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontSecondary,
            TextWrapping = TextWrapping.Wrap
        });
        return dock;
    }

    /// <summary>Success line — checkmark + green text, like BIMAI "Successfully created...".</summary>
    internal static UIElement TerminalSuccess(string message)
    {
        return TerminalLine("\u2713", message, AppTheme.TerminalGreen);
    }

    /// <summary>Info line — bracket prefix like "[System] Parsing intent...".</summary>
    internal static UIElement TerminalInfo(string prefix, string message)
    {
        return TerminalLine(prefix, message, AppTheme.TerminalCyan);
    }

    /// <summary>Progress line — spinner + message + count.</summary>
    internal static UIElement TerminalProgress(string message, int current, int total)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        dock.Children.Add(new TextBlock
        {
            Text = "\u25CB ",
            Foreground = AppTheme.TerminalYellow,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontSecondary,
            VerticalAlignment = VerticalAlignment.Top
        });
        dock.Children.Add(new TextBlock
        {
            Text = $"{message}  [{current}/{total}]",
            Foreground = AppTheme.TerminalText,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontSecondary,
            TextWrapping = TextWrapping.Wrap
        });
        return dock;
    }

    /// <summary>
    /// Hero title — large text + subtitle for page headers.
    /// </summary>
    internal static UIElement HeroHeader(string title, string subtitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, AppTheme.SpaceMD) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontHero,
            FontWeight = FontWeights.Bold
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontBody,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = AppTheme.LineHeightRelaxed
        });
        return stack;
    }

    /// <summary>
    /// Collapsible chevron row for terminal-style expandable sections.
    /// </summary>
    internal static UIElement ChevronRow(string text, bool expanded = false)
    {
        var dock = new DockPanel { Margin = new Thickness(0, AppTheme.SpaceXS, 0, AppTheme.SpaceXS) };
        dock.Children.Add(new TextBlock
        {
            Text = expanded ? IconLibrary.ChevronDown : IconLibrary.ChevronRight,
            FontFamily = AppTheme.FontIcon,
            FontSize = 10,
            Foreground = AppTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        });
        dock.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return dock;
    }
}
