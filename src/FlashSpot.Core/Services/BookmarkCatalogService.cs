using System.Text.Json;

namespace FlashSpot.Core.Services;

public sealed class BookmarkCatalogService
{
    private List<BookmarkEntry> _bookmarks = [];
    private DateTime _lastRefresh;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    public IReadOnlyList<BookmarkEntry> Search(string query, int maxResults)
    {
        EnsureRefreshed();

        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var q = query.Trim();
        return _bookmarks
            .Select(b => (b, score: MatchScore(b.Title, b.Url, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .Select(x => x.b)
            .ToList();
    }

    private void EnsureRefreshed()
    {
        if (_bookmarks.Count > 0 && DateTime.UtcNow - _lastRefresh < RefreshInterval)
        {
            return;
        }

        var bookmarks = new List<BookmarkEntry>();

        // Chrome bookmarks
        var chromeBookmarks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data", "Default", "Bookmarks");
        ParseChromiumBookmarks(chromeBookmarks, "Chrome", bookmarks);

        // Edge bookmarks
        var edgeBookmarks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Edge", "User Data", "Default", "Bookmarks");
        ParseChromiumBookmarks(edgeBookmarks, "Edge", bookmarks);

        // Brave bookmarks
        var braveBookmarks = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BraveSoftware", "Brave-Browser", "User Data", "Default", "Bookmarks");
        ParseChromiumBookmarks(braveBookmarks, "Brave", bookmarks);

        _bookmarks = bookmarks;
        _lastRefresh = DateTime.UtcNow;
    }

    private static void ParseChromiumBookmarks(string path, string browser, List<BookmarkEntry> results)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("roots", out var roots))
            {
                return;
            }

            foreach (var root in roots.EnumerateObject())
            {
                if (root.Value.ValueKind == JsonValueKind.Object)
                {
                    TraverseBookmarkNode(root.Value, browser, results);
                }
            }
        }
        catch
        {
            // Silently ignore parse errors
        }
    }

    private static void TraverseBookmarkNode(JsonElement node, string browser, List<BookmarkEntry> results)
    {
        if (node.TryGetProperty("type", out var type))
        {
            if (type.GetString() == "url"
                && node.TryGetProperty("name", out var name)
                && node.TryGetProperty("url", out var url))
            {
                var nameStr = name.GetString() ?? "";
                var urlStr = url.GetString() ?? "";

                if (nameStr.Length > 0 && urlStr.Length > 0)
                {
                    results.Add(new BookmarkEntry(nameStr, urlStr, browser));
                }
            }
            else if (type.GetString() == "folder" && node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    TraverseBookmarkNode(child, browser, results);
                }
            }
        }
    }

    private static float MatchScore(string title, string url, string query)
    {
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 80f : 50f;
        }

        if (url.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 30f;
        }

        return 0f;
    }
}

public sealed record BookmarkEntry(string Title, string Url, string Browser);
