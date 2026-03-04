using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed class WebSearchProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All", "Web"];

    private readonly WebSearchService _webSearchService;

    public WebSearchProvider(WebSearchService webSearchService)
    {
        _webSearchService = webSearchService;
    }

    public string Name => "Web Search";
    public int Priority => 50;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query) => query.Trim().Length >= 3;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var webResults = await _webSearchService.SearchAsync(query, maxResults, cancellationToken);

        return webResults.Select((r, i) =>
        {
            var title = r.Text.Length > 120 ? r.Text[..117] + "..." : r.Text;

            return new SearchResult
            {
                ProviderId = "web",
                Category = "Web",
                Kind = "WebResult",
                Title = title,
                Subtitle = string.IsNullOrWhiteSpace(r.Url) ? r.Source : r.Url,
                IconGlyph = "web",
                ActionUri = string.IsNullOrWhiteSpace(r.Url) ? null : r.Url,
                Score = 10f - i
            };
        }).ToList();
    }
}
