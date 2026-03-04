using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed class AppSearchProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All", "Apps"];

    private readonly AppCatalogService _catalog;

    public AppSearchProvider(AppCatalogService catalog)
    {
        _catalog = catalog;
    }

    public string Name => "Apps";
    public int Priority => 2;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query) => query.Length >= 1;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var matches = _catalog.Search(query, maxResults);

        var results = matches.Select((app, i) => new SearchResult
        {
            ProviderId = "apps",
            Category = "Apps",
            Kind = "Application",
            Title = app.DisplayName,
            Subtitle = app.ExecutablePath,
            IconPath = app.ExecutablePath,
            ActionUri = app.ExecutablePath,
            Score = 500f - i
        }).ToList();

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
