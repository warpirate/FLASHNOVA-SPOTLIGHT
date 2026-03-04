namespace FlashSpot.Core.Abstractions;

public interface IFileIndexingService
{
    bool IsIndexing { get; }
    Task RunFullIndexAsync(CancellationToken cancellationToken = default);
    event EventHandler<IndexingProgressEventArgs>? ProgressChanged;
}

public sealed class IndexingProgressEventArgs : EventArgs
{
    public required int FilesProcessed { get; init; }
    public required int FilesSkipped { get; init; }
    public required int FilesFailed { get; init; }
    public required bool IsComplete { get; init; }
}
