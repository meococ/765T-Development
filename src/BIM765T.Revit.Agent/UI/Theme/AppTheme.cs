using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BIM765T.Revit.Agent.Config;

namespace BIM765T.Revit.Agent.UI.Theme;

internal static class AppTheme
{
    internal static string CurrentMode { get; private set; } = UiThemeModes.Dark;

    internal static bool IsDarkMode => string.Equals(CurrentMode, UiThemeModes.Dark, System.StringComparison.OrdinalIgnoreCase);

    internal static void SetMode(string? mode)
    {
        CurrentMode = NormalizeMode(mode);
    }

    internal static string ToggleMode()
    {
        CurrentMode = IsDarkMode ? UiThemeModes.Light : UiThemeModes.Dark;
        return CurrentMode;
    }

    internal static string NormalizeMode(string? mode)
    {
        return string.Equals(mode, UiThemeModes.Light, System.StringComparison.OrdinalIgnoreCase)
            ? UiThemeModes.Light
            : UiThemeModes.Dark;
    }

    internal static SolidColorBrush PageBackground => Brush("#0A0A0C", "#F3F4F8");
    internal static SolidColorBrush CardBackground => Brush("#111317", "#FBFBFD");
    internal static SolidColorBrush SurfaceElevated => Brush("#171A1F", "#FFFFFF");
    internal static SolidColorBrush CardBorder => Brush("#262A32", "#D9DEE8");
    internal static SolidColorBrush SubtleBorder => AlphaBrush("#FFFFFF", 0.08, "#0F172A", 0.10);

    internal static SolidColorBrush Accent => Brush("#22D3EE", "#0F766E");
    internal static SolidColorBrush AccentAlt => Brush("#8B5CF6", "#6D28D9");
    internal static SolidColorBrush AccentBlue => Brush("#60A5FA", "#2563EB");
    internal static SolidColorBrush AccentDim => Brush("#103142", "#D7EAFE");

    internal static LinearGradientBrush GradientAccent => Gradient("#5B8CFF", "#8B5CF6", "#2563EB", "#7C3AED");
    internal static LinearGradientBrush GradientCyanPurple => Gradient("#22D3EE", "#8B5CF6", "#0F766E", "#7C3AED");

    internal static SolidColorBrush Success => Brush("#34D399", "#15803D");
    internal static SolidColorBrush Warning => Brush("#F59E0B", "#B45309");
    internal static SolidColorBrush Danger => Brush("#F87171", "#B91C1C");
    internal static SolidColorBrush Info => Brush("#38BDF8", "#0369A1");

    internal static SolidColorBrush TextPrimary => Brush("#F8FAFC", "#111827");
    internal static SolidColorBrush TextSecondary => Brush("#CBD5E1", "#334155");
    internal static SolidColorBrush TextMuted => Brush("#94A3B8", "#64748B");
    internal static SolidColorBrush TextDisabled => Brush("#475569", "#94A3B8");

    internal static SolidColorBrush HoverBg => Brush("#181C22", "#EEF2FF");
    internal static SolidColorBrush PressedBg => Brush("#11151B", "#E2E8F0");
    internal static SolidColorBrush HoverBorder => Brush("#313744", "#C3CBD9");
    internal static SolidColorBrush SelectedBg => Brush("#15263D", "#DBEAFE");
    internal static SolidColorBrush ActiveIndicator => Brush("#22D3EE", "#2563EB");
    internal static SolidColorBrush FocusRing => AlphaBrush("#60A5FA", 0.50, "#2563EB", 0.35);

    internal static SolidColorBrush SuccessBg => Brush("#052E16", "#DCFCE7");
    internal static SolidColorBrush WarningBg => Brush("#422006", "#FEF3C7");
    internal static SolidColorBrush ErrorBg => Brush("#450A0A", "#FEE2E2");
    internal static SolidColorBrush InfoBg => Brush("#082F49", "#E0F2FE");

    internal static SolidColorBrush DetailBg => Brush("#0E1116", "#F8FAFC");
    internal static SolidColorBrush CardBgHover => Brush("#191D24", "#F8FAFC");

    internal static SolidColorBrush TerminalBg => Brush("#0C0E12", "#F8FAFC");
    internal static SolidColorBrush TerminalBorder => Brush("#20242D", "#D9DEE8");
    internal static SolidColorBrush TerminalHeaderBg => Brush("#12161C", "#EEF2F7");
    internal static SolidColorBrush TerminalText => Brush("#D8DEE9", "#1F2937");
    internal static SolidColorBrush TerminalGreen => Brush("#4ADE80", "#15803D");
    internal static SolidColorBrush TerminalYellow => Brush("#FACC15", "#B45309");
    internal static SolidColorBrush TerminalCyan => Brush("#22D3EE", "#0369A1");
    internal static SolidColorBrush TerminalRed => Brush("#F87171", "#B91C1C");
    internal static SolidColorBrush TerminalDimText => Brush("#71717A", "#64748B");
    internal static SolidColorBrush TerminalDots => Brush("#52525B", "#CBD5E1");

    internal static SolidColorBrush InlineCodeBg => Brush("#171A20", "#EEF2F7");
    internal static SolidColorBrush InlineCodeBorder => Brush("#2D3440", "#CBD5E1");
    internal static SolidColorBrush InlineCodeText => Brush("#7DD3FC", "#0F766E");

    internal static SolidColorBrush CardGlowCyan => Brush("#0E4F5C", "#CFFAFE");
    internal static SolidColorBrush CardGlowGreen => Brush("#052E16", "#DCFCE7");
    internal static SolidColorBrush CardGlowViolet => Brush("#2E1065", "#F3E8FF");
    internal static SolidColorBrush CardGlowAmber => Brush("#451A03", "#FEF3C7");
    internal static SolidColorBrush CardGlowBlue => Brush("#172554", "#DBEAFE");

    internal static SolidColorBrush SectionIconCyan => Brush("#155E75", "#CCFBF1");
    internal static SolidColorBrush SectionIconGreen => Brush("#065F46", "#DCFCE7");
    internal static SolidColorBrush SectionIconViolet => Brush("#4C1D95", "#F3E8FF");
    internal static SolidColorBrush SectionIconAmber => Brush("#78350F", "#FEF3C7");
    internal static SolidColorBrush SectionIconBlue => Brush("#1E3A5F", "#DBEAFE");

    internal static SolidColorBrush TagBorderCyan => Brush("#0E7490", "#67E8F9");
    internal static SolidColorBrush TagBorderGreen => Brush("#059669", "#4ADE80");
    internal static SolidColorBrush TagBorderViolet => Brush("#7C3AED", "#C4B5FD");
    internal static SolidColorBrush TagBorderAmber => Brush("#D97706", "#FCD34D");
    internal static SolidColorBrush TagBorderBlue => Brush("#3B82F6", "#93C5FD");

    internal static SolidColorBrush UserAvatarBg => Brush("#242936", "#E2E8F0");
    internal static SolidColorBrush BotAvatarBg => AlphaBrush("#60A5FA", 0.16, "#2563EB", 0.08);
    internal static SolidColorBrush BotAvatarBorder => AlphaBrush("#60A5FA", 0.32, "#2563EB", 0.18);
    internal static SolidColorBrush ResponseCardBg => Brush("#12161C", "#FFFFFF");
    internal static SolidColorBrush ResponseCardBorder => AlphaBrush("#FFFFFF", 0.08, "#0F172A", 0.12);
    internal static SolidColorBrush ActionBarBg => Brush("#0F1318", "#F8FAFC");

    internal static SolidColorBrush TrustReadOnly => Success;
    internal static SolidColorBrush TrustPreview => AccentBlue;
    internal static SolidColorBrush TrustApproval => Warning;
    internal static SolidColorBrush TrustExecuted => AccentAlt;
    internal static SolidColorBrush TrustVerified => Success;
    internal static SolidColorBrush TrustResidual => Danger;

    internal static SolidColorBrush PillBackground => Brush("#10141A", "#F8FAFC");
    internal static SolidColorBrush PillBorder => Brush("#2A313E", "#D1D9E6");
    internal static SolidColorBrush DrawerBackground => Brush("#0E1218", "#FFFFFF");
    internal static SolidColorBrush DrawerBorder => Brush("#232B38", "#D9DEE8");
    internal static SolidColorBrush SidebarBackground => Brush("#0B0E13", "#FFFFFF");
    internal static SolidColorBrush SidebarSelectedBg => Brush("#131F32", "#E0E7FF");
    internal static SolidColorBrush HeroBackground => Brush("#10151D", "#FFFFFF");
    internal static SolidColorBrush SurfaceMuted => Brush("#131821", "#F8FAFC");
    internal static SolidColorBrush SurfaceOverlay => AlphaBrush("#FFFFFF", 0.03, "#FFFFFF", 0.78);

    internal const double FontTitle = 20;
    internal const double FontSection = 16;
    internal const double FontBody = 14;
    internal const double FontSecondary = 13;
    internal const double FontCaption = 12;
    internal const double FontSmall = 11;
    internal const double FontHero = 26;
    internal const double FontTerminalLog = 13;
    internal const double FontInlineCode = 12.5;
    internal const double LineHeightRelaxed = 24;

    internal static readonly FontFamily FontUI = new FontFamily("Segoe UI Variable, Segoe UI, Roboto");
    internal static readonly FontFamily FontMono = new FontFamily("Cascadia Mono, Cascadia Code, JetBrains Mono, Consolas");
    internal static readonly FontFamily FontIcon = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Default FontWeight for body text — Medium (500) for crisp readability.</summary>
    internal static readonly FontWeight WeightBody = FontWeights.Medium;
    /// <summary>FontWeight for headings — SemiBold (600).</summary>
    internal static readonly FontWeight WeightHeading = FontWeights.SemiBold;
    /// <summary>FontWeight for emphasis / buttons — Bold (700).</summary>
    internal static readonly FontWeight WeightStrong = FontWeights.Bold;

    internal const double SpaceXS = 4;
    internal const double SpaceSM = 8;
    internal const double SpaceMD = 12;
    internal const double SpaceLG = 16;
    internal const double SpaceXL = 24;
    internal const double SpaceXXL = 32;

    internal const double CardRadius = 10;
    internal const double CardRadiusLarge = 14;
    internal const double ButtonRadius = 8;
    internal const double ButtonRadiusLarge = 12;
    internal const double BadgeRadius = 6;
    internal const double PillRadius = 6;
    internal const double NavWidth = 44;
    internal const double InspectorDrawerWidth = 286;
    internal const double InspectorDrawerCompactWidth = 240;
    internal const double StatusBarHeight = 36;
    internal const double ButtonMinHeight = 48;
    internal const double ButtonCompactHeight = 36;
    internal const double IconSizeDefault = 14;
    internal const double IconSizeLarge = 20;
    internal const double AvatarSize = 32;
    internal const double CompactPaneWidth = 760;
    internal const double NarrowPaneWidth = 620;

    internal const int AnimFast = 120;
    internal const int AnimNormal = 200;
    internal const int AnimSlow = 350;
    internal const int ToastDuration = 4000;
    internal const int MaxToasts = 3;

    internal static DropShadowEffect GlowCyan => Glow("#22D3EE", "#0891B2", 20, 0.20);
    internal static DropShadowEffect GlowBlue => Glow("#60A5FA", "#2563EB", 24, 0.18);
    internal static DropShadowEffect GlowPurple => Glow("#A78BFA", "#7C3AED", 20, 0.18);
    internal static DropShadowEffect CardShadow => new DropShadowEffect
    {
        Color = IsDarkMode ? Colors.Black : ColorFrom("#CBD5E1"),
        BlurRadius = IsDarkMode ? 10 : 14,
        ShadowDepth = IsDarkMode ? 2 : 3,
        Opacity = IsDarkMode ? 0.40 : 0.18,
        Direction = 270
    };

    internal static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush(ColorFrom(hex));
        brush.Freeze();
        return brush;
    }

    internal static SolidColorBrush FrozenAlpha(string hex, double alpha)
    {
        var color = ColorFrom(hex);
        color.A = (byte)(alpha * 255);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    internal static LinearGradientBrush FrozenGradient(string hexStart, string hexEnd)
    {
        var brush = new LinearGradientBrush(ColorFrom(hexStart), ColorFrom(hexEnd), 0);
        brush.Freeze();
        return brush;
    }

    internal static Color ColorFrom(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    internal static Pen FrozenPen(SolidColorBrush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush Brush(string darkHex, string lightHex)
    {
        return Frozen(IsDarkMode ? darkHex : lightHex);
    }

    private static SolidColorBrush AlphaBrush(string darkHex, double darkAlpha, string lightHex, double lightAlpha)
    {
        return FrozenAlpha(IsDarkMode ? darkHex : lightHex, IsDarkMode ? darkAlpha : lightAlpha);
    }

    private static LinearGradientBrush Gradient(string darkStart, string darkEnd, string lightStart, string lightEnd)
    {
        return FrozenGradient(IsDarkMode ? darkStart : lightStart, IsDarkMode ? darkEnd : lightEnd);
    }

    private static DropShadowEffect Glow(string darkHex, string lightHex, double blurRadius, double opacity)
    {
        return new DropShadowEffect
        {
            Color = ColorFrom(IsDarkMode ? darkHex : lightHex),
            BlurRadius = blurRadius,
            ShadowDepth = 0,
            Opacity = opacity
        };
    }
}
