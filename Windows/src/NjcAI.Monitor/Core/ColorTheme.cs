namespace NjcAI.Monitor.Core;

internal enum ColorTheme
{
    BitcoinOrange,
    Web3Emerald,
    CyberBlue,
    Ultraviolet,
}

internal static class ColorThemeNames
{
    public static string DisplayName(this ColorTheme theme) => theme switch
    {
        ColorTheme.Web3Emerald => "Web3 Emerald",
        ColorTheme.CyberBlue => "Cyber Blue",
        ColorTheme.Ultraviolet => "Ultraviolet",
        _ => "Bitcoin Orange",
    };
}
