using System.Collections.Concurrent;

namespace FlashSpot.Core.Services;

public sealed class AppCatalogService
{
    private readonly ConcurrentDictionary<string, AppEntry> _apps = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AppEntry> Apps => _apps.Values.ToList();

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ScanAll(cancellationToken), cancellationToken);
    }

    public IReadOnlyList<AppEntry> Search(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var q = query.Trim();
        return _apps.Values
            .Select(app =>
            {
                var score = MatchScore(app.DisplayName, q);
                return (app, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .Select(x => x.app)
            .ToList();
    }

    private void ScanAll(CancellationToken ct)
    {
        ScanStartMenuFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), ct);
        ScanStartMenuFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), ct);
        ScanStartMenuFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), ct);
        ScanStartMenuFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ct);

        // Desktop shortcuts
        ScanStartMenuFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ct);
        ScanStartMenuFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ct);
    }

    private void ScanStartMenuFolder(string folder, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".lnk" or ".exe" or ".appref-ms" or ".url"))
                {
                    continue;
                }

                var displayName = Path.GetFileNameWithoutExtension(file);

                // Skip uninstall/setup shortcuts
                var lower = displayName.ToLowerInvariant();
                if (lower.Contains("uninstall") || lower.Contains("setup")
                    || lower.Contains("readme") || lower.Contains("help")
                    || lower.Contains("license") || lower.Contains("changelog"))
                {
                    continue;
                }

                _apps.TryAdd(file, new AppEntry
                {
                    DisplayName = displayName,
                    ExecutablePath = file,
                    Source = "StartMenu"
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static float MatchScore(string name, string query)
    {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100f;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 80f;
        }

        var nameWords = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in nameWords)
        {
            if (word.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return 60f;
            }
        }

        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 40f;
        }

        return 0f;
    }
}

public sealed class AppEntry
{
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public required string Source { get; init; }
}
