using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace FlashSpot.Core.Services;

public sealed class WebSearchService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, List<WebResult> Results)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public WebSearchService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "FlashSpot/1.0");
    }

    public async Task<IReadOnlyList<WebResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        var cacheKey = query.Trim().ToLowerInvariant();

        if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheDuration)
        {
            return cached.Results.Take(maxResults).ToList();
        }

        try
        {
            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
            var response = await _httpClient.GetStringAsync(url, ct);
            var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var results = new List<WebResult>();

            // Abstract (main answer)
            if (root.TryGetProperty("Abstract", out var abs) && abs.GetString() is { Length: > 0 } abstractText)
            {
                var absUrl = root.TryGetProperty("AbstractURL", out var au) ? au.GetString() ?? "" : "";
                var absSource = root.TryGetProperty("AbstractSource", out var asrc) ? asrc.GetString() ?? "" : "";
                results.Add(new WebResult(abstractText, absUrl, absSource));
            }

            // Related topics
            if (root.TryGetProperty("RelatedTopics", out var topics) && topics.ValueKind == JsonValueKind.Array)
            {
                foreach (var topic in topics.EnumerateArray())
                {
                    if (results.Count >= maxResults)
                    {
                        break;
                    }

                    if (topic.TryGetProperty("Text", out var text) && text.GetString() is { Length: > 0 } t
                        && topic.TryGetProperty("FirstURL", out var firstUrl))
                    {
                        results.Add(new WebResult(t, firstUrl.GetString() ?? "", "DuckDuckGo"));
                    }
                }
            }

            // Trim cache
            if (_cache.Count > 50)
            {
                var oldest = _cache.OrderBy(kv => kv.Value.CachedAt).Take(10).Select(kv => kv.Key).ToList();
                foreach (var key in oldest)
                {
                    _cache.TryRemove(key, out _);
                }
            }

            _cache[cacheKey] = (DateTime.UtcNow, results);
            return results.Take(maxResults).ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed record WebResult(string Text, string Url, string Source);
