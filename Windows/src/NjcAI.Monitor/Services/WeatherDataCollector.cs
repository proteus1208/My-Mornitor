using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class WeatherDataCollector : IDisposable
{
    private static readonly string CachePath = Path.Combine(AppSettings.DataDirectory, "weather-cache.json");
    private readonly HttpClient _http;
    private WeatherSnapshot _current;

    public WeatherSnapshot Current => Volatile.Read(ref _current);

    public WeatherDataCollector(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NJC-AI-Monitor/0.5");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _current = LoadCache() ?? WeatherSnapshot.Empty();
    }

    public async Task<WeatherSnapshot> CollectAsync(
        string location,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var lat = latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("0.####", CultureInfo.InvariantCulture);
        var uri = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={lat}&longitude={lon}" +
                  "&current=temperature_2m,relative_humidity_2m,apparent_temperature," +
                  "precipitation,weather_code,wind_speed_10m,wind_direction_10m" +
                  "&hourly=temperature_2m,precipitation_probability,wind_speed_10m,wind_direction_10m" +
                  "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  "&timezone=auto&forecast_days=3";
        try
        {
            var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            var snapshot = ParseJson(json, location);
            Volatile.Write(ref _current, snapshot);
            SaveCache(snapshot);
            return snapshot;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var previous = Current;
            var snapshot = previous with
            {
                Location = location,
                Error = "天气数据暂不可用",
            };
            Volatile.Write(ref _current, snapshot);
            Log.Warn($"Open-Meteo update failed: {exception.Message}");
            return snapshot;
        }
    }

    internal static WeatherSnapshot ParseJson(string json, string location)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var current = root.GetProperty("current");
        var updated = ReadDate(current, "time") ?? DateTime.Now;
        var temperature = ReadDouble(current, "temperature_2m");
        var apparent = ReadDouble(current, "apparent_temperature");
        var humidity = ReadInt(current, "relative_humidity_2m");
        var precipitation = ReadDouble(current, "precipitation");
        var code = ReadInt(current, "weather_code");
        var wind = ReadDouble(current, "wind_speed_10m");
        var windDirection = ReadInt(current, "wind_direction_10m");

        double? maximum = null;
        double? minimum = null;
        int? rainProbability = null;
        if (root.TryGetProperty("daily", out var daily))
        {
            maximum = ReadFirstDouble(daily, "temperature_2m_max");
            minimum = ReadFirstDouble(daily, "temperature_2m_min");
            rainProbability = ReadFirstInt(daily, "precipitation_probability_max");
        }

        var hours = new List<WeatherHour>();
        if (root.TryGetProperty("hourly", out var hourly) &&
            hourly.TryGetProperty("time", out var times) &&
            hourly.TryGetProperty("temperature_2m", out var temperatures) &&
            hourly.TryGetProperty("precipitation_probability", out var probabilities))
        {
            var hasWindSpeeds = hourly.TryGetProperty("wind_speed_10m", out var windSpeeds);
            var hasWindDirections = hourly.TryGetProperty("wind_direction_10m", out var windDirections);
            var count = Math.Min(times.GetArrayLength(),
                Math.Min(temperatures.GetArrayLength(), probabilities.GetArrayLength()));
            var future = new List<WeatherHour>();
            for (var index = 0; index < count; index++)
            {
                if (!DateTime.TryParse(times[index].GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var time) || time < DateTime.Now.AddMinutes(-30))
                {
                    continue;
                }
                var hourWindSpeed = hasWindSpeeds && index < windSpeeds.GetArrayLength() &&
                                    windSpeeds[index].ValueKind == JsonValueKind.Number
                    ? windSpeeds[index].GetDouble()
                    : (double?)null;
                var hourWindDirection = hasWindDirections && index < windDirections.GetArrayLength() &&
                                        windDirections[index].ValueKind == JsonValueKind.Number
                    ? windDirections[index].GetInt32()
                    : (int?)null;
                future.Add(new WeatherHour(time, temperatures[index].GetDouble(),
                    probabilities[index].ValueKind == JsonValueKind.Null ? 0 : probabilities[index].GetInt32(),
                    hourWindSpeed, hourWindDirection));
            }
            hours.AddRange(future.Where((_, index) => index % 2 == 0).Take(4));
        }

        return new WeatherSnapshot(location, temperature, apparent, humidity, wind, windDirection,
            precipitation, code, maximum, minimum, rainProbability, hours, updated, null);
    }

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static double? ReadFirstDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array &&
        value.GetArrayLength() > 0 && value[0].ValueKind == JsonValueKind.Number
            ? value[0].GetDouble()
            : null;

    private static int? ReadFirstInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array &&
        value.GetArrayLength() > 0 && value[0].ValueKind == JsonValueKind.Number
            ? value[0].GetInt32()
            : null;

    private static DateTime? ReadDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;

    private static WeatherSnapshot? LoadCache()
    {
        try
        {
            return File.Exists(CachePath)
                ? JsonSerializer.Deserialize<WeatherSnapshot>(File.ReadAllText(CachePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(WeatherSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception exception)
        {
            Log.Warn($"Unable to save weather cache: {exception.Message}");
        }
    }

    public void Dispose() => _http.Dispose();
}
