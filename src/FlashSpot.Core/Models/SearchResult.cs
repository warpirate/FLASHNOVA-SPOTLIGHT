namespace FlashSpot.Core.Models;

public sealed class SearchResult
{
    public required string ProviderId { get; init; }
    public required string Category { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public string? IconGlyph { get; init; }
    public string? IconPath { get; init; }
    public string? InlineValue { get; init; }
    public string? ActionUri { get; init; }
    public string? SecondaryActionUri { get; init; }
    public float Score { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public long? SizeBytes { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
