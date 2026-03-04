using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.Core.Services;

public sealed class FileWatcherService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly IFileIndexingService _indexingService;
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _debounceTimer;
    private readonly object _lock = new();
    private bool _disposed;

    public FileWatcherService(IFlashSpotSettingsProvider settingsProvider, IFileIndexingService indexingService)
    {
        _indexingService = indexingService;
        _debounceTimer = new Timer(OnDebounce, null, Timeout.Infinite, Timeout.Infinite);

        var settings = settingsProvider.Load();
        foreach (var root in settings.Roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;

                _watchers.Add(watcher);
            }
            catch
            {
                // Skip roots that can't be watched
            }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        EnqueuePath(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        EnqueuePath(e.OldFullPath);
        EnqueuePath(e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // FileSystemWatcher can overflow; we can't do much except log
    }

    private void EnqueuePath(string path)
    {
        lock (_lock)
        {
            _pendingPaths.Add(path);
            _debounceTimer.Change(500, Timeout.Infinite);
        }
    }

    private void OnDebounce(object? state)
    {
        // For now, we just note that changes happened.
        // A full reindex can be triggered manually.
        // Future: call incremental index update methods.
        lock (_lock)
        {
            _pendingPaths.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Dispose();

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
