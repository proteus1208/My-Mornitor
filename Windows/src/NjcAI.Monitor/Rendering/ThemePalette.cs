using System.Drawing;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Rendering;

internal sealed record ThemePalette(
    Color Background,
    Color BackgroundGradientEnd,
    Color Panel,
    Color PanelGradientEnd,
    Color PanelBorder,
    Color ModuleSeparator,
    Color InnerSeparator,
    Color NewsSeparator,
    Color Track,
    Color PrimaryText,
    Color SecondaryText,
    Color SecondaryTextBright,
    Color AccentHighlight,
    Color AccentPrimary,
    Color AccentStrong,
    Color Error,
    Color MarketDown,
    Color Quota,
    Color CompletionBright,
    Color CompletionDim,
    Color AlertBackgroundStart,
    Color AlertBackgroundEnd,
    Color AlertCard,
    Color AlertSecondaryButton)
{
    public static ThemePalette For(ColorTheme theme) => theme switch
    {
        ColorTheme.Web3Emerald => new ThemePalette(
            C(1, 8, 6), C(2, 18, 13), C(6, 26, 19), C(3, 15, 11),
            C(18, 107, 77), C(16, 141, 94), C(16, 105, 74), C(24, 166, 112),
            C(12, 45, 34), C(242, 255, 249), C(130, 169, 154), C(180, 220, 204),
            C(103, 255, 177), C(0, 229, 153), C(0, 201, 133), C(255, 107, 107),
            C(255, 107, 107), C(0, 148, 98), C(190, 255, 221), C(16, 107, 75),
            C(8, 40, 29), C(4, 25, 18), C(7, 35, 25), C(18, 68, 50)),
        ColorTheme.CyberBlue => new ThemePalette(
            C(2, 7, 18), C(4, 18, 38), C(7, 20, 38), C(4, 12, 26),
            C(22, 77, 122), C(26, 100, 164), C(22, 74, 116), C(30, 123, 190),
            C(14, 34, 58), C(241, 247, 255), C(130, 151, 179), C(183, 204, 228),
            C(41, 217, 255), C(47, 140, 255), C(30, 185, 255), C(255, 122, 104),
            C(255, 122, 104), C(20, 103, 210), C(193, 235, 255), C(23, 79, 127),
            C(8, 28, 52), C(4, 17, 34), C(7, 25, 47), C(20, 55, 91)),
        ColorTheme.Ultraviolet => new ThemePalette(
            C(7, 3, 15), C(19, 6, 35), C(22, 10, 36), C(12, 5, 22),
            C(86, 48, 128), C(111, 62, 160), C(78, 43, 116), C(137, 72, 190),
            C(41, 24, 59), C(247, 242, 255), C(166, 154, 184), C(213, 199, 229),
            C(230, 87, 255), C(155, 108, 255), C(197, 81, 255), C(255, 112, 135),
            C(56, 230, 197), C(118, 65, 210), C(238, 212, 255), C(91, 50, 132),
            C(37, 16, 59), C(22, 8, 38), C(31, 13, 51), C(65, 35, 91)),
        _ => new ThemePalette(
            C(3, 2, 1), C(12, 5, 2), C(26, 13, 6), C(16, 7, 4),
            C(91, 48, 20), C(138, 70, 24), C(107, 53, 18), C(166, 82, 24),
            C(48, 34, 23), C(246, 241, 236), C(171, 145, 127), C(216, 196, 181),
            C(255, 212, 90), C(247, 147, 26), C(255, 92, 53), C(255, 105, 116),
            C(0, 255, 163), C(217, 106, 0), C(255, 226, 164), C(132, 69, 22),
            C(50, 25, 12), C(36, 17, 7), C(42, 21, 10), C(74, 42, 28)),
    };

    private static Color C(byte red, byte green, byte blue) => Color.FromArgb(red, green, blue);
}

internal static class ThemePaletteContext
{
    private static readonly AsyncLocal<ThemePalette?> Active = new();

    public static ThemePalette Current => Active.Value ?? ThemePalette.For(ColorTheme.BitcoinOrange);

    public static IDisposable Use(ColorTheme theme)
    {
        var previous = Active.Value;
        Active.Value = ThemePalette.For(theme);
        return new Scope(() => Active.Value = previous);
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
