using System.Collections.Concurrent;
using System.Text.Json;

namespace FlashSpot.Core.Services;

public sealed class UsageTracker
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, UsageEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _saveTimer;

    public UsageTracker(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "usage-stats.json");
        _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        Load();
    }

    public void RecordActivation(string actionUri)
    {
        if (string.IsNullOrWhiteSpace(actionUri))
        {
            return;
        }

        _entries.AddOrUpdate(
            actionUri,
            _ => new UsageEntry { Count = 1, LastUsedUtc = DateTime.UtcNow },
            (_, existing) =>
            {
                existing.Count++;
                existing.LastUsedUtc = DateTime.UtcNow;
                return existing;
            });

        // Debounced save
        _saveTimer.Change(2000, Timeout.Infinite);
    }

    public float GetBoost(string actionUri)
    {
        if (!_entries.TryGetValue(actionUri, out var entry))
        {
            return 0f;
        }

        var daysSinceUse = (DateTime.UtcNow - entry.LastUsedUtc).TotalDays;
        var recencyDecay = (float)Math.Exp(-daysSinceUse / 30.0); // 30-day half-life

        return (float)(Math.Log(entry.Count + 1) * recencyDecay * 50f);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<Dictionary<string, UsageEntry>>(json);
            if (entries is null)
            {
                return;
            }

            foreach (var (key, value) in entries)
            {
                _entries[key] = value;
            }
        }
        catch
        {
            // Start fresh if file is corrupted
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(
                _entries.ToDictionary(kv => kv.Key, kv => kv.Value),
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Non-critical, will retry on next save
        }
    }
}

public sealed class UsageEntry
{
    public int Count { get; set; }
    public DateTime LastUsedUtc { get; set; }
}
