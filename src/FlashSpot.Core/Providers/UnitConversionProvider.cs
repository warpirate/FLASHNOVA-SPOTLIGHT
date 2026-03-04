using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed class UnitConversionProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All"];
    private readonly UnitConversionService _conversionService = new();

    public string Name => "Unit Conversion";
    public int Priority => 5;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        return q.Length >= 5
            && q.Any(char.IsDigit)
            && (q.Contains(" in ") || q.Contains(" to ") || q.Contains(" as "));
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (!_conversionService.TryConvert(query, out var result))
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        var searchResult = new SearchResult
        {
            ProviderId = "unitconversion",
            Category = "All",
            Kind = "UnitConversion",
            Title = result,
            Subtitle = "Press Enter to copy result",
            IconGlyph = "unit",
            InlineValue = result,
            ActionUri = $"copy://{result}",
            Score = 900f
        };

        return Task.FromResult<IReadOnlyList<SearchResult>>([searchResult]);
    }
}
