using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed class BookmarkProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All", "Web"];

    private readonly BookmarkCatalogService _catalog;

    public BookmarkProvider(BookmarkCatalogService catalog)
    {
        _catalog = catalog;
    }

    public string Name => "Bookmarks";
    public int Priority => 25;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query) => query.Trim().Length >= 2;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var matches = _catalog.Search(query, maxResults);

        var results = matches.Select((b, i) => new SearchResult
        {
            ProviderId = "bookmarks",
            Category = "Web",
            Kind = "Bookmark",
            Title = b.Title,
            Subtitle = $"{b.Browser} - {b.Url}",
            IconGlyph = "link",
            ActionUri = b.Url,
            Score = 100f - i
        }).ToList();

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
