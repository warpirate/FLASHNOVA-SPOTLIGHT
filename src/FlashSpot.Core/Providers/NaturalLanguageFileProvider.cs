using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.Core.Providers;

public sealed class NaturalLanguageFileProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All", "Files", "Images"];

    private readonly IFileSearchService _fileSearchService;
    private readonly NaturalLanguageParser _parser = new();

    public NaturalLanguageFileProvider(IFileSearchService fileSearchService)
    {
        _fileSearchService = fileSearchService;
    }

    public string Name => "Natural Language Files";
    public int Priority => 35;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        return q.Contains("from ") || q.Contains("modified ") || q.Contains("opened ")
            || q.Contains("large file") || q.Contains("recent download")
            || q.Contains("files over") || q.Contains("bigger than");
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var nlQuery = _parser.TryParse(query);
        if (nlQuery is null)
        {
            return [];
        }

        // Build a search query that the Lucene service can handle
        // We search broadly and then filter results by date/size/path
        var searchTerms = "*";
        if (nlQuery.Extensions is { Length: > 0 })
        {
            searchTerms = string.Join(" OR ", nlQuery.Extensions.Select(e => $"extension:{e.TrimStart('.')}"));
        }
        else if (nlQuery.PathContains is not null)
        {
            searchTerms = Path.GetFileName(nlQuery.PathContains) ?? "*";
        }

        var hits = await _fileSearchService.SearchAsync(searchTerms, maxResults * 3, cancellationToken);

        var filtered = hits
            .Where(h => MatchesQuery(h, nlQuery))
            .Take(maxResults)
            .Select(hit =>
            {
                var ext = hit.Extension?.Trim().ToLowerInvariant() ?? "";
                if (ext.Length > 0 && !ext.StartsWith('.'))
                {
                    ext = $".{ext}";
                }

                return new SearchResult
                {
                    ProviderId = "nlfiles",
                    Category = IsImageExtension(ext) ? "Images" : "Files",
                    Kind = "File",
                    Title = string.IsNullOrWhiteSpace(hit.Name) ? Path.GetFileName(hit.Path) : hit.Name,
                    Subtitle = hit.Path,
                    IconPath = hit.Path,
                    ActionUri = hit.Path,
                    SecondaryActionUri = hit.Path,
                    Score = hit.Score + 50f,
                    Timestamp = hit.LastModifiedUtc,
                    SizeBytes = hit.SizeBytes
                };
            })
            .ToList();

        return filtered;
    }

    private static bool MatchesQuery(SearchHit hit, NaturalLanguageQuery nlQuery)
    {
        if (nlQuery.FromDate.HasValue && hit.LastModifiedUtc.HasValue
            && hit.LastModifiedUtc.Value < nlQuery.FromDate.Value)
        {
            return false;
        }

        if (nlQuery.ToDate.HasValue && hit.LastModifiedUtc.HasValue
            && hit.LastModifiedUtc.Value > nlQuery.ToDate.Value)
        {
            return false;
        }

        if (nlQuery.MinSizeBytes.HasValue && hit.SizeBytes.HasValue
            && hit.SizeBytes.Value < nlQuery.MinSizeBytes.Value)
        {
            return false;
        }

        if (nlQuery.PathContains is not null
            && !hit.Path.Contains(nlQuery.PathContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (nlQuery.Extensions is { Length: > 0 })
        {
            var ext = hit.Extension?.Trim().ToLowerInvariant() ?? "";
            if (ext.Length > 0 && !ext.StartsWith('.'))
            {
                ext = $".{ext}";
            }

            if (!nlQuery.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsImageExtension(string ext)
    {
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".svg";
    }
}
