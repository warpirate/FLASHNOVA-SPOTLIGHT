using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.Core.Providers;

public sealed class CalculatorProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All"];

    private readonly ICalculatorService _calculatorService;

    public CalculatorProvider(ICalculatorService calculatorService)
    {
        _calculatorService = calculatorService;
    }

    public string Name => "Calculator";
    public int Priority => 1;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query)
    {
        return query.Length >= 1 && query.Any(c => char.IsDigit(c) || c == '(' || c == '.');
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (!_calculatorService.TryEvaluate(query, out var value))
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        var result = new SearchResult
        {
            ProviderId = "calculator",
            Category = "All",
            Kind = "Calculation",
            Title = $"{query} = {value}",
            Subtitle = "Press Enter to copy result",
            IconGlyph = "=",
            InlineValue = value,
            ActionUri = $"copy://{value}",
            Score = 1000f
        };

        return Task.FromResult<IReadOnlyList<SearchResult>>([result]);
    }
}
