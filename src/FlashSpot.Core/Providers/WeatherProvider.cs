using System.Text.RegularExpressions;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed partial class WeatherProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All"];

    private readonly WeatherService _weatherService;

    public WeatherProvider(WeatherService weatherService)
    {
        _weatherService = weatherService;
    }

    public string Name => "Weather";
    public int Priority => 20;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        return q.StartsWith("weather") || q.StartsWith("temperature") || q.StartsWith("forecast");
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var city = ExtractCity(query.Trim());

        var data = await _weatherService.GetWeatherAsync(city, cancellationToken);
        if (data is null)
        {
            return [];
        }

        var tempF = data.TemperatureCelsius * 9 / 5 + 32;

        return
        [
            new SearchResult
            {
                ProviderId = "weather",
                Category = "All",
                Kind = "Weather",
                Title = $"{data.City}: {data.TemperatureCelsius:F1} C ({tempF:F1} F) - {data.Condition}",
                Subtitle = $"Wind: {data.WindSpeedKmh:F0} km/h | Humidity: {data.HumidityPercent}%",
                IconGlyph = "wx",
                InlineValue = $"{data.TemperatureCelsius:F1} C",
                Score = 400f
            }
        ];
    }

    private static string? ExtractCity(string query)
    {
        var match = WeatherCityPattern().Match(query);
        if (match.Success)
        {
            var city = match.Groups["city"].Value.Trim();
            return city.Length > 0 ? city : null;
        }

        return null;
    }

    [GeneratedRegex(@"^(?:weather|temperature|forecast)\s+(?:in|for|at)\s+(?<city>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WeatherCityPattern();
}
