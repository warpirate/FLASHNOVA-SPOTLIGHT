using System.Net.Http;
using System.Text.Json;

namespace FlashSpot.Core.Services;

public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _httpClient;
    private (DateTime CachedAt, string City, WeatherData Data)? _lastResult;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public WeatherService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<WeatherData?> GetWeatherAsync(string? city, CancellationToken ct)
    {
        var cityKey = city?.Trim().ToLowerInvariant() ?? "_current";

        if (_lastResult.HasValue
            && _lastResult.Value.City == cityKey
            && DateTime.UtcNow - _lastResult.Value.CachedAt < CacheDuration)
        {
            return _lastResult.Value.Data;
        }

        try
        {
            double lat, lon;
            string resolvedCity;

            if (!string.IsNullOrWhiteSpace(city))
            {
                var geo = await GeocodeAsync(city, ct);
                if (geo is null)
                {
                    return null;
                }

                lat = geo.Value.Lat;
                lon = geo.Value.Lon;
                resolvedCity = geo.Value.Name;
            }
            else
            {
                // Use IP-based geolocation
                var ipGeo = await IpGeolocationAsync(ct);
                if (ipGeo is null)
                {
                    return null;
                }

                lat = ipGeo.Value.Lat;
                lon = ipGeo.Value.Lon;
                resolvedCity = ipGeo.Value.City;
            }

            var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,wind_speed_10m,relative_humidity_2m&temperature_unit=celsius";
            var weatherJson = await _httpClient.GetStringAsync(weatherUrl, ct);
            var doc = JsonDocument.Parse(weatherJson);

            if (!doc.RootElement.TryGetProperty("current", out var current))
            {
                return null;
            }

            var temp = current.TryGetProperty("temperature_2m", out var t) ? t.GetDouble() : 0;
            var weatherCode = current.TryGetProperty("weather_code", out var wc) ? wc.GetInt32() : 0;
            var windSpeed = current.TryGetProperty("wind_speed_10m", out var ws) ? ws.GetDouble() : 0;
            var humidity = current.TryGetProperty("relative_humidity_2m", out var rh) ? rh.GetInt32() : 0;

            var data = new WeatherData(
                resolvedCity,
                temp,
                WeatherCodeToDescription(weatherCode),
                windSpeed,
                humidity);

            _lastResult = (DateTime.UtcNow, cityKey, data);
            return data;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(double Lat, double Lon, string Name)?> GeocodeAsync(string city, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en";
        var json = await _httpClient.GetStringAsync(url, ct);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            return null;
        }

        var first = results[0];
        var lat = first.GetProperty("latitude").GetDouble();
        var lon = first.GetProperty("longitude").GetDouble();
        var name = first.TryGetProperty("name", out var n) ? n.GetString() ?? city : city;

        return (lat, lon, name);
    }

    private async Task<(double Lat, double Lon, string City)?> IpGeolocationAsync(CancellationToken ct)
    {
        try
        {
            var json = await _httpClient.GetStringAsync("http://ip-api.com/json/?fields=lat,lon,city", ct);
            var doc = JsonDocument.Parse(json);
            var lat = doc.RootElement.GetProperty("lat").GetDouble();
            var lon = doc.RootElement.GetProperty("lon").GetDouble();
            var city = doc.RootElement.TryGetProperty("city", out var c) ? c.GetString() ?? "Unknown" : "Unknown";
            return (lat, lon, city);
        }
        catch
        {
            return null;
        }
    }

    private static string WeatherCodeToDescription(int code)
    {
        return code switch
        {
            0 => "Clear sky",
            1 => "Mainly clear",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Foggy",
            51 or 53 or 55 => "Drizzle",
            61 or 63 or 65 => "Rain",
            66 or 67 => "Freezing rain",
            71 or 73 or 75 => "Snow",
            77 => "Snow grains",
            80 or 81 or 82 => "Rain showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm with hail",
            _ => "Unknown"
        };
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed record WeatherData(string City, double TemperatureCelsius, string Condition, double WindSpeedKmh, int HumidityPercent);
