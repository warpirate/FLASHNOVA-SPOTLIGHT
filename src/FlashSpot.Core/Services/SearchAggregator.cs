using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.Core.Services;

public sealed class SearchAggregator
{
    private readonly IReadOnlyList<ISearchProvider> _providers;
    private readonly UsageTracker? _usageTracker;

    public SearchAggregator(IEnumerable<ISearchProvider> providers, UsageTracker? usageTracker = null)
    {
        _providers = providers.OrderBy(p => p.Priority).ToList();
        _usageTracker = usageTracker;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        string? categoryFilter,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var applicable = _providers
            .Where(p => p.CanHandle(query))
            .Where(p => categoryFilter is null || p.Categories.Contains(categoryFilter))
            .ToList();

        if (applicable.Count == 0)
        {
            return [];
        }

        var tasks = applicable.Select(async provider =>
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

                return await provider.SearchAsync(query, maxResults, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (IReadOnlyList<SearchResult>)[];
            }
            catch (Exception)
            {
                return (IReadOnlyList<SearchResult>)[];
            }
        });

        var results = await Task.WhenAll(tasks);

        var merged = results.SelectMany(r => r);

        // Apply usage-based ranking boost
        if (_usageTracker is not null)
        {
            merged = merged.Select(r =>
            {
                var boost = r.ActionUri is not null ? _usageTracker.GetBoost(r.ActionUri) : 0f;
                if (boost <= 0f)
                {
                    return r;
                }

                return new SearchResult
                {
                    ProviderId = r.ProviderId,
                    Category = r.Category,
                    Kind = r.Kind,
                    Title = r.Title,
                    Subtitle = r.Subtitle,
                    IconGlyph = r.IconGlyph,
                    IconPath = r.IconPath,
                    InlineValue = r.InlineValue,
                    ActionUri = r.ActionUri,
                    SecondaryActionUri = r.SecondaryActionUri,
                    Score = r.Score + boost,
                    Timestamp = r.Timestamp,
                    SizeBytes = r.SizeBytes,
                    Metadata = r.Metadata
                };
            });
        }

        return merged
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }
}
