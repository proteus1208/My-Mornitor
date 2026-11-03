using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class NewsDataCollector : IDisposable
{
    private const string GdeltUrl =
        "https://api.gdeltproject.org/api/v2/doc/doc?query=(economy%20OR%20markets%20OR%20business%20OR%20geopolitics%20OR%20Australia)&mode=artlist&maxrecords=30&timespan=24h&sort=datedesc&format=jsonfeed";
    private const string HackerNewsTopUrl = "https://hacker-news.firebaseio.com/v0/topstories.json";
    private const string HackerNewsItemUrl = "https://hacker-news.firebaseio.com/v0/item/{0}.json";
    private const string CoinDeskRssUrl = "https://www.coindesk.com/arc/outboundfeeds/rss/";
    private static readonly string CachePath = Path.Combine(AppSettings.DataDirectory, "news-cache-en.json");

    private readonly HttpClient _http;
    private NewsSnapshot _current;

    public NewsSnapshot Current => Volatile.Read(ref _current);

    public NewsDataCollector(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NJC-AI-Monitor/0.2");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
        _current = LoadCache() ?? NewsSnapshot.Empty;
    }

    public async Task<NewsSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var tasks = new[]
        {
            FetchSourceAsync("GDELT", FetchGdeltAsync, cancellationToken),
            FetchSourceAsync("Hacker News", FetchHackerNewsAsync, cancellationToken),
            FetchSourceAsync("CoinDesk", FetchCoinDeskAsync, cancellationToken),
        };
        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        var articles = Balance(batches.Select(batch => batch.Articles));
        var errors = batches
            .Where(batch => !string.IsNullOrWhiteSpace(batch.Error))
            .Select(batch => $"{batch.Name}: {batch.Error}")
            .ToArray();

        NewsSnapshot snapshot;
        if (articles.Count > 0)
        {
            snapshot = new NewsSnapshot(articles, DateTime.Now, null);
            SaveCache(snapshot);
            if (errors.Length > 0)
            {
                Log.Warn($"Some English news sources failed: {string.Join(" · ", errors)}");
            }
        }
        else
        {
            var previous = Current;
            snapshot = new NewsSnapshot(
                previous.Articles,
                previous.UpdatedAt,
                "English news sources are unavailable");
            Log.Warn($"English news update failed: {string.Join(" · ", errors)}");
        }
        Volatile.Write(ref _current, snapshot);
        return snapshot;
    }

    private async Task<SourceBatch> FetchSourceAsync(
        string name,
        Func<CancellationToken, Task<IReadOnlyList<NewsArticle>>> fetch,
        CancellationToken cancellationToken)
    {
        try
        {
            return new SourceBatch(name, await fetch(cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SourceBatch(name, [], exception.Message);
        }
    }

    private async Task<IReadOnlyList<NewsArticle>> FetchGdeltAsync(CancellationToken cancellationToken)
    {
        var json = await _http.GetStringAsync(GdeltUrl, cancellationToken).ConfigureAwait(false);
        return ParseGdeltJsonFeed(json);
    }

    private async Task<IReadOnlyList<NewsArticle>> FetchHackerNewsAsync(CancellationToken cancellationToken)
    {
        var json = await _http.GetStringAsync(HackerNewsTopUrl, cancellationToken).ConfigureAwait(false);
        var ids = JsonSerializer.Deserialize<long[]>(json) ?? [];
        var tasks = ids.Take(14).Select(id => FetchHackerNewsItemAsync(id, cancellationToken));
        var articles = await Task.WhenAll(tasks).ConfigureAwait(false);
        return articles
            .Where(article => article is not null)
            .Cast<NewsArticle>()
            .OrderByDescending(article => article.Importance)
            .ThenByDescending(article => article.PublishedAt)
            .Take(8)
            .ToArray();
    }

    private async Task<NewsArticle?> FetchHackerNewsItemAsync(long id, CancellationToken cancellationToken)
    {
        var url = string.Format(CultureInfo.InvariantCulture, HackerNewsItemUrl, id);
        var json = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseHackerNewsItem(json);
    }

    private async Task<IReadOnlyList<NewsArticle>> FetchCoinDeskAsync(CancellationToken cancellationToken)
    {
        var xml = await _http.GetStringAsync(CoinDeskRssUrl, cancellationToken).ConfigureAwait(false);
        return ParseRss(xml, "CoinDesk", "Crypto");
    }

    internal static IReadOnlyList<NewsArticle> ParseGdeltJsonFeed(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<NewsArticle>();
        var position = 0;
        foreach (var item in items.EnumerateArray())
        {
            var title = CleanText(ReadString(item, "title") ?? string.Empty);
            var url = ReadString(item, "url", "id");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url) ||
                !IsLikelyEnglish(title))
            {
                continue;
            }
            var published = ReadDate(item, "date_published", "date_modified") ??
                            DateTime.Now.AddMinutes(-position * 3);
            results.Add(new NewsArticle(
                title,
                ClassifyGdelt(title),
                4,
                1,
                published,
                url,
                "GDELT"));
            position++;
        }
        return Deduplicate(results).Take(10).ToArray();
    }

    internal static NewsArticle? ParseHackerNewsItem(string json)
    {
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement;
        var type = ReadString(item, "type");
        var title = CleanText(ReadString(item, "title") ?? string.Empty);
        if (!string.Equals(type, "story", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(title))
        {
            return null;
        }
        var id = ReadLong(item, "id") ?? 0;
        var score = ReadLong(item, "score") ?? 0;
        var timestamp = ReadLong(item, "time") ?? DateTimeOffset.Now.ToUnixTimeSeconds();
        var url = ReadString(item, "url") ?? $"https://news.ycombinator.com/item?id={id}";
        var importance = score switch
        {
            >= 300 => 5,
            >= 100 => 4,
            _ => 3,
        };
        return new NewsArticle(
            title,
            "AI / Tech",
            importance,
            1,
            DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime,
            url,
            "Hacker News");
    }

    internal static IReadOnlyList<NewsArticle> ParseRss(
        string xml,
        string source,
        string defaultCategory)
    {
        var document = XDocument.Parse(xml);
        var results = document.Descendants("item")
            .Select(item =>
            {
                var title = CleanText(item.Element("title")?.Value ?? string.Empty);
                var url = item.Element("link")?.Value.Trim() ?? string.Empty;
                var category = CleanText(item.Elements("category").FirstOrDefault()?.Value ?? defaultCategory);
                var publishedText = item.Element("pubDate")?.Value;
                var published = DateTimeOffset.TryParse(publishedText, out var parsed)
                    ? parsed.LocalDateTime
                    : DateTime.Now;
                return new NewsArticle(title, category, 4, 1, published, url, source);
            })
            .Where(article => !string.IsNullOrWhiteSpace(article.Title) &&
                              !string.IsNullOrWhiteSpace(article.Url) &&
                              IsLikelyEnglish(article.Title));
        return Deduplicate(results).Take(8).ToArray();
    }

    private static IReadOnlyList<NewsArticle> Balance(
        IEnumerable<IReadOnlyList<NewsArticle>> sources)
    {
        var queues = sources
            .Where(source => source.Count > 0)
            .Select(source => source
                .OrderByDescending(article => article.PublishedAt)
                .Take(8)
                .ToArray())
            .ToArray();
        var output = new List<NewsArticle>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; output.Count < 20 && queues.Any(queue => index < queue.Length); index++)
        {
            foreach (var queue in queues)
            {
                if (index >= queue.Length || !seenUrls.Add(queue[index].Url))
                {
                    continue;
                }
                output.Add(queue[index]);
                if (output.Count == 20)
                {
                    break;
                }
            }
        }
        return output;
    }

    private static IEnumerable<NewsArticle> Deduplicate(IEnumerable<NewsArticle> articles) => articles
        .GroupBy(article => article.Url, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First());

    private static string ClassifyGdelt(string title)
    {
        var value = title.ToLowerInvariant();
        string[] marketTerms =
        [
            "market", "stock", "shares", "economy", "economic", "inflation",
            "rates", "central bank", "earnings", "investor", "trade",
        ];
        return marketTerms.Any(value.Contains) ? "Markets" : "World";
    }

    private static bool IsLikelyEnglish(string value)
    {
        if (value.Any(character => character is >= '\u3400' and <= '\u9fff'))
        {
            return false;
        }
        var letters = value.Count(char.IsLetter);
        if (letters == 0)
        {
            return false;
        }
        var latin = value.Count(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        return latin / (double)letters >= 0.82;
    }

    private static string CleanText(string value)
    {
        var withoutTags = Regex.Replace(value, "<[^>]+>", string.Empty);
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private static string? ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }

    private static long? ReadLong(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out var number))
            {
                return number;
            }
        }
        return null;
    }

    private static DateTime? ReadDate(JsonElement item, params string[] names)
    {
        var value = ReadString(item, names);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.LocalDateTime : null;
    }

    private static NewsSnapshot? LoadCache()
    {
        try
        {
            return File.Exists(CachePath)
                ? JsonSerializer.Deserialize<NewsSnapshot>(File.ReadAllText(CachePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(NewsSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception exception)
        {
            Log.Warn($"Unable to save English news cache: {exception.Message}");
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record SourceBatch(
        string Name,
        IReadOnlyList<NewsArticle> Articles,
        string? Error);
}
