using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace FlashSpot.Core.Services;

public sealed class DictionaryService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, DictionaryEntry? Entry)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public DictionaryService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    }

    public async Task<DictionaryEntry?> LookupAsync(string word, CancellationToken ct)
    {
        var key = word.Trim().ToLowerInvariant();

        if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheDuration)
        {
            return cached.Entry;
        }

        try
        {
            var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(key)}";
            var response = await _httpClient.GetStringAsync(url, ct);
            var doc = JsonDocument.Parse(response);

            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                _cache[key] = (DateTime.UtcNow, null);
                return null;
            }

            var first = doc.RootElement[0];
            var wordText = first.TryGetProperty("word", out var w) ? w.GetString() ?? key : key;

            string? partOfSpeech = null;
            string? definition = null;

            if (first.TryGetProperty("meanings", out var meanings) && meanings.ValueKind == JsonValueKind.Array)
            {
                foreach (var meaning in meanings.EnumerateArray())
                {
                    partOfSpeech ??= meaning.TryGetProperty("partOfSpeech", out var pos) ? pos.GetString() : null;

                    if (meaning.TryGetProperty("definitions", out var defs) && defs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var def in defs.EnumerateArray())
                        {
                            if (def.TryGetProperty("definition", out var d) && d.GetString() is { Length: > 0 } defText)
                            {
                                definition ??= defText;
                                break;
                            }
                        }
                    }

                    if (definition is not null)
                    {
                        break;
                    }
                }
            }

            if (definition is null)
            {
                _cache[key] = (DateTime.UtcNow, null);
                return null;
            }

            var entry = new DictionaryEntry(wordText, partOfSpeech ?? "", definition);

            // Trim cache
            if (_cache.Count > 100)
            {
                var oldest = _cache.OrderBy(kv => kv.Value.CachedAt).Take(20).Select(kv => kv.Key).ToList();
                foreach (var k in oldest)
                {
                    _cache.TryRemove(k, out _);
                }
            }

            _cache[key] = (DateTime.UtcNow, entry);
            return entry;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed record DictionaryEntry(string Word, string PartOfSpeech, string Definition);
