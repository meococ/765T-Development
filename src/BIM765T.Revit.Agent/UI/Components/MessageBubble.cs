using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Modern chat message bubble — Terminal + AI Chat aesthetic.
///
/// User messages: right-aligned with "You" avatar + context tags.
/// Worker messages: left-aligned with gradient Bot avatar + parsed markdown:
///   - Text paragraphs, **bold**, `inline code`, code blocks
///   - Structured response cards (icon + title + description)
///   - Action bar (Chạy Script, Copy Code)
/// System messages: subtle elevated surface.
/// </summary>
internal sealed class MessageBubble : Border
{
    private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex InlineCodeRegex = new Regex(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex CodeBlockRegex = new Regex(@"```(\w+)?\r?\n([\s\S]*?)```", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new Regex(@"^#{1,3}\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new Regex(@"^[-*]\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex NumberedRegex = new Regex(@"^(\d+)\.\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex InlineFormattingRegex = new Regex(@"(\*\*(.+?)\*\*)|(`([^`]+)`)", RegexOptions.Compiled);

    /// <summary>Event raised when user clicks "Copy Code" — parent can wire clipboard.</summary>
#pragma warning disable CS0067 // Event is declared but never used — wired by parent tab at runtime
    internal event Action<string>? CopyCodeRequested;
#pragma warning restore CS0067

    /// <summary>Event raised when user clicks "Run Script in Revit".</summary>
    internal event Action<string>? RunScriptRequested;

    internal MessageBubble(WorkerChatMessage message)
    {
        var isUser = string.Equals(message.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase);
        var isSystem = string.Equals(message.Role, WorkerMessageRoles.System, StringComparison.OrdinalIgnoreCase);

        Margin = new Thickness(0, 0, 0, AppTheme.SpaceLG);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);

        if (isSystem)
        {
            Child = BuildSystemMessage(message);
        }
        else if (isUser)
        {
            Child = BuildUserMessage(message);
        }
        else
        {
            Child = BuildWorkerMessage(message);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  System Message — subtle, centered
    // ══════════════════════════════════════════════════════════════
    private static UIElement BuildSystemMessage(WorkerChatMessage message)
    {
        var border = new Border
        {
            Background = AppTheme.SurfaceElevated,
            BorderBrush = AppTheme.SubtleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceSM, AppTheme.SpaceLG, AppTheme.SpaceSM),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 600
        };

        var text = new TextBlock
        {
            Text = message.Content ?? string.Empty,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSecondary,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            FontStyle = FontStyles.Italic
        };

        border.Child = text;
        return border;
    }

    // ══════════════════════════════════════════════════════════════
    //  User Message — avatar + text + optional tags
    // ══════════════════════════════════════════════════════════════
    private static UIElement BuildUserMessage(WorkerChatMessage message)
    {
        var row = new DockPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = 600
        };

        // Message content on the left
        var contentStack = new StackPanel { Margin = new Thickness(0, 0, AppTheme.SpaceMD, 0) };

        // Timestamp
        contentStack.Children.Add(new TextBlock
        {
            Text = $"{message.TimestampUtc:HH:mm}",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSmall,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceXS)
        });

        // Message text in a styled bubble
        var messageBorder = new Border
        {
            Background = AppTheme.SelectedBg,
            BorderBrush = AppTheme.FocusRing,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius, AppTheme.CardRadius, 2, AppTheme.CardRadius),
            Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM, AppTheme.SpaceMD, AppTheme.SpaceSM)
        };

        messageBorder.Child = new TextBlock
        {
            Text = message.Content ?? string.Empty,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = AppTheme.LineHeightRelaxed
        };

        contentStack.Children.Add(messageBorder);

        DockPanel.SetDock(contentStack, Dock.Left);
        row.Children.Add(contentStack);

        // Avatar on the right
        var avatar = new Border
        {
            Width = AppTheme.AvatarSize,
            Height = AppTheme.AvatarSize,
            CornerRadius = new CornerRadius(8),
            Background = AppTheme.UserAvatarBg,
            BorderBrush = AppTheme.SubtleBorder,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "Anh",
                Foreground = AppTheme.TextSecondary,
                FontSize = AppTheme.FontSmall,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        DockPanel.SetDock(avatar, Dock.Right);
        row.Children.Add(avatar);

        return row;
    }

    // ══════════════════════════════════════════════════════════════
    //  Worker / AI Message — avatar + parsed markdown + cards + actions
    // ══════════════════════════════════════════════════════════════
    private UIElement BuildWorkerMessage(WorkerChatMessage message)
    {
        var isStreaming = string.Equals(message.StatusCode, "STREAMING", StringComparison.OrdinalIgnoreCase);
        var row = new DockPanel { MaxWidth = 720 };

        // Bot avatar on the left
        var avatar = CreateBotAvatar();
        DockPanel.SetDock(avatar, Dock.Left);
        row.Children.Add(avatar);

        // Content area
        var contentHost = new Border
        {
            Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0),
            Background = isStreaming ? AppTheme.SurfaceMuted : AppTheme.ResponseCardBg,
            BorderBrush = isStreaming ? AppTheme.FocusRing : AppTheme.ResponseCardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadiusLarge),
            Padding = new Thickness(AppTheme.SpaceLG + 2, AppTheme.SpaceMD, AppTheme.SpaceLG + 2, AppTheme.SpaceMD)
        };
        var contentStack = new StackPanel();

        // Label + timestamp
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "765T Worker",
            Foreground = AppTheme.AccentBlue,
            FontSize = AppTheme.FontSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $" \u2022 {message.TimestampUtc:HH:mm}",
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSmall,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (isStreaming)
        {
            headerPanel.Children.Add(new Border
            {
                Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0),
                Child = new StatusBadge("DANG TRA LOI", AppTheme.AccentBlue)
            });
        }
        contentStack.Children.Add(headerPanel);

        // Parse and render content
        var content = message.Content ?? string.Empty;
        var parsedElements = ParseMarkdown(content);
        foreach (var element in parsedElements)
        {
            contentStack.Children.Add(element);
        }

        contentHost.Child = contentStack;
        row.Children.Add(contentHost);
        return row;
    }

    // ──────────────────────────────────────────────────────────────
    //  Bot Avatar — gradient circle with icon
    // ──────────────────────────────────────────────────────────────
    private static UIElement CreateBotAvatar()
    {
        var avatar = new Border
        {
            Width = AppTheme.AvatarSize,
            Height = AppTheme.AvatarSize,
            CornerRadius = new CornerRadius(8),
            Background = AppTheme.BotAvatarBg,
            BorderBrush = AppTheme.BotAvatarBorder,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Effect = AppTheme.GlowBlue,
            Child = IconLibrary.Create(IconLibrary.WorkerChat, 14, AppTheme.AccentBlue)
        };
        return avatar;
    }

    // ══════════════════════════════════════════════════════════════
    //  Markdown Parser — returns list of UI elements
    // ══════════════════════════════════════════════════════════════
    private static List<UIElement> ParseMarkdown(string content)
    {
        var elements = new List<UIElement>();
        if (string.IsNullOrWhiteSpace(content)) return elements;

        // Split by code blocks first
        var codeBlockMatches = CodeBlockRegex.Matches(content);
        int lastIndex = 0;

        foreach (Match match in codeBlockMatches)
        {
            // Text before code block
            if (match.Index > lastIndex)
            {
                var textBefore = content.Substring(lastIndex, match.Index - lastIndex);
                elements.AddRange(ParseTextBlock(textBefore));
            }

            // Code block
            var language = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(language)) language = "python";
            var code = match.Groups[2].Value.Trim();
            elements.Add(BuildCodeBlock(language, code));

            lastIndex = match.Index + match.Length;
        }

        // Remaining text after last code block
        if (lastIndex < content.Length)
        {
            var remaining = content.Substring(lastIndex);
            elements.AddRange(ParseTextBlock(remaining));
        }

        return elements;
    }

    /// <summary>Parse non-code text into paragraphs, headings, bullets, etc.</summary>
    private static List<UIElement> ParseTextBlock(string text)
    {
        var elements = new List<UIElement>();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Heading
            var headingMatch = HeadingRegex.Match(trimmed);
            if (headingMatch.Success)
            {
                var level = trimmed.IndexOf(' ');
                elements.Add(new TextBlock
                {
                    Text = headingMatch.Groups[1].Value,
                    Foreground = AppTheme.TextPrimary,
                    FontSize = level <= 2 ? AppTheme.FontSection : AppTheme.FontBody,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, AppTheme.SpaceMD, 0, AppTheme.SpaceXS),
                    TextWrapping = TextWrapping.Wrap
                });
                continue;
            }

            // Bullet
            var bulletMatch = BulletRegex.Match(trimmed);
            if (bulletMatch.Success)
            {
                elements.Add(BuildBulletLine(bulletMatch.Groups[1].Value));
                continue;
            }

            // Numbered list
            var numberedMatch = NumberedRegex.Match(trimmed);
            if (numberedMatch.Success)
            {
                elements.Add(BuildNumberedLine(numberedMatch.Groups[1].Value, numberedMatch.Groups[2].Value));
                continue;
            }

            // Regular paragraph with inline formatting
            elements.Add(BuildFormattedTextBlock(trimmed));
        }

        return elements;
    }

    // ──────────────────────────────────────────────────────────────
    //  Inline Formatted Text — **bold** and `code`
    // ──────────────────────────────────────────────────────────────
    private static TextBlock BuildFormattedTextBlock(string text)
    {
        var tb = new TextBlock
        {
            Foreground = AppTheme.TextSecondary,
            FontSize = AppTheme.FontBody,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = AppTheme.LineHeightRelaxed,
            Margin = new Thickness(0, 2, 0, 2)
        };

        // Parse inline formatting: **bold** and `code`
        int pos = 0;
        var matches = InlineFormattingRegex.Matches(text);

        if (matches.Count == 0)
        {
            tb.Text = text;
            return tb;
        }

        foreach (Match m in matches)
        {
            // Text before match
            if (m.Index > pos)
            {
                tb.Inlines.Add(new System.Windows.Documents.Run(text.Substring(pos, m.Index - pos)));
            }

            if (m.Groups[2].Success)
            {
                // **bold**
                tb.Inlines.Add(new System.Windows.Documents.Run(m.Groups[2].Value)
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = AppTheme.TextPrimary
                });
            }
            else if (m.Groups[4].Success)
            {
                // `inline code`
                var codeBorder = new Border
                {
                    Background = AppTheme.InlineCodeBg,
                    BorderBrush = AppTheme.InlineCodeBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(1, 0, 1, 0),
                    Child = new TextBlock
                    {
                        Text = m.Groups[4].Value,
                        Foreground = AppTheme.InlineCodeText,
                        FontFamily = AppTheme.FontMono,
                        FontSize = AppTheme.FontInlineCode
                    }
                };
                tb.Inlines.Add(new System.Windows.Documents.InlineUIContainer(codeBorder));
            }

            pos = m.Index + m.Length;
        }

        // Remaining text
        if (pos < text.Length)
        {
            tb.Inlines.Add(new System.Windows.Documents.Run(text.Substring(pos)));
        }

        return tb;
    }

    // ──────────────────────────────────────────────────────────────
    //  Bullet Line
    // ──────────────────────────────────────────────────────────────
    private static UIElement BuildBulletLine(string text)
    {
        var panel = new DockPanel { Margin = new Thickness(AppTheme.SpaceSM, 2, 0, 2) };

        panel.Children.Add(new TextBlock
        {
            Text = "\u2022",
            Foreground = AppTheme.AccentBlue,
            FontSize = AppTheme.FontBody,
            Width = 16,
            VerticalAlignment = VerticalAlignment.Top
        });

        var content = BuildFormattedTextBlock(text);
        content.Margin = new Thickness(0);
        panel.Children.Add(content);

        return panel;
    }

    // ──────────────────────────────────────────────────────────────
    //  Numbered Line
    // ──────────────────────────────────────────────────────────────
    private static UIElement BuildNumberedLine(string number, string text)
    {
        var panel = new DockPanel { Margin = new Thickness(AppTheme.SpaceSM, 2, 0, 2) };

        panel.Children.Add(new TextBlock
        {
            Text = $"{number}.",
            Foreground = AppTheme.AccentBlue,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontSmall,
            Width = 20,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var content = BuildFormattedTextBlock(text);
        content.Margin = new Thickness(0);
        panel.Children.Add(content);

        return panel;
    }

    // ══════════════════════════════════════════════════════════════
    //  Code Block — terminal style with header + copy button
    // ══════════════════════════════════════════════════════════════
    private static UIElement BuildCodeBlock(string language, string code)
    {
        var container = new Border
        {
            Background = AppTheme.TerminalBg,
            BorderBrush = AppTheme.TerminalBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceSM),
            ClipToBounds = true
        };

        var stack = new DockPanel();

        // Header bar
        var header = new Border
        {
            Background = AppTheme.TerminalHeaderBg,
            BorderBrush = AppTheme.TerminalBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM, AppTheme.SpaceMD, AppTheme.SpaceSM)
        };

        header.Child = new TextBlock
        {
            Text = language,
            Foreground = AppTheme.TextMuted,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontSmall,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(header, Dock.Top);
        stack.Children.Add(header);

        // Code content
        var codeText = new TextBlock
        {
            Text = code,
            Foreground = AppTheme.TerminalText,
            FontFamily = AppTheme.FontMono,
            FontSize = AppTheme.FontTerminalLog,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(AppTheme.SpaceLG)
        };
        stack.Children.Add(codeText);

        container.Child = stack;
        return container;
    }

    // ══════════════════════════════════════════════════════════════
    //  Action Bar — "Run Script in Revit" + "Copy Code"
    // ══════════════════════════════════════════════════════════════
    private UIElement BuildActionBar(string codeContent)
    {
        var panel = new Border
        {
            Background = AppTheme.ActionBarBg,
            BorderBrush = AppTheme.SubtleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Padding = new Thickness(AppTheme.SpaceSM),
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, 0)
        };

        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // Primary: Chạy Script trong Revit
        var runBtn = CreateActionButton(
            "\u25B6",
            "Ch\u1EA1y Script trong Revit",
            AppTheme.GradientAccent,
            AppTheme.TextPrimary,
            true);
        runBtn.MouseLeftButtonDown += (_, _) => RunScriptRequested?.Invoke(codeContent);
        buttonsPanel.Children.Add(runBtn);

        // Secondary: Copy Code
        var copyBtn = CreateActionButton(
            IconLibrary.Copy,
            "Copy Code",
            Brushes.Transparent,
            AppTheme.TextSecondary,
            false);
        copyBtn.MouseLeftButtonDown += (_, _) =>
        {
            try { Clipboard.SetText(codeContent); } catch { }
        };
        buttonsPanel.Children.Add(copyBtn);

        panel.Child = buttonsPanel;
        return panel;
    }

    private static Border CreateActionButton(string icon, string label, Brush background, Brush foreground, bool isPrimary)
    {
        var border = new Border
        {
            Background = background,
            BorderBrush = isPrimary ? Brushes.Transparent : AppTheme.SubtleBorder,
            BorderThickness = new Thickness(isPrimary ? 0 : 1),
            CornerRadius = new CornerRadius(AppTheme.ButtonRadius),
            Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM, AppTheme.SpaceLG, AppTheme.SpaceSM),
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            Cursor = Cursors.Hand
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = icon.Length == 1 && char.IsHighSurrogate(icon[0]) || icon.Length == 1 && icon[0] > '\uE000'
                ? AppTheme.FontIcon : AppTheme.FontUI,
            FontSize = 12,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = foreground,
            FontSize = AppTheme.FontSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        border.Child = content;

        // Hover effects
        border.MouseEnter += (_, _) =>
        {
            border.Opacity = 0.85;
            AnimationHelper.ScaleTo(border, 1.02, AppTheme.AnimFast);
        };
        border.MouseLeave += (_, _) =>
        {
            border.Opacity = 1.0;
            AnimationHelper.ScaleTo(border, 1.0, AppTheme.AnimFast);
        };

        return border;
    }

    // ══════════════════════════════════════════════════════════════
    //  Response Card — icon circle + title + description
    //  (Public static so WorkerTab or other surfaces can use it)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a structured response card like the BIMAI reference:
    /// Left accent border + icon circle + title + description with inline code.
    /// </summary>
    internal static Border CreateResponseCard(
        string iconChar, string title, string description,
        SolidColorBrush accentColor, SolidColorBrush iconBgColor)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });   // accent bar
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });       // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text

        // Accent bar (left edge)
        var accentBar = new Border
        {
            Background = accentColor,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 4, AppTheme.SpaceMD, 4)
        };
        Grid.SetColumn(accentBar, 0);
        grid.Children.Add(accentBar);

        // Icon circle
        var iconCircle = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = iconBgColor,
            Margin = new Thickness(0, 0, AppTheme.SpaceMD, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = iconChar,
                FontFamily = AppTheme.FontIcon,
                FontSize = 13,
                Foreground = accentColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconCircle, 1);
        grid.Children.Add(iconCircle);

        // Title + description
        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceXS)
        });

        var descBlock = BuildFormattedTextBlock(description);
        descBlock.Foreground = AppTheme.TextMuted;
        textStack.Children.Add(descBlock);

        Grid.SetColumn(textStack, 2);
        grid.Children.Add(textStack);

        return new Border
        {
            Background = AppTheme.ResponseCardBg,
            BorderBrush = AppTheme.ResponseCardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.CardRadius),
            Padding = new Thickness(AppTheme.SpaceLG),
            Margin = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceSM),
            Child = grid
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Utility — extract last code block text
    // ══════════════════════════════════════════════════════════════
    private static string? ExtractLastCodeBlock(string content)
    {
        var matches = CodeBlockRegex.Matches(content);
        if (matches.Count == 0) return null;
        return matches[matches.Count - 1].Groups[2].Value.Trim();
    }
}
