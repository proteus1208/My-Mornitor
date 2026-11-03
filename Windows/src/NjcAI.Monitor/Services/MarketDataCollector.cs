using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class MarketDataCollector : IDisposable
{
    private const int MaxHistoryPoints = 24;
    private static readonly string[] StockSymbols =
        ["BGBL.AX", "NDQ.AX", "IBIT.AX", "IVV.AX"];
    private static readonly string[] MarketDisplaySymbols = ["USDT/AUD", "BTC", "ETH", "WLFI"];
    private const string OkxApiBase = "https://us.okx.com";
    private static readonly (string OkxInstrument, string? BinancePair, string Symbol)[] CryptoPairs =
    [
        ("USDT-AUD", null, "USDT/AUD"),
        ("BTC-USDT", "BTCUSDT", "BTC"),
        ("ETH-USDT", "ETHUSDT", "ETH"),
        ("WLFI-USDT", "WLFIUSDT", "WLFI"),
    ];

    private readonly HttpClient _http;
    private DateTime _nextStocksAt;
    private DateTime _nextCryptoAt;
    private DateTime _nextCryptoHistoryAt;
    private DateTime _nextOkxPrecisionAt;
    private DateTime _nextBinancePrecisionAt;
    private Dictionary<string, decimal> _okxPriceTicks =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, decimal> _binancePriceTicks =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, IReadOnlyList<decimal>> _cryptoHourlyHistory =
        new Dictionary<string, IReadOnlyList<decimal>>(StringComparer.OrdinalIgnoreCase);
    private readonly object _historyGate = new();
    private readonly Dictionary<string, List<decimal>> _priceHistory =
        new(StringComparer.OrdinalIgnoreCase);
    private MarketSnapshot _current = MarketSnapshot.Empty;

    public MarketSnapshot Current => Volatile.Read(ref _current);

    public MarketDataCollector(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NJC-AI-Monitor/0.5");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<MarketSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var stocks = Current.Stocks;
        var cryptos = Current.Cryptos;
        var errors = new List<string>();

        if (now >= _nextStocksAt)
        {
            _nextStocksAt = now.AddMinutes(1);
            var fresh = await FetchStocksAsync(cancellationToken).ConfigureAwait(false);
            if (fresh.Count > 0)
            {
                fresh = AttachHistory(fresh);
                stocks = MergeQuotes(StockSymbols.Select(SymbolWithoutSuffix), stocks, fresh);
            }
            else
            {
                errors.Add("ASX 延迟行情暂不可用");
            }
        }

        if (now >= _nextCryptoAt)
        {
            _nextCryptoAt = now.AddSeconds(5);
            Task<IReadOnlyDictionary<string, decimal>>? okxPrecisionTask = null;
            Task<IReadOnlyDictionary<string, decimal>>? binancePrecisionTask = null;
            Task<IReadOnlyDictionary<string, IReadOnlyList<decimal>>>? cryptoHistoryTask = null;
            if (now >= _nextOkxPrecisionAt)
            {
                _nextOkxPrecisionAt = now.AddHours(12);
                okxPrecisionTask = FetchOkxPriceTicksAsync(cancellationToken);
            }
            if (now >= _nextBinancePrecisionAt)
            {
                _nextBinancePrecisionAt = now.AddHours(12);
                binancePrecisionTask = FetchBinancePriceTicksAsync(cancellationToken);
            }
            if (now >= _nextCryptoHistoryAt)
            {
                _nextCryptoHistoryAt = now.AddMinutes(5);
                cryptoHistoryTask = FetchOkxHourlyHistoriesAsync(cancellationToken);
            }

            var cryptoTask = FetchCryptoAsync(cancellationToken);
            if (okxPrecisionTask is not null)
            {
                var freshTicks = await okxPrecisionTask.ConfigureAwait(false);
                if (freshTicks.Count > 0)
                {
                    _okxPriceTicks = MergeTicks(_okxPriceTicks, freshTicks);
                }
                else
                {
                    _nextOkxPrecisionAt = now.AddMinutes(5);
                }
            }
            if (binancePrecisionTask is not null)
            {
                var freshTicks = await binancePrecisionTask.ConfigureAwait(false);
                if (freshTicks.Count > 0)
                {
                    _binancePriceTicks = MergeTicks(_binancePriceTicks, freshTicks);
                }
                else
                {
                    _nextBinancePrecisionAt = now.AddMinutes(5);
                }
            }
            if (cryptoHistoryTask is not null)
            {
                var histories = await cryptoHistoryTask.ConfigureAwait(false);
                if (histories.Count > 0)
                {
                    _cryptoHourlyHistory = histories;
                }
                else
                {
                    _nextCryptoHistoryAt = now.AddMinutes(1);
                }
            }

            var fresh = ApplyCryptoHourlyHistory(
                ApplyPriceTicks(await cryptoTask.ConfigureAwait(false)));
            if (fresh.Count > 0)
            {
                fresh = AttachHistory(fresh);
                cryptos = MergeQuotes(MarketDisplaySymbols, cryptos, fresh);
            }
            else
            {
                _nextCryptoAt = now.AddSeconds(20);
                errors.Add("加密货币实时行情暂不可用");
            }
        }

        var snapshot = new MarketSnapshot(stocks, cryptos, now,
            errors.Count == 0 ? null : string.Join(" · ", errors));
        Volatile.Write(ref _current, snapshot);
        return snapshot;
    }

    private async Task<IReadOnlyList<MarketQuote>> FetchStocksAsync(CancellationToken cancellationToken)
    {
        var tasks = StockSymbols.Select(symbol => FetchYahooQuoteAsync(symbol, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(quote => quote is not null).Select(quote => quote!).ToArray();
    }

    private async Task<MarketQuote?> FetchYahooQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var encoded = Uri.EscapeDataString(symbol);
            var uri = $"https://query1.finance.yahoo.com/v8/finance/chart/{encoded}?interval=1h&range=5d";
            var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            return ParseYahooQuote(json, SymbolWithoutSuffix(symbol));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warn($"Yahoo market quote failed ({symbol}): {exception.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<MarketQuote>> FetchCryptoAsync(CancellationToken cancellationToken)
    {
        var okxTasks = CryptoPairs.Select(pair => FetchOkxQuoteAsync(pair, cancellationToken));
        var results = (await Task.WhenAll(okxTasks).ConfigureAwait(false))
            .Where(quote => quote is not null)
            .Select(quote => quote!)
            .ToList();

        var missing = CryptoPairs.Where(pair => results.All(quote => quote.Symbol != pair.Symbol)).ToArray();
        var binanceMissing = missing.Where(pair => !string.IsNullOrWhiteSpace(pair.BinancePair)).ToArray();
        if (binanceMissing.Length > 0)
        {
            var binanceTasks = binanceMissing.Select(pair => FetchBinanceQuoteAsync(pair, cancellationToken));
            foreach (var quote in await Task.WhenAll(binanceTasks).ConfigureAwait(false))
            {
                if (quote is not null)
                {
                    results.Add(quote);
                }
            }
        }

        var coinGeckoNeeded = CryptoPairs
            .Where(pair => pair.BinancePair is not null)
            .Any(pair => results.All(quote => quote.Symbol != pair.Symbol));
        if (coinGeckoNeeded)
        {
            try
            {
                var fallback = await FetchCoinGeckoAsync(cancellationToken).ConfigureAwait(false);
                foreach (var quote in fallback)
                {
                    if (results.All(existing => existing.Symbol != quote.Symbol))
                    {
                        results.Add(quote);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Warn($"CoinGecko fallback failed: {exception.Message}");
            }
        }
        return results;
    }

    private async Task<MarketQuote?> FetchOkxQuoteAsync(
        (string OkxInstrument, string? BinancePair, string Symbol) pair,
        CancellationToken cancellationToken)
    {
        try
        {
            var instrument = Uri.EscapeDataString(pair.OkxInstrument);
            var uri = $"{OkxApiBase}/api/v5/market/ticker?instId={instrument}";
            var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            return ParseOkxQuote(json, pair.Symbol);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warn($"OKX market quote failed ({pair.OkxInstrument}): {exception.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<decimal>>>
        FetchOkxHourlyHistoriesAsync(CancellationToken cancellationToken)
    {
        var tasks = CryptoPairs.Select(async pair =>
            (pair.Symbol, History: await FetchOkxHourlyHistoryAsync(pair, cancellationToken)
                .ConfigureAwait(false)));
        var histories = new Dictionary<string, IReadOnlyList<decimal>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var result in await Task.WhenAll(tasks).ConfigureAwait(false))
        {
            if (result.History.Count > 1)
            {
                histories[result.Symbol] = result.History;
            }
        }
        return histories;
    }

    private async Task<IReadOnlyList<decimal>> FetchOkxHourlyHistoryAsync(
        (string OkxInstrument, string? BinancePair, string Symbol) pair,
        CancellationToken cancellationToken)
    {
        try
        {
            var instrument = Uri.EscapeDataString(pair.OkxInstrument);
            var uri = $"{OkxApiBase}/api/v5/market/candles" +
                      $"?instId={instrument}&bar=1H&limit={MaxHistoryPoints}";
            var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            return ParseOkxCandles(json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warn($"OKX hourly candles failed ({pair.OkxInstrument}): {exception.Message}");
            return [];
        }
    }

    private async Task<MarketQuote?> FetchBinanceQuoteAsync(
        (string OkxInstrument, string? BinancePair, string Symbol) pair,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = $"https://data-api.binance.vision/api/v3/ticker/24hr?symbol={pair.BinancePair!}";
            var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            return ParseBinanceQuote(json, pair.Symbol);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warn($"Binance fallback quote failed ({pair.BinancePair}): {exception.Message}");
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, decimal>> FetchOkxPriceTicksAsync(
        CancellationToken cancellationToken)
    {
        var tasks = CryptoPairs.Select(async pair =>
        {
            try
            {
                var instrument = Uri.EscapeDataString(pair.OkxInstrument);
                var uri = $"{OkxApiBase}/api/v5/public/instruments" +
                          $"?instType=SPOT&instId={instrument}";
                var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
                return ParseOkxPriceTicks(json);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Warn($"OKX price precision failed ({pair.OkxInstrument}): {exception.Message}");
                return (IReadOnlyDictionary<string, decimal>)new Dictionary<string, decimal>();
            }
        });
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in await Task.WhenAll(tasks).ConfigureAwait(false))
        {
            foreach (var (symbol, tickSize) in result)
            {
                ticks[symbol] = tickSize;
            }
        }
        return ticks;
    }

    private async Task<IReadOnlyDictionary<string, decimal>> FetchBinancePriceTicksAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var symbols = JsonSerializer.Serialize(CryptoPairs
                .Where(pair => pair.BinancePair is not null)
                .Select(pair => pair.BinancePair!));
            var uri = "https://data-api.binance.vision/api/v3/exchangeInfo?symbols=" +
                      Uri.EscapeDataString(symbols);
            var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            return ParseBinancePriceTicks(json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Warn($"Binance fallback precision failed: {exception.Message}");
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlyList<MarketQuote>> FetchCoinGeckoAsync(CancellationToken cancellationToken)
    {
        const string uri = "https://api.coingecko.com/api/v3/simple/price" +
                           "?ids=bitcoin,ethereum,world-liberty-financial" +
                           "&vs_currencies=usd&include_24hr_change=true&include_last_updated_at=true";
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return ParseCoinGeckoQuotes(json);
    }

    internal static MarketQuote ParseYahooQuote(string json, string displaySymbol)
    {
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("chart").GetProperty("result")[0];
        var meta = result.GetProperty("meta");
        var price = ReadDecimal(meta, "regularMarketPrice");
        var previous = TryReadDecimal(meta, "chartPreviousClose") ??
                       TryReadDecimal(meta, "previousClose") ?? price;
        var change = previous == 0 ? 0 : (price - previous) * 100 / previous;
        var updated = ReadUnixTime(meta, "regularMarketTime") ?? DateTime.Now;
        var open = IsInsideRegularTradingPeriod(meta, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return new MarketQuote(displaySymbol, price, change, "AUD", updated, false, open,
            Source: "Yahoo", History: ReadYahooHistory(result));
    }

    internal static MarketQuote ParseOkxQuote(string json, string displaySymbol)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.GetProperty("data")[0];
        var price = ReadDecimal(root, "last");
        var open24h = TryReadDecimal(root, "open24h") ?? price;
        var change = open24h == 0 ? 0 : (price - open24h) * 100 / open24h;
        var updated = ReadUnixTimeMilliseconds(root, "ts") ?? DateTime.Now;
        var currency = displaySymbol == "USDT/AUD" ? "AUD" : "USDT";
        return new MarketQuote(displaySymbol, price, change, currency, updated, true,
            Source: "OKX");
    }

    internal static IReadOnlyList<decimal> ParseOkxCandles(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candles = new List<(long Timestamp, decimal Close)>();
        foreach (var candle in data.EnumerateArray())
        {
            if (candle.ValueKind != JsonValueKind.Array || candle.GetArrayLength() < 5)
            {
                continue;
            }
            var timestampText = candle[0].ValueKind == JsonValueKind.String
                ? candle[0].GetString()
                : candle[0].GetRawText();
            var closeText = candle[4].ValueKind == JsonValueKind.String
                ? candle[4].GetString()
                : candle[4].GetRawText();
            if (long.TryParse(timestampText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var timestamp) &&
                decimal.TryParse(closeText, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var close) && close > 0)
            {
                candles.Add((timestamp, close));
            }
        }
        return candles
            .OrderBy(candle => candle.Timestamp)
            .Select(candle => candle.Close)
            .TakeLast(MaxHistoryPoints)
            .ToArray();
    }

    internal static MarketQuote ParseBinanceQuote(string json, string displaySymbol)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var price = ReadDecimal(root, "lastPrice");
        var change = ReadDecimal(root, "priceChangePercent");
        var updated = ReadUnixTimeMilliseconds(root, "closeTime") ?? DateTime.Now;
        return new MarketQuote(displaySymbol, price, change, "USDT", updated, true,
            Source: "Binance");
    }

    internal static IReadOnlyList<MarketQuote> ParseCoinGeckoQuotes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var mappings = new[]
        {
            (Id: "bitcoin", Symbol: "BTC"),
            (Id: "ethereum", Symbol: "ETH"),
            (Id: "world-liberty-financial", Symbol: "WLFI"),
        };
        var quotes = new List<MarketQuote>();
        foreach (var mapping in mappings)
        {
            if (!document.RootElement.TryGetProperty(mapping.Id, out var item))
            {
                continue;
            }
            var price = ReadDecimal(item, "usd");
            var change = TryReadDecimal(item, "usd_24h_change") ?? 0;
            var updated = ReadUnixTime(item, "last_updated_at") ?? DateTime.Now;
            quotes.Add(new MarketQuote(mapping.Symbol, price, change, "USD", updated, true,
                Source: "CoinGecko"));
        }
        return quotes;
    }

    internal static IReadOnlyDictionary<string, decimal> ParseOkxPriceTicks(string json)
    {
        using var document = JsonDocument.Parse(json);
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("data", out var instruments) ||
            instruments.ValueKind != JsonValueKind.Array)
        {
            return ticks;
        }

        foreach (var instrument in instruments.EnumerateArray())
        {
            var instrumentId = instrument.TryGetProperty("instId", out var id)
                ? id.GetString()
                : null;
            var displaySymbol = CryptoPairs.FirstOrDefault(item =>
                string.Equals(item.OkxInstrument, instrumentId, StringComparison.OrdinalIgnoreCase)).Symbol;
            var state = instrument.TryGetProperty("state", out var stateElement)
                ? stateElement.GetString()
                : "live";
            var tickSize = TryReadDecimal(instrument, "tickSz");
            if (!string.IsNullOrWhiteSpace(displaySymbol) && state == "live" && tickSize > 0)
            {
                ticks[displaySymbol] = tickSize.Value;
            }
        }
        return ticks;
    }

    internal static IReadOnlyDictionary<string, decimal> ParseBinancePriceTicks(string json)
    {
        using var document = JsonDocument.Parse(json);
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("symbols", out var symbols) ||
            symbols.ValueKind != JsonValueKind.Array)
        {
            return ticks;
        }

        foreach (var instrument in symbols.EnumerateArray())
        {
            var pair = instrument.TryGetProperty("symbol", out var symbolElement)
                ? symbolElement.GetString()
                : null;
            var displaySymbol = CryptoPairs.FirstOrDefault(item =>
                string.Equals(item.BinancePair, pair, StringComparison.OrdinalIgnoreCase)).Symbol;
            if (string.IsNullOrWhiteSpace(displaySymbol) ||
                !instrument.TryGetProperty("filters", out var filters) ||
                filters.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var filter in filters.EnumerateArray())
            {
                if (!filter.TryGetProperty("filterType", out var type) ||
                    type.GetString() != "PRICE_FILTER")
                {
                    continue;
                }
                var tickSize = TryReadDecimal(filter, "tickSize");
                if (tickSize > 0)
                {
                    ticks[displaySymbol] = tickSize.Value;
                }
                break;
            }
        }
        return ticks;
    }

    private IReadOnlyList<MarketQuote> ApplyPriceTicks(IReadOnlyList<MarketQuote> quotes) =>
        quotes.Select(quote =>
            {
                decimal tickSize;
                var found = quote.Source switch
                {
                    "OKX" => _okxPriceTicks.TryGetValue(quote.Symbol, out tickSize),
                    "Binance" => _binancePriceTicks.TryGetValue(quote.Symbol, out tickSize),
                    _ => _okxPriceTicks.TryGetValue(quote.Symbol, out tickSize) ||
                         _binancePriceTicks.TryGetValue(quote.Symbol, out tickSize),
                };
                return found ? quote with { PriceTickSize = tickSize } : quote;
            })
            .ToArray();

    private IReadOnlyList<MarketQuote> ApplyCryptoHourlyHistory(
        IReadOnlyList<MarketQuote> quotes) =>
        quotes.Select(quote =>
            _cryptoHourlyHistory.TryGetValue(quote.Symbol, out var history) && history.Count > 1
                ? quote with { History = history }
                : quote)
            .ToArray();

    private IReadOnlyList<MarketQuote> AttachHistory(IReadOnlyList<MarketQuote> quotes)
    {
        lock (_historyGate)
        {
            return quotes.Select(quote =>
                {
                    if (!_priceHistory.TryGetValue(quote.Symbol, out var history))
                    {
                        history = [];
                        _priceHistory[quote.Symbol] = history;
                    }

                    var supplied = quote.History?
                        .Where(price => price > 0)
                        .TakeLast(MaxHistoryPoints)
                        .ToArray() ?? [];
                    if (supplied.Length > 1)
                    {
                        history.Clear();
                        history.AddRange(supplied);
                        if (history[^1] != quote.Price)
                        {
                            history.Add(quote.Price);
                        }
                    }
                    else
                    {
                        history.Add(quote.Price);
                    }

                    if (history.Count > MaxHistoryPoints)
                    {
                        history.RemoveRange(0, history.Count - MaxHistoryPoints);
                    }
                    return quote with { History = history.ToArray() };
                })
                .ToArray();
        }
    }

    private static IReadOnlyList<decimal> ReadYahooHistory(JsonElement result)
    {
        if (!result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quoteSets) ||
            quoteSets.ValueKind != JsonValueKind.Array ||
            quoteSets.GetArrayLength() == 0 ||
            !quoteSets[0].TryGetProperty("close", out var closes) ||
            closes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return closes.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Number)
            .Select(value => value.TryGetDecimal(out var price) ? price : 0)
            .Where(price => price > 0)
            .TakeLast(MaxHistoryPoints)
            .ToArray();
    }

    private static Dictionary<string, decimal> MergeTicks(
        IReadOnlyDictionary<string, decimal> existing,
        IReadOnlyDictionary<string, decimal> fresh)
    {
        var merged = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, tickSize) in existing)
        {
            merged[symbol] = tickSize;
        }
        foreach (var (symbol, tickSize) in fresh)
        {
            merged[symbol] = tickSize;
        }
        return merged;
    }

    internal static IReadOnlyList<MarketQuote> MergeQuotes(
        IEnumerable<string> expected,
        IReadOnlyList<MarketQuote> existing,
        IReadOnlyList<MarketQuote> fresh)
    {
        return expected.Select(symbol =>
            {
                var current = existing.FirstOrDefault(quote => quote.Symbol == symbol);
                var candidate = fresh.FirstOrDefault(quote => quote.Symbol == symbol);
                if (candidate is null)
                {
                    return current;
                }
                return current is null || candidate.UpdatedAt >= current.UpdatedAt ? candidate : current;
            })
            .Where(quote => quote is not null)
            .Select(quote => quote!)
            .ToArray();
    }

    private static decimal ReadDecimal(JsonElement element, string name) =>
        TryReadDecimal(element, name) ?? throw new InvalidDataException($"Missing market field: {name}");

    private static decimal? TryReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static DateTime? ReadUnixTime(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetInt64(out var epoch))
        {
            return null;
        }
        return DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime;
    }

    private static DateTime? ReadUnixTimeMilliseconds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }
        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var number) => number,
            _ => (long?)null,
        };
        return parsed is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(parsed.Value).LocalDateTime;
    }

    private static bool IsInsideRegularTradingPeriod(JsonElement meta, long now)
    {
        try
        {
            var regular = meta.GetProperty("currentTradingPeriod").GetProperty("regular");
            return now >= regular.GetProperty("start").GetInt64() &&
                   now <= regular.GetProperty("end").GetInt64();
        }
        catch
        {
            return false;
        }
    }

    private static string SymbolWithoutSuffix(string value) =>
        value.EndsWith(".AX", StringComparison.OrdinalIgnoreCase) ? value[..^3] : value;

    public void Dispose() => _http.Dispose();
}
