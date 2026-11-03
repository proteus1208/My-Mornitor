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
            "NJC AI Monitor", "已完成系统指标采集和 Windows 托盘应用。",
            false, false, null, "claude-opus", 4, 4, "任务完成", null, null, true, "demo-claude");
        var codex = new AgentSnapshot(
            AgentKind.Codex, true, 184_000, 24_000, DateTime.Now,
            "windows-port", "已完成部署，四个模块全部通过构建：\n模块 | 状态\nUSB | 通过\nRenderer | 通过\nAgents | 通过",
            true, false, null, "gpt-5.6", 4, 6, "生成发布包与使用说明", 57, DateTime.Now.AddDays(5), true,
            "session-windows-port", 10_080);
        var codexSecond = new AgentSnapshot(
            AgentKind.Codex, true, 62_000, 8_400, DateTime.Now.AddMinutes(-40),
            "market-panel", "已完成行情显示验证。",
            false, false, null, "gpt-5.6", 4, 4, "任务完成", 57, DateTime.Now.AddDays(5), true,
            "session-market-panel", 10_080);
        var cursor = new AgentSnapshot(
            AgentKind.Cursor, true, 0, 0, DateTime.Now.AddMinutes(-30),
            "dashboard", "等待新的 Agent 消息…", false, false,
            null, "composer", null, null, null, null, null, false, "demo-cursor");
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
            new NewsArticle("OpenAI 发布面向开发者的新一代智能体工具", "前沿科技", 5, 4,
                DateTime.Now.AddMinutes(-12), "https://textradar.app/"),
            new NewsArticle("全球市场关注本周通胀与就业数据", "国际", 5, 6,
                DateTime.Now.AddMinutes(-28), "https://textradar.app/"),
            new NewsArticle("比特币 ETF 连续录得资金净流入", "WEB3", 4, 3,
                DateTime.Now.AddMinutes(-45), "https://textradar.app/"),
            new NewsArticle("澳洲市场等待最新利率路径与企业财报指引", "财经", 4, 5,
                DateTime.Now.AddHours(-1), "https://textradar.app/"),
            new NewsArticle("人工智能基础设施投资继续成为科技行业焦点", "科技", 3, 2,
                DateTime.Now.AddHours(-2), "https://textradar.app/"),
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
            StepText = "任务完成",
        };
        var agents = idle
            ? new AgentsSnapshot(
                AgentSnapshot.Missing(AgentKind.Claude),
                idleCodex,
                AgentSnapshot.Missing(AgentKind.Cursor),
                [idleCodex])
            : completed
                ? new AgentsSnapshot(claude, codex, cursor, [codex, completedSecondCodex])
                : new AgentsSnapshot(claude, codex, cursor, [codex, codexSecond]);
        return new DashboardSnapshot(system, agents,
            markets, news, weather);
    }
}
