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
            C(0, 148, 98), C(190, 255, 221), C(16, 107, 75),
            C(8, 40, 29), C(4, 25, 18), C(7, 35, 25), C(18, 68, 50)),
        ColorTheme.DeepOcean => new ThemePalette(
            C(2, 7, 17), C(6, 20, 36), C(8, 21, 37), C(5, 13, 25),
            C(33, 71, 100), C(43, 95, 130), C(29, 65, 93), C(46, 110, 151),
            C(20, 40, 58), C(241, 247, 255), C(137, 155, 173), C(185, 200, 214),
            C(140, 207, 255), C(59, 130, 208), C(74, 155, 218), C(255, 112, 105),
            C(42, 100, 163), C(204, 232, 251), C(40, 81, 113),
            C(10, 30, 49), C(5, 18, 31), C(8, 26, 43), C(25, 55, 78)),
        ColorTheme.RoyalIndigo => new ThemePalette(
            C(7, 6, 17), C(21, 16, 42), C(18, 16, 32), C(11, 8, 22),
            C(68, 58, 98), C(89, 75, 125), C(58, 51, 82), C(107, 91, 145),
            C(41, 36, 58), C(247, 244, 255), C(156, 151, 174), C(205, 199, 219),
            C(184, 167, 242), C(118, 102, 201), C(146, 126, 221), C(255, 112, 135),
            C(101, 83, 168), C(226, 218, 255), C(80, 67, 112),
            C(31, 27, 49), C(17, 14, 30), C(25, 21, 40), C(54, 46, 75)),
        _ => new ThemePalette(
            C(3, 2, 1), C(12, 5, 2), C(26, 13, 6), C(16, 7, 4),
            C(91, 48, 20), C(138, 70, 24), C(107, 53, 18), C(166, 82, 24),
            C(48, 34, 23), C(246, 241, 236), C(171, 145, 127), C(216, 196, 181),
            C(255, 212, 90), C(247, 147, 26), C(255, 92, 53), C(255, 105, 116),
            C(217, 106, 0), C(255, 226, 164), C(132, 69, 22),
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
