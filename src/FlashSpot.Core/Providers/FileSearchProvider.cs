using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.Core.Providers;

public sealed class FileSearchProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All", "Files", "Apps", "Images"];
    private static readonly HashSet<string> _appExtensions = [".exe", ".lnk", ".appref-ms", ".bat", ".cmd"];
    private static readonly HashSet<string> _imageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".svg"];

    private readonly IFileSearchService _fileSearchService;

    public FileSearchProvider(IFileSearchService fileSearchService)
    {
        _fileSearchService = fileSearchService;
    }

    public string Name => "Files";
    public int Priority => 20;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query) => query.Length >= 1;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var hits = await _fileSearchService.SearchAsync(query, maxResults, cancellationToken);

        return hits.Select(hit =>
        {
            var ext = hit.Extension?.Trim().ToLowerInvariant() ?? "";
            if (ext.Length > 0 && !ext.StartsWith('.'))
            {
                ext = $".{ext}";
            }

            var category = _appExtensions.Contains(ext) ? "Apps"
                : _imageExtensions.Contains(ext) ? "Images"
                : "Files";

            return new SearchResult
            {
                ProviderId = "files",
                Category = category,
                Kind = "File",
                Title = string.IsNullOrWhiteSpace(hit.Name)
                    ? Path.GetFileName(hit.Path)
                    : hit.Name,
                Subtitle = hit.Path,
                IconPath = hit.Path,
                ActionUri = hit.Path,
                SecondaryActionUri = hit.Path,
                Score = hit.Score,
                Timestamp = hit.LastModifiedUtc,
                SizeBytes = hit.SizeBytes
            };
        }).ToList();
    }
}
