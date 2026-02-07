namespace FlashSpot.Core.Models;

public sealed class SearchHit
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Extension { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public DateTimeOffset? LastModifiedUtc { get; init; }
    public float Score { get; init; }
}

