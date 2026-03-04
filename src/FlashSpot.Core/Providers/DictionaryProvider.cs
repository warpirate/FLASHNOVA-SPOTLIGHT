using System.Text.RegularExpressions;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed partial class DictionaryProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All"];

    private readonly DictionaryService _dictionaryService;

    public DictionaryProvider(DictionaryService dictionaryService)
    {
        _dictionaryService = dictionaryService;
    }

    public string Name => "Dictionary";
    public int Priority => 15;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        return DefinePattern().IsMatch(q)
            || (q.Length >= 3 && q.Length <= 30 && SingleWordPattern().IsMatch(q));
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var word = ExtractWord(query.Trim());
        if (string.IsNullOrWhiteSpace(word))
        {
            return [];
        }

        var entry = await _dictionaryService.LookupAsync(word, cancellationToken);
        if (entry is null)
        {
            return [];
        }

        var subtitle = string.IsNullOrWhiteSpace(entry.PartOfSpeech)
            ? "Definition"
            : entry.PartOfSpeech;

        return
        [
            new SearchResult
            {
                ProviderId = "dictionary",
                Category = "All",
                Kind = "Definition",
                Title = $"{entry.Word}: {entry.Definition}",
                Subtitle = subtitle,
                IconGlyph = "Aa",
                InlineValue = entry.Definition,
                ActionUri = $"copy://{entry.Definition}",
                Score = 300f
            }
        ];
    }

    private static string ExtractWord(string query)
    {
        var match = DefinePattern().Match(query.ToLowerInvariant());
        if (match.Success)
        {
            return match.Groups["word"].Value.Trim();
        }

        return query.Trim();
    }

    [GeneratedRegex(@"^(?:define|meaning\s+of|what\s+is|what's)\s+(?<word>\w+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DefinePattern();

    [GeneratedRegex(@"^[a-zA-Z]+$", RegexOptions.Compiled)]
    private static partial Regex SingleWordPattern();
}
