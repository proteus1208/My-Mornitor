namespace NjcAI.Monitor.Core;

internal enum AgentKind
{
    Claude,
    Codex,
    Cursor,
}

internal sealed record SystemSnapshot(
    DateTime CapturedAt,
    double CpuPercent,
    IReadOnlyList<double> PerCore,
    double? CpuTemperature,
    double? GpuTemperature,
    double GpuPercent,
    ulong TotalMemory,
    ulong UsedMemory,
    ulong AvailableMemory,
    long UptimeSeconds,
    int ProcessCount,
    double NetworkReceiveBytesPerSecond,
    double NetworkSendBytesPerSecond,
    double DiskReadBytesPerSecond,
    double DiskWriteBytesPerSecond)
{
    public double MemoryPercent => TotalMemory == 0 ? 0 : UsedMemory * 100d / TotalMemory;

    public static SystemSnapshot Empty => new(
        DateTime.Now, 0, [], null, null, 0, 0, 0, 0,
        Environment.TickCount64 / 1000, 0, 0, 0, 0, 0);
}

internal sealed record AgentSnapshot(
    AgentKind Kind,
    bool Available,
    ulong TodayInputTokens,
    ulong TodayOutputTokens,
    DateTime? LastActive,
    string? Project,
    string? Activity,
    bool IsWorking,
    bool NeedsAttention,
    string? WaitingFor,
    string? Model,
    int? StepCurrent,
    int? StepTotal,
    string? StepText,
    double? QuotaUsedPercent,
    DateTime? QuotaResetsAt,
    bool HasTokenUsage = true,
    string? SessionId = null,
    int? QuotaWindowMinutes = null,
    DateTime? CompletedAt = null,
    string? Workspace = null)
{
    public ulong TodayTotalTokens => TodayInputTokens + TodayOutputTokens;
    public string Identity => $"{Kind}:{SessionId ?? Project ?? "unknown"}";

    public static AgentSnapshot Missing(AgentKind kind) => new(
        kind, false, 0, 0, null, null, null, false, false,
        null, null, null, null, null, null, null, kind != AgentKind.Cursor);
}

internal sealed record AgentCompletionAlert(
    string Identity,
    AgentKind Kind,
    string Project,
    string Activity,
    DateTime CompletedAt);

internal sealed record AgentsSnapshot(
    AgentSnapshot Claude,
    AgentSnapshot Codex,
    AgentSnapshot Cursor,
    IReadOnlyList<AgentSnapshot>? CodexSessions = null)
{
    public IReadOnlyList<AgentSnapshot> Sessions
    {
        get
        {
            var sessions = new List<AgentSnapshot>();
            if (CodexSessions is { Count: > 0 })
            {
                sessions.AddRange(CodexSessions.Where(session => session.LastActive is not null));
            }
            else if (Codex.LastActive is not null)
            {
                sessions.Add(Codex);
            }
            if (Claude.LastActive is not null)
            {
                sessions.Add(Claude);
            }
            if (Cursor.LastActive is not null)
            {
                sessions.Add(Cursor);
            }
            return sessions
                .GroupBy(session => session.Identity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }
    }

    public AgentSnapshot Get(AgentKind kind) => kind switch
    {
        AgentKind.Claude => Claude,
        AgentKind.Codex => Codex,
        AgentKind.Cursor => Cursor,
        _ => Codex,
    };

    public static AgentsSnapshot Empty => new(
        AgentSnapshot.Missing(AgentKind.Claude),
        AgentSnapshot.Missing(AgentKind.Codex),
        AgentSnapshot.Missing(AgentKind.Cursor),
        []);
}

internal sealed record MarketQuote(
    string Symbol,
    decimal Price,
    decimal ChangePercent,
    string Currency,
    DateTime UpdatedAt,
    bool IsLive,
    bool MarketOpen = true,
    decimal? PriceTickSize = null,
    string Source = "",
    IReadOnlyList<decimal>? History = null);

internal sealed record MarketSnapshot(
    IReadOnlyList<MarketQuote> Stocks,
    IReadOnlyList<MarketQuote> Cryptos,
    DateTime? LastAttempt,
    string? Error)
{
    public static MarketSnapshot Empty => new([], [], null, "Connecting to free market data");
}

internal sealed record NewsArticle(
    string Title,
    string Category,
    int Importance,
    int SourceCount,
    DateTime PublishedAt,
    string Url,
    string Source = "");

internal sealed record NewsSnapshot(
    IReadOnlyList<NewsArticle> Articles,
    DateTime? UpdatedAt,
    string? Error)
{
    public static NewsSnapshot Empty => new([], null, "Connecting to English news sources");
}

internal sealed record WeatherHour(
    DateTime Time,
    double Temperature,
    int PrecipitationProbability,
    double? WindSpeed = null,
    int? WindDirection = null);

internal sealed record WeatherSnapshot(
    string Location,
    double? Temperature,
    double? ApparentTemperature,
    int? RelativeHumidity,
    double? WindSpeed,
    int? WindDirection,
    double? Precipitation,
    int? WeatherCode,
    double? TodayMaximum,
    double? TodayMinimum,
    int? TodayRainProbability,
    IReadOnlyList<WeatherHour> Hours,
    DateTime? UpdatedAt,
    string? Error)
{
    public static WeatherSnapshot Empty(string location = "Sydney") => new(
        location, null, null, null, null, null, null, null,
        null, null, null, [], null, "Connecting to weather service");
}

internal enum DashboardPanelKind
{
    Agent,
    Markets,
    News,
    Forecast,
    AiQuota,
}

internal sealed record DashboardPanel(
    DashboardPanelKind Kind,
    AgentSnapshot? Agent = null);

internal sealed record DashboardSnapshot(
    SystemSnapshot System,
    AgentsSnapshot Agents,
    MarketSnapshot? Markets = null,
    NewsSnapshot? News = null,
    WeatherSnapshot? Weather = null);

internal sealed record DeviceInfo(
    int Pm,
    int Sub,
    int Fbl,
    int Width,
    int Height,
    bool UsesJpeg,
    bool NeedsRotation,
    int ProductId);
