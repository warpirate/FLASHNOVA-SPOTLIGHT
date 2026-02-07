using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace FlashSpot.Core.Services;

public sealed class LuceneFileSearchService : IFileSearchService, IDisposable
{
    private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;

    private static readonly string[] QueryFields =
    [
        "name", "filename", "fileName", "title",
        "path", "fullpath", "fullPath", "filepath", "filePath",
        "content", "text", "body", "extension", "ext"
    ];

    private static readonly string[] NameFields = ["name", "filename", "fileName", "title"];
    private static readonly string[] PathFields = ["path", "fullpath", "fullPath", "filepath", "filePath", "uri"];
    private static readonly string[] ExtensionFields = ["extension", "ext"];
    private static readonly string[] SizeFields = ["size", "length", "fileSize", "bytes"];
    private static readonly string[] ModifiedFields =
        ["lastModifiedUtc", "lastModified", "modified", "updatedAtUtc", "lastWriteTimeUtc", "lastWriteTicks", "modifiedTicks"];

    private readonly IFlashSpotSettingsProvider _settingsProvider;
    private readonly object _sync = new();
    private readonly StandardAnalyzer _analyzer = new(Version);

    private string? _activeIndexPath;
    private FSDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private bool _disposed;

    public LuceneFileSearchService(IFlashSpotSettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        EnsureSearcherReady();
        var searcher = _searcher;
        if (searcher is null)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        Query luceneQuery;
        try
        {
            luceneQuery = BuildQuery(query);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        var docs = searcher.Search(luceneQuery, maxResults);
        var hits = new List<SearchHit>(docs.ScoreDocs.Length);

        foreach (var scoreDoc in docs.ScoreDocs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = searcher.Doc(scoreDoc.Doc);

            var path = FirstNonEmpty(doc, PathFields);
            var name = FirstNonEmpty(doc, NameFields);

            if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
            {
                name = System.IO.Path.GetFileName(path);
            }

            path ??= name!;

            var extension = FirstNonEmpty(doc, ExtensionFields);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = System.IO.Path.GetExtension(path);
            }

            hits.Add(new SearchHit
            {
                Name = name!,
                Path = path,
                Extension = extension ?? string.Empty,
                SizeBytes = ParseNullableLong(FirstNonEmpty(doc, SizeFields)),
                LastModifiedUtc = ParseDate(FirstNonEmpty(doc, ModifiedFields)),
                Score = scoreDoc.Score
            });
        }

        return Task.FromResult<IReadOnlyList<SearchHit>>(hits);
    }

    private Query BuildQuery(string query)
    {
        var parser = new MultiFieldQueryParser(Version, QueryFields, _analyzer)
        {
            DefaultOperator = QueryParserBase.AND_OPERATOR
        };

        Query parsed;
        try
        {
            parsed = parser.Parse(query.Trim());
        }
        catch (ParseException)
        {
            parsed = parser.Parse(QueryParserBase.Escape(query.Trim()));
        }

        var lowered = query.Trim().ToLowerInvariant();
        var filenameWildcard = new WildcardQuery(new Term("filename", $"*{lowered}*")) { Boost = 4.0f };
        var nameWildcard = new WildcardQuery(new Term("name", $"*{lowered}*")) { Boost = 4.0f };
        var pathWildcard = new WildcardQuery(new Term("path", $"*{lowered}*")) { Boost = 1.6f };

        var boosted = new BooleanQuery
        {
            { parsed, Occur.SHOULD },
            { filenameWildcard, Occur.SHOULD },
            { nameWildcard, Occur.SHOULD },
            { pathWildcard, Occur.SHOULD }
        };

        return boosted;
    }

    private void EnsureSearcherReady()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            var settings = _settingsProvider.Load();
            if (string.IsNullOrWhiteSpace(settings.IndexPath) || !System.IO.Directory.Exists(settings.IndexPath))
            {
                ReleaseLuceneObjects();
                _activeIndexPath = null;
                return;
            }

            if (!string.Equals(_activeIndexPath, settings.IndexPath, StringComparison.OrdinalIgnoreCase))
            {
                ReleaseLuceneObjects();
                _directory = FSDirectory.Open(settings.IndexPath);
                _reader = DirectoryReader.Open(_directory);
                _searcher = new IndexSearcher(_reader);
                _activeIndexPath = settings.IndexPath;
                return;
            }

            if (_reader is null)
            {
                return;
            }

            var reopened = DirectoryReader.OpenIfChanged(_reader);
            if (reopened is not null)
            {
                _reader.Dispose();
                _reader = reopened;
                _searcher = new IndexSearcher(_reader);
            }
        }
    }

    private static string? FirstNonEmpty(Document doc, IEnumerable<string> fields)
    {
        foreach (var field in fields)
        {
            var value = doc.Get(field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static long? ParseNullableLong(string? raw)
    {
        if (long.TryParse(raw, out var value))
        {
            return value;
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        if (!long.TryParse(raw, out var number))
        {
            return null;
        }

        // .NET ticks
        if (number > 621_355_968_000_000_000)
        {
            try
            {
                return new DateTimeOffset(number, TimeSpan.Zero).ToUniversalTime();
            }
            catch
            {
                return null;
            }
        }

        // Unix milliseconds
        if (number > 100_000_000_000)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(number).ToUniversalTime();
            }
            catch
            {
                return null;
            }
        }

        // Unix seconds
        if (number > 100_000_000)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(number).ToUniversalTime();
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            ReleaseLuceneObjects();
            _analyzer.Dispose();
            _disposed = true;
        }
    }

    private void ReleaseLuceneObjects()
    {
        _searcher = null;

        _reader?.Dispose();
        _reader = null;

        _directory?.Dispose();
        _directory = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LuceneFileSearchService));
        }
    }
}
