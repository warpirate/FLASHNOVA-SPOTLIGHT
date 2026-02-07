using FlashSpot.Core.Models;

namespace FlashSpot.Core.Abstractions;

public interface IFileSearchService
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default);
}

