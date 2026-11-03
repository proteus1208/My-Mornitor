namespace NjcAI.Monitor.Core;

internal enum ColorTheme
{
    BitcoinOrange = 0,
    Web3Emerald = 1,
    GraphiteTitanium = 2,
    CyberNeon = 4,
    SolanaAurora = 5,
}

internal static class ColorThemeNames
{
    public static string DisplayName(this ColorTheme theme) => theme switch
    {
        ColorTheme.Web3Emerald => "Web3 Emerald",
        ColorTheme.GraphiteTitanium => "Graphite Titanium",
        ColorTheme.CyberNeon => "Cyber Neon",
        ColorTheme.SolanaAurora => "Solana Aurora",
        _ => "Bitcoin Orange",
    };
}
