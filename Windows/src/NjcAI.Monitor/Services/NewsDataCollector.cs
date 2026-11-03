using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class NewsDataCollector : IDisposable
{
    private const string BaseUrl = "https://textradar.app/";
    private const string DisplayApiUrl = "https://textradar.app/api/display.json?limit=20";
    private static readonly string CachePath = Path.Combine(AppSettings.DataDirectory, "news-cache.json");

    private readonly HttpClient _http;
    private NewsSnapshot _current;

    public NewsSnapshot Current => Volatile.Read(ref _current);

    public NewsDataCollector(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NJC-AI-Monitor/0.5");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        _current = LoadCache() ?? NewsSnapshot.Empty;
    }

    public async Task<NewsSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        IReadOnlyList<NewsArticle> articles = [];
        try
        {
            var json = await _http.GetStringAsync(DisplayApiUrl, cancellationToken).ConfigureAwait(false);
            articles = ParseJson(json, BaseUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add($"JSON: {exception.Message}");
        }

        if (articles.Count == 0)
        {
            try
            {
                var html = await _http.GetStringAsync(BaseUrl, cancellationToken).ConfigureAwait(false);
                articles = ParseHtml(html, BaseUrl);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"HTML: {exception.Message}");
            }
        }

        NewsSnapshot snapshot;
        if (articles.Count > 0)
        {
            snapshot = new NewsSnapshot(articles, DateTime.Now, null);
            SaveCache(snapshot);
        }
        else
        {
            var previous = Current;
            var message = errors.Count == 0 ? "TextRadar 暂无新闻" : "TextRadar 暂不可用";
            snapshot = new NewsSnapshot(previous.Articles, previous.UpdatedAt, message);
            Log.Warn($"TextRadar update failed: {string.Join(" · ", errors)}");
        }
        Volatile.Write(ref _current, snapshot);
        return snapshot;
    }

    internal static IReadOnlyList<NewsArticle> ParseJson(string json, string baseUrl = BaseUrl)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement items;
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root;
        }
        else if (!TryArray(root, out items, "items", "articles", "headlines", "news"))
        {
            return [];
        }

        var results = new List<NewsArticle>();
        foreach (var item in items.EnumerateArray())
        {
            var title = ReadString(item, "title", "headline", "name");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }
            var category = ReadString(item, "category", "cat", "section") ?? "热点";
            var importance = ReadInt(item, "importance", "stars", "rating", "score") ?? 3;
            var sourceCount = ReadInt(item, "source_count", "sourceCount", "sources") ?? 1;
            var published = ReadDate(item, "published_at", "publishedAt", "time", "date") ?? DateTime.Now;
            var url = AbsoluteUrl(ReadString(item, "url", "link", "href") ?? BaseUrl, baseUrl);
            results.Add(new NewsArticle(
                CleanText(title), CleanText(category), Math.Clamp(importance, 1, 5),
                Math.Max(1, sourceCount), published, url));
        }
        return Rank(results);
    }

    internal static IReadOnlyList<NewsArticle> ParseHtml(string html, string baseUrl = BaseUrl)
    {
        var results = new List<NewsArticle>();
        foreach (Match article in Regex.Matches(html, @"<article\s+class=""lede\b(?<body>.*?)</article>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var body = article.Groups["body"].Value;
            var title = Capture(body, @"<a\s+class=""lede-title""\s+href=""(?<url>[^""]+)""[^>]*>(?<value>.*?)</a>");
            if (title.Value is null)
            {
                continue;
            }
            var category = CaptureValue(body, @"<span\s+class=""lede-cat[^""]*""[^>]*>(?<value>.*?)</span>") ?? "热点";
            var stars = CaptureInt(body, @"data-stars=""(?<value>\d+)""") ?? 5;
            var sources = CaptureInt(body, @"<span\s+class=""lede-srcct"">(?<value>\d+)源</span>") ?? 1;
            var published = CaptureDate(body) ?? DateTime.Now;
            results.Add(new NewsArticle(CleanText(title.Value), CleanText(category), stars, sources,
                published, AbsoluteUrl(title.Url ?? BaseUrl, baseUrl)));
        }

        foreach (Match item in Regex.Matches(html, @"<li\s+class=""cluster\b(?<body>.*?)</li>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var body = item.Groups["body"].Value;
            var title = Capture(body, @"<a\s+class=""cl-title""\s+href=""(?<url>[^""]+)""[^>]*>(?<value>.*?)</a>");
            if (title.Value is null)
            {
                continue;
            }
            var category = CaptureValue(body, @"<span\s+class=""cl-cat""[^>]*>(?<value>.*?)</span>") ?? "热点";
            var stars = CaptureInt(body, @"class=""cl-stars""\s+data-stars=""(?<value>\d+)""") ?? 3;
            var sources = CaptureInt(body, @"(?<value>\d+)源") ?? 1;
            var published = CaptureDate(body) ?? DateTime.Now;
            results.Add(new NewsArticle(CleanText(title.Value), CleanText(category), stars, sources,
                published, AbsoluteUrl(title.Url ?? BaseUrl, baseUrl)));
        }
        return Rank(results);
    }

    private static IReadOnlyList<NewsArticle> Rank(IEnumerable<NewsArticle> articles) => articles
        .Where(article => !string.IsNullOrWhiteSpace(article.Title))
        .GroupBy(article => article.Url, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderByDescending(article => article.Importance)
        .ThenByDescending(article => article.PublishedAt)
        .Take(18)
        .ToArray();

    private static (string? Value, string? Url) Capture(string html, string pattern)
    {
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success
            ? (match.Groups["value"].Value, match.Groups["url"].Value)
            : (null, null);
    }

    private static string? CaptureValue(string html, string pattern)
    {
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static int? CaptureInt(string html, string pattern) =>
        int.TryParse(CaptureValue(html, pattern), out var value) ? value : null;

    private static DateTime? CaptureDate(string html)
    {
        var value = CaptureValue(html, @"<time\s+datetime=""(?<value>[^""]+)""");
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.LocalDateTime : null;
    }

    private static string CleanText(string value)
    {
        var withoutTags = Regex.Replace(value, "<[^>]+>", string.Empty);
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private static string AbsoluteUrl(string value, string baseUrl) =>
        Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(new Uri(baseUrl), value).ToString();

    private static bool TryArray(JsonElement root, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }
        array = default;
        return false;
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

    private static int? ReadInt(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value))
            {
                continue;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.GetArrayLength();
            }
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
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
            Log.Warn($"Unable to save news cache: {exception.Message}");
        }
    }

    public void Dispose() => _http.Dispose();
}
