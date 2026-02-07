using System.Text.Json;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.Core.Services;

public sealed class JsonFlashSpotSettingsProvider : IFlashSpotSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public JsonFlashSpotSettingsProvider(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
    }

    public FlashSpotSettings Load()
    {
        var defaults = CreateDefault(_settingsPath);

        if (!File.Exists(_settingsPath))
        {
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions);
            if (dto is null)
            {
                return defaults;
            }

            return new FlashSpotSettings
            {
                IndexPath = string.IsNullOrWhiteSpace(dto.IndexPath) ? defaults.IndexPath : dto.IndexPath,
                Roots = dto.Roots?.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    ?? defaults.Roots,
                ExcludedPaths = dto.ExcludedPaths?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    ?? Array.Empty<string>(),
                WorkerCount = dto.WorkerCount is > 0 ? dto.WorkerCount.Value : defaults.WorkerCount,
                MaxTextFileBytes = dto.MaxTextFileBytes is > 0 ? dto.MaxTextFileBytes.Value : defaults.MaxTextFileBytes,
                MaxSearchResults = dto.MaxSearchResults is > 0 ? dto.MaxSearchResults.Value : defaults.MaxSearchResults,
                SettingsPath = _settingsPath
            };
        }
        catch
        {
            return defaults;
        }
    }

    private static FlashSpotSettings CreateDefault(string settingsPath)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var basePath = Path.Combine(appData, "FlashSpot");

        return new FlashSpotSettings
        {
            IndexPath = Path.Combine(basePath, "Index"),
            Roots =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            ],
            ExcludedPaths = Array.Empty<string>(),
            WorkerCount = Environment.ProcessorCount,
            MaxTextFileBytes = 524_288,
            MaxSearchResults = 40,
            SettingsPath = settingsPath
        };
    }

    private static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "FlashSpot", "settings.json");
    }

    private sealed class SettingsDto
    {
        public string? IndexPath { get; set; }
        public string[]? Roots { get; set; }
        public string[]? ExcludedPaths { get; set; }
        public int? WorkerCount { get; set; }
        public int? MaxTextFileBytes { get; set; }
        public int? MaxSearchResults { get; set; }
    }
}

