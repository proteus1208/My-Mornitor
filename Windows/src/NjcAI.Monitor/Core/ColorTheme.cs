namespace NjcAI.Monitor.Core;

internal enum ColorTheme
{
    BitcoinOrange = 0,
    Web3Emerald = 1,
    DeepOcean = 2,
    RoyalIndigo = 3,
}

internal static class ColorThemeNames
{
    public static string DisplayName(this ColorTheme theme) => theme switch
    {
        ColorTheme.Web3Emerald => "Web3 Emerald",
        ColorTheme.DeepOcean => "Deep Ocean",
        ColorTheme.RoyalIndigo => "Royal Indigo",
        _ => "Bitcoin Orange",
    };
}
