using System.Text.RegularExpressions;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed partial class CurrencyConversionProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All"];

    private readonly CurrencyService _currencyService;

    public CurrencyConversionProvider(CurrencyService currencyService)
    {
        _currencyService = currencyService;
    }

    public string Name => "Currency Conversion";
    public int Priority => 8;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        return q.Length >= 5
            && q.Any(char.IsDigit)
            && (q.Contains(" in ") || q.Contains(" to "));
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var match = CurrencyPattern().Match(query.Trim());
        if (!match.Success)
        {
            return [];
        }

        if (!decimal.TryParse(match.Groups["amount"].Value, out var amount))
        {
            return [];
        }

        var from = match.Groups["from"].Value.Trim();
        var to = match.Groups["to"].Value.Trim();

        // Verify both are valid currency codes before calling the API
        if (CurrencyService.ResolveCurrencyCode(from) is null
            || CurrencyService.ResolveCurrencyCode(to) is null)
        {
            return [];
        }

        var result = await _currencyService.ConvertAsync(amount, from, to, cancellationToken);
        if (result is null)
        {
            return [];
        }

        var display = $"{result.FromAmount:N2} {result.FromCode} = {result.ToAmount:N2} {result.ToCode}";

        return
        [
            new SearchResult
            {
                ProviderId = "currency",
                Category = "All",
                Kind = "UnitConversion",
                Title = display,
                Subtitle = "Currency conversion (live rates)",
                IconGlyph = "$",
                InlineValue = display,
                ActionUri = $"copy://{result.ToAmount:N2} {result.ToCode}",
                Score = 800f
            }
        ];
    }

    [GeneratedRegex(@"^(?:\$\s*)?(?<amount>\d+\.?\d*)\s*(?<from>[a-zA-Z$]+(?:\s+[a-zA-Z]+)?)\s+(?:in|to)\s+(?<to>[a-zA-Z$]+(?:\s+[a-zA-Z]+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CurrencyPattern();
}
