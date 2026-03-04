namespace FlashSpot.Core.Models;

public sealed class FlashSpotSettings
{
    public string IndexPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludedPaths { get; init; } = Array.Empty<string>();
    public int WorkerCount { get; init; } = Environment.ProcessorCount;
    public int MaxTextFileBytes { get; init; } = 524_288;
    public int MaxSearchResults { get; init; } = 40;
    public string SettingsPath { get; init; } = string.Empty;
    public bool EnableWebSearch { get; init; } = true;
    public bool EnableDictionary { get; init; } = true;
    public bool EnableCurrencyConversion { get; init; } = true;
    public bool EnableWeather { get; init; } = true;
}

