namespace FlashSpot.Core.Models;

public sealed class IndexStatusSnapshot
{
    public bool IsIndexing { get; init; }
    public bool InitialScanCompleted { get; init; }
    public long IndexedItemCount { get; init; }
    public int PendingCount { get; init; }
    public int FailedCount { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

