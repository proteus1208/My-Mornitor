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
    Color AccentSecondary,
    Color Error,
    Color MarketUp,
    Color MarketDown,
    Color Quota,
    Color CompletionBright,
    Color CompletionDim,
    Color AlertBackgroundStart,
    Color AlertBackgroundEnd,
    Color AlertCard,
    Color AlertSecondaryButton,
    bool GradientMarketUp = false,
    bool GradientMarketDown = false)
{
    public bool UsesDualAccent => AccentSecondary.ToArgb() != AccentStrong.ToArgb();

    public static ThemePalette For(ColorTheme theme) => theme switch
    {
        ColorTheme.Web3Emerald => new ThemePalette(
            C(1, 5, 2), C(4, 14, 5), C(6, 18, 7), C(3, 10, 4),
            C(75, 118, 34), C(116, 171, 43), C(61, 103, 33), C(135, 194, 48),
            C(24, 43, 20), C(246, 255, 239), C(151, 174, 139), C(202, 224, 188),
            C(218, 255, 91), C(157, 255, 63), C(92, 230, 55), C(0, 245, 160), C(255, 83, 133),
            C(157, 255, 63), C(255, 77, 94),
            C(107, 224, 42), C(230, 255, 146), C(72, 110, 35),
            C(12, 30, 8), C(5, 15, 5), C(9, 24, 8), C(43, 68, 31)),
        ColorTheme.GraphiteTitanium => new ThemePalette(
            C(7, 9, 13), C(11, 17, 23), C(16, 21, 28), C(9, 13, 18),
            C(59, 70, 82), C(100, 115, 131), C(59, 72, 83), C(112, 129, 145),
            C(36, 44, 53), C(242, 246, 250), C(152, 165, 179), C(196, 206, 216),
            C(232, 240, 247), C(216, 226, 236), C(154, 170, 185), C(89, 225, 213), C(255, 89, 104),
            C(89, 225, 213), C(255, 89, 104),
            C(115, 199, 194), C(234, 251, 255), C(89, 103, 113),
            C(20, 26, 34), C(9, 12, 17), C(17, 23, 30), C(53, 65, 75)),
        ColorTheme.CyberNeon => new ThemePalette(
            C(2, 3, 10), C(6, 13, 24), C(8, 11, 20), C(4, 6, 12),
            C(38, 52, 74), C(31, 94, 112), C(27, 51, 70), C(27, 112, 132),
            C(20, 32, 45), C(243, 247, 255), C(137, 149, 168), C(185, 196, 211),
            C(168, 247, 255), C(0, 229, 255), C(0, 202, 228), C(255, 45, 170), C(255, 86, 145),
            C(0, 229, 255), C(255, 45, 170),
            C(0, 154, 179), C(255, 210, 242), C(122, 20, 82),
            C(15, 11, 28), C(7, 7, 16), C(12, 10, 23), C(40, 28, 55)),
        ColorTheme.SolanaAurora => new ThemePalette(
            C(5, 3, 11), C(10, 7, 22), C(14, 10, 28), C(8, 5, 18),
            C(76, 55, 110), C(116, 80, 162), C(53, 75, 97), C(80, 111, 128),
            C(36, 28, 51), C(247, 244, 255), C(167, 155, 184), C(211, 203, 224),
            C(167, 245, 66), C(153, 69, 255), C(20, 241, 217), C(167, 245, 66), C(255, 92, 122),
            C(167, 245, 66), C(255, 92, 122),
            C(124, 92, 255), C(215, 255, 140), C(94, 59, 128),
            C(26, 13, 40), C(8, 6, 17), C(18, 11, 29), C(58, 40, 82),
            GradientMarketUp: true,
            GradientMarketDown: true),
        _ => new ThemePalette(
            C(3, 2, 1), C(12, 5, 2), C(26, 13, 6), C(16, 7, 4),
            C(91, 48, 20), C(138, 70, 24), C(107, 53, 18), C(166, 82, 24),
            C(48, 34, 23), C(246, 241, 236), C(171, 145, 127), C(216, 196, 181),
            C(255, 212, 90), C(247, 147, 26), C(255, 92, 53), C(255, 92, 53), C(255, 105, 116),
            C(247, 147, 26), C(0, 229, 153),
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
