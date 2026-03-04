using System.Text;
using System.Text.Json;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FlashSpot.Core.Services;

public sealed class LuceneFileIndexingService : IFileIndexingService
{
    private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // System / hidden
        "$Recycle.Bin", "System Volume Information", "$WINDOWS.~BT", "$WINDOWS.~WS",
        "$WinREAgent", "PerfLogs", "Recovery", "Windows",
        // Dev tooling
        "node_modules", ".git", ".svn", ".hg", "bin", "obj", "packages",
        "__pycache__", ".tox", ".venv", "venv",
        // IDE
        ".vs", ".idea", ".vscode",
        // Build output
        "Debug", "Release", "x64", "x86",
        // App data noise
        "Cache", "CachedData", "Code Cache", "GPUCache", "DawnCache",
        "Temp", "tmp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".cfg", ".conf",
        ".yaml", ".yml", ".toml",
        ".cs", ".csx", ".fs", ".fsx", ".vb",
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs",
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".kts", ".scala",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".hh",
        ".html", ".htm", ".css", ".scss", ".less", ".sass",
        ".sql", ".sh", ".bash", ".ps1", ".psm1", ".bat", ".cmd",
        ".sln", ".csproj", ".fsproj", ".vbproj", ".props", ".targets",
        ".xaml", ".axaml", ".razor", ".cshtml",
        ".dockerfile", ".gitignore", ".editorconfig", ".env"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IFlashSpotSettingsProvider _settingsProvider;
    private volatile bool _isIndexing;

    public bool IsIndexing => _isIndexing;
    public event EventHandler<IndexingProgressEventArgs>? ProgressChanged;

    public LuceneFileIndexingService(IFlashSpotSettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    public async Task RunFullIndexAsync(CancellationToken cancellationToken = default)
    {
        if (_isIndexing)
        {
            return;
        }

        _isIndexing = true;
        var settings = _settingsProvider.Load();
        var scanStatePath = GetScanStatePath(settings);

        WriteScanState(scanStatePath, isIndexing: true, completed: false, indexed: 0, pending: 1, failed: 0);

        int processed = 0, skipped = 0, failed = 0;

        try
        {
            await Task.Run(() =>
            {
                System.IO.Directory.CreateDirectory(settings.IndexPath);

                using var directory = FSDirectory.Open(settings.IndexPath);
                using var analyzer = new StandardAnalyzer(Version);

                var config = new IndexWriterConfig(Version, analyzer)
                {
                    OpenMode = OpenMode.CREATE // full rebuild
                };

                using var writer = new IndexWriter(directory, config);

                var excludedLower = settings.ExcludedPaths
                    .Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant())
                    .ToHashSet();

                foreach (var root in settings.Roots)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!System.IO.Directory.Exists(root))
                    {
                        continue;
                    }

                    WalkDirectory(writer, root, excludedLower, settings.MaxTextFileBytes, cancellationToken,
                        ref processed, ref skipped, ref failed);

                    if (processed % 500 == 0)
                    {
                        RaiseProgress(processed, skipped, failed, isComplete: false);
                        WriteScanState(scanStatePath, isIndexing: true, completed: false, indexed: processed, pending: 0, failed: failed);
                    }
                }

                writer.Commit();

            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // cancelled — partial index is fine
        }
        catch
        {
            failed++;
        }
        finally
        {
            _isIndexing = false;
            WriteScanState(scanStatePath, isIndexing: false, completed: true, indexed: processed, pending: 0, failed: failed);
            RaiseProgress(processed, skipped, failed, isComplete: true);
        }
    }

    private void WalkDirectory(
        IndexWriter writer,
        string directoryPath,
        HashSet<string> excludedPaths,
        int maxTextBytes,
        CancellationToken ct,
        ref int processed,
        ref int skipped,
        ref int failed)
    {
        ct.ThrowIfCancellationRequested();

        // Skip excluded directory names
        var dirName = Path.GetFileName(directoryPath);
        if (ExcludedDirectoryNames.Contains(dirName))
        {
            return;
        }

        // Skip explicitly excluded paths
        if (excludedPaths.Contains(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()))
        {
            return;
        }

        // Index files in this directory
        try
        {
            foreach (var filePath in System.IO.Directory.EnumerateFiles(directoryPath))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var doc = CreateDocument(filePath, maxTextBytes);
                    if (doc is not null)
                    {
                        writer.AddDocument(doc);
                        processed++;
                    }
                    else
                    {
                        skipped++;
                    }

                    // Progress callback every 200 files
                    if (processed % 200 == 0)
                    {
                        RaiseProgress(processed, skipped, failed, isComplete: false);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    failed++;
                }
            }
        }
        catch (UnauthorizedAccessException) { return; }
        catch (OperationCanceledException) { throw; }
        catch { /* skip inaccessible directories */ }

        // Recurse into subdirectories
        try
        {
            foreach (var subDir in System.IO.Directory.EnumerateDirectories(directoryPath))
            {
                WalkDirectory(writer, subDir, excludedPaths, maxTextBytes, ct, ref processed, ref skipped, ref failed);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (OperationCanceledException) { throw; }
        catch { }
    }

    private static Document? CreateDocument(string filePath, int maxTextBytes)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            return null;
        }

        // Skip very large files (>100MB) and zero-byte files
        if (info.Length > 100_000_000 || info.Length == 0)
        {
            return null;
        }

        var name = info.Name;
        var extension = info.Extension;
        var lastModified = info.LastWriteTimeUtc;

        var doc = new Document
        {
            new StringField("path", filePath, Field.Store.YES),
            new TextField("name", name, Field.Store.YES),
            new TextField("filename", name, Field.Store.YES),
            new StringField("extension", extension, Field.Store.YES),
            new StringField("ext", extension, Field.Store.YES),
            new StringField("size", info.Length.ToString(), Field.Store.YES),
            new StringField("lastModifiedUtc", lastModified.ToString("o"), Field.Store.YES),
        };

        // Index text content for known text file types
        if (TextExtensions.Contains(extension) && info.Length <= maxTextBytes)
        {
            try
            {
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                doc.Add(new TextField("content", content, Field.Store.NO));
            }
            catch
            {
                // can't read content — still index the file metadata
            }
        }

        return doc;
    }

    private void RaiseProgress(int processed, int skipped, int failed, bool isComplete)
    {
        ProgressChanged?.Invoke(this, new IndexingProgressEventArgs
        {
            FilesProcessed = processed,
            FilesSkipped = skipped,
            FilesFailed = failed,
            IsComplete = isComplete
        });
    }

    private static string GetScanStatePath(FlashSpotSettings settings)
    {
        var basePath = Path.GetDirectoryName(settings.SettingsPath);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FlashSpot");
        }

        return Path.Combine(basePath, "scan-state.json");
    }

    private static void WriteScanState(string path, bool isIndexing, bool completed, long indexed, int pending, int failed)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var state = new
            {
                IsIndexing = isIndexing,
                InitialScanCompleted = completed,
                IndexedItems = indexed,
                PendingCount = pending,
                FailedCount = failed,
                UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
            };

            File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // non-critical — status display may be stale
        }
    }
}
