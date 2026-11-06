namespace NjcAI.Monitor.Core;

internal static class DemoData
{
    public static DashboardSnapshot Create(bool idle = false, bool completed = false)
    {
        var random = new Random(42);
        var system = new SystemSnapshot(
            DateTime.Now,
            58,
            Enumerable.Range(0, 10).Select(_ => (double)random.Next(24, 88)).ToArray(),
            52,
            47,
            36,
            32UL * 1024 * 1024 * 1024,
            14UL * 1024 * 1024 * 1024,
            18UL * 1024 * 1024 * 1024,
            99_000,
            312,
            4.8 * 1024 * 1024,
            820 * 1024,
            0,
            0);
        var claude = new AgentSnapshot(
            AgentKind.Claude, true, 48_300_000, 512_000, DateTime.Now.AddMinutes(-35),
            "NJC AI Monitor", "System metrics and the Windows tray app are complete.",
            false, false, null, "claude-opus", 4, 4, "Task completed", null, null, true, "demo-claude");
        var codex = new AgentSnapshot(
            AgentKind.Codex, true, 184_000, 24_000, DateTime.Now,
            "windows-port", "Deployment complete; all modules passed the build: USB, Renderer, and Agents.",
            true, false, null, "gpt-5.6", 4, 6, "Build release package and user guide", 57, DateTime.Now.AddDays(5), true,
            "session-windows-port", 10_080);
        var codexSecond = new AgentSnapshot(
            AgentKind.Codex, true, 62_000, 8_400, DateTime.Now.AddMinutes(-40),
            "market-panel", "Market display verification completed.",
            false, false, null, "gpt-5.6", 4, 4, "Task completed", 57, DateTime.Now.AddDays(5), true,
            "session-market-panel", 10_080);
        var cursor = new AgentSnapshot(
            AgentKind.Cursor, true, 0, 0, DateTime.Now.AddMinutes(-30),
            "dashboard", "Waiting for a new agent message…", false, false,
            null, "composer", null, null, null, null, null, false, "demo-cursor");
        var grok = new AgentSnapshot(
            AgentKind.Grok, true, 0, 0, null,
            "Grok Bot", null, false, false, null, "SuperGrok",
            null, null, null, 31.2, DateTime.Now.AddDays(4), false,
            null, 10_080);
        var markets = new MarketSnapshot(
            [
                new MarketQuote("BGBL", 75.86m, 0.42m, "AUD", DateTime.Now.AddMinutes(-20), false,
                    Source: "Yahoo", History: [75.32m, 75.48m, 75.44m, 75.67m, 75.73m, 75.86m]),
                new MarketQuote("NDQ", 54.31m, 0.75m, "AUD", DateTime.Now.AddMinutes(-20), false,
                    Source: "Yahoo", History: [53.92m, 54.02m, 54.08m, 54.06m, 54.22m, 54.31m]),
                new MarketQuote("IBIT", 26.70m, 0.28m, "AUD", DateTime.Now.AddMinutes(-20), false,
                    Source: "Yahoo", History: [26.42m, 26.55m, 26.51m, 26.62m, 26.66m, 26.70m]),
                new MarketQuote("IVV", 70.50m, -0.18m, "AUD", DateTime.Now.AddMinutes(-20), false,
                    Source: "Yahoo", History: [70.72m, 70.61m, 70.66m, 70.58m, 70.55m, 70.50m]),
            ],
            [
                new MarketQuote("USDT/AUD", 1.4053m, -0.59m, "AUD", DateTime.Now, true,
                    PriceTickSize: 0.0001m, Source: "OKX",
                    History: [1.4140m, 1.4118m, 1.4126m, 1.4089m, 1.4074m, 1.4053m]),
                new MarketQuote("BTC", 62_483.2m, 3.50m, "USDT", DateTime.Now, true,
                    PriceTickSize: 0.1m, Source: "OKX",
                    History: [60_820m, 61_440m, 61_180m, 62_010m, 61_860m, 62_483.2m]),
                new MarketQuote("ETH", 1_855.01m, 3.30m, "USDT", DateTime.Now, true,
                    PriceTickSize: 0.01m, Source: "OKX",
                    History: [1_792m, 1_811m, 1_805m, 1_834m, 1_842m, 1_855.01m]),
                new MarketQuote("WLFI", 0.0546m, -0.80m, "USDT", DateTime.Now, true,
                    PriceTickSize: 0.0001m, Source: "OKX",
                    History: [0.0554m, 0.0551m, 0.0552m, 0.0549m, 0.0548m, 0.0546m]),
            ],
            DateTime.Now,
            null);
        var news = new NewsSnapshot(
        [
            new NewsArticle("OpenAI releases a new generation of agent tools for developers", "Frontier Tech", 5, 4,
                DateTime.Now.AddMinutes(-12), "https://news.ycombinator.com/", "Hacker News"),
            new NewsArticle("Global markets focus on this week's inflation and employment data", "World", 5, 6,
                DateTime.Now.AddMinutes(-28), "https://www.gdeltproject.org/", "GDELT"),
            new NewsArticle("Bitcoin ETFs continue to record net inflows", "WEB3", 4, 3,
                DateTime.Now.AddMinutes(-45), "https://www.coindesk.com/", "CoinDesk"),
            new NewsArticle("Australian markets await the latest rate path and earnings guidance", "Finance", 4, 5,
                DateTime.Now.AddHours(-1), "https://www.gdeltproject.org/", "GDELT"),
            new NewsArticle("AI infrastructure investment remains a focus for the technology sector", "Technology", 3, 2,
                DateTime.Now.AddHours(-2), "https://news.ycombinator.com/", "Hacker News"),
        ], DateTime.Now, null);
        var weather = new WeatherSnapshot(
            "Sydney", 19, 18, 64, 13, 70, 0.2, 2, 22, 14, 30,
            [
                new WeatherHour(DateTime.Now.AddHours(1), 19, 20, 13, 70),
                new WeatherHour(DateTime.Now.AddHours(3), 18, 24, 15, 75),
                new WeatherHour(DateTime.Now.AddHours(5), 17, 30, 17, 82),
                new WeatherHour(DateTime.Now.AddHours(7), 16, 25, 14, 88),
            ], DateTime.Now, null);
        var idleCodex = codex with
        {
            LastActive = DateTime.Now.AddHours(-1),
            IsWorking = false,
            NeedsAttention = false,
        };
        var completedSecondCodex = codexSecond with
        {
            LastActive = DateTime.Now.AddSeconds(-20),
            IsWorking = false,
            NeedsAttention = true,
            CompletedAt = DateTime.Now.AddSeconds(-20),
            StepCurrent = 4,
            StepTotal = 4,
            StepText = "Task completed",
        };
        var agents = idle
            ? new AgentsSnapshot(
                AgentSnapshot.Missing(AgentKind.Claude),
                idleCodex,
                AgentSnapshot.Missing(AgentKind.Cursor),
                [idleCodex],
                grok)
            : completed
                ? new AgentsSnapshot(claude, codex, cursor, [codex, completedSecondCodex], grok)
                : new AgentsSnapshot(claude, codex, cursor, [codex, codexSecond], grok);
        return new DashboardSnapshot(system, agents,
            markets, news, weather);
    }
}
