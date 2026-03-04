using FlashSpot.Core.Models;

namespace FlashSpot.Core.Abstractions;

public interface ISearchProvider
{
    string Name { get; }

    int Priority { get; }

    IReadOnlySet<string> Categories { get; }

    bool CanHandle(string query);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
