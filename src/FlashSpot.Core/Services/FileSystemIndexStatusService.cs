using System.Text.Json;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace FlashSpot.Core.Services;

public sealed class FileSystemIndexStatusService : IIndexStatusService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFlashSpotSettingsProvider _settingsProvider;
    private readonly string _scanStatePath;
    private readonly object _sync = new();

    private DateTime _lastDocCountRefreshUtc = DateTime.MinValue;
    private long _cachedDocCount;

    public FileSystemIndexStatusService(IFlashSpotSettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;

        var settings = settingsProvider.Load();
        var basePath = Path.GetDirectoryName(settings.SettingsPath);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FlashSpot");
        }

        _scanStatePath = Path.Combine(basePath, "scan-state.json");
    }

    public IndexStatusSnapshot GetStatus()
    {
        var settings = _settingsProvider.Load();
        var state = LoadScanState();

        var pendingCount = state.PendingCount
            ?? state.PendingDirectories?.Length
            ?? 0;

        var failedCount = state.FailedCount ?? 0;
        var scanCompleted = state.InitialScanCompleted ?? false;

        var indexed = GetDocCount(settings.IndexPath, state.IndexedItems);
        var isIndexing = state.IsIndexing ?? (!scanCompleted || pendingCount > 0);

        return new IndexStatusSnapshot
        {
            IsIndexing = isIndexing,
            InitialScanCompleted = scanCompleted,
            IndexedItemCount = indexed,
            PendingCount = pendingCount,
            FailedCount = failedCount,
            UpdatedAtUtc = ParseDateTimeOffset(state.UpdatedAtUtc)
        };
    }

    private long GetDocCount(string indexPath, long? fallback)
    {
        lock (_sync)
        {
            if ((DateTime.UtcNow - _lastDocCountRefreshUtc) < TimeSpan.FromSeconds(2))
            {
                return _cachedDocCount > 0 ? _cachedDocCount : fallback ?? 0;
            }

            _lastDocCountRefreshUtc = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrWhiteSpace(indexPath) || !System.IO.Directory.Exists(indexPath))
                {
                    _cachedDocCount = fallback ?? 0;
                    return _cachedDocCount;
                }

                using var directory = FSDirectory.Open(indexPath);
                if (!DirectoryReader.IndexExists(directory))
                {
                    _cachedDocCount = fallback ?? 0;
                    return _cachedDocCount;
                }

                using var reader = DirectoryReader.Open(directory);
                _cachedDocCount = reader.NumDocs;
                return _cachedDocCount;
            }
            catch
            {
                return _cachedDocCount > 0 ? _cachedDocCount : fallback ?? 0;
            }
        }
    }

    private ScanStateDto LoadScanState()
    {
        if (!File.Exists(_scanStatePath))
        {
            return new ScanStateDto();
        }

        try
        {
            var json = File.ReadAllText(_scanStatePath);
            return JsonSerializer.Deserialize<ScanStateDto>(json, JsonOptions) ?? new ScanStateDto();
        }
        catch
        {
            return new ScanStateDto();
        }
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private sealed class ScanStateDto
    {
        public bool? InitialScanCompleted { get; set; }
        public string[]? PendingDirectories { get; set; }
        public int? PendingCount { get; set; }
        public int? FailedCount { get; set; }
        public long? IndexedItems { get; set; }
        public bool? IsIndexing { get; set; }
        public string? UpdatedAtUtc { get; set; }
    }
}
