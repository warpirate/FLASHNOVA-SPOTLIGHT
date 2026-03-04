using System.Text.RegularExpressions;

namespace FlashSpot.Core.Services;

public sealed partial class NaturalLanguageParser
{
    public NaturalLanguageQuery? TryParse(string query)
    {
        var q = query.Trim().ToLowerInvariant();

        // "files from yesterday", "documents from last week", etc.
        var match = TemporalFilePattern().Match(q);
        if (match.Success)
        {
            var fileType = match.Groups["type"].Value;
            var timeExpr = match.Groups["time"].Value;
            var extensions = ResolveFileType(fileType);
            var (from, to) = ResolveTimeRange(timeExpr);

            if (from.HasValue)
            {
                return new NaturalLanguageQuery(extensions, from.Value, to ?? DateTimeOffset.UtcNow, null, null);
            }
        }

        // "large files", "files over 1 GB"
        var sizeMatch = SizePattern().Match(q);
        if (sizeMatch.Success)
        {
            var sizeStr = sizeMatch.Groups["size"].Value;
            var unit = sizeMatch.Groups["unit"].Value.ToLowerInvariant();
            if (long.TryParse(sizeStr, out var size))
            {
                var bytes = unit switch
                {
                    "kb" => size * 1024,
                    "mb" => size * 1024 * 1024,
                    "gb" => size * 1024L * 1024 * 1024,
                    "tb" => size * 1024L * 1024 * 1024 * 1024,
                    _ => size
                };

                return new NaturalLanguageQuery(null, null, null, bytes, null);
            }
        }

        // "large files" shorthand
        if (q.Contains("large file"))
        {
            return new NaturalLanguageQuery(null, null, null, 100 * 1024 * 1024L, null); // >100 MB
        }

        // "recent downloads"
        if (q.Contains("recent download"))
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return new NaturalLanguageQuery(null, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, downloadsPath);
        }

        return null;
    }

    private static string[]? ResolveFileType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "photo" or "photos" or "image" or "images" or "picture" or "pictures"
                => [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".svg"],
            "document" or "documents" or "doc" or "docs"
                => [".doc", ".docx", ".pdf", ".txt", ".rtf", ".odt", ".xls", ".xlsx", ".ppt", ".pptx"],
            "video" or "videos"
                => [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm"],
            "music" or "audio" or "song" or "songs"
                => [".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a"],
            "code" or "source"
                => [".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".go", ".rs", ".rb", ".swift"],
            "file" or "files" => null, // Any file
            _ => null
        };
    }

    private static (DateTimeOffset? From, DateTimeOffset? To) ResolveTimeRange(string timeExpr)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.Date;

        return timeExpr.Trim().ToLowerInvariant() switch
        {
            "today" => (new DateTimeOffset(today, TimeSpan.Zero), now),
            "yesterday" => (new DateTimeOffset(today.AddDays(-1), TimeSpan.Zero), new DateTimeOffset(today, TimeSpan.Zero)),
            "last week" or "this week" => (now.AddDays(-7), now),
            "last month" or "this month" => (now.AddDays(-30), now),
            "last year" or "this year" => (now.AddDays(-365), now),
            _ when timeExpr.Contains("days ago") => TryParseDaysAgo(timeExpr, now),
            _ => (null, null)
        };
    }

    private static (DateTimeOffset? From, DateTimeOffset? To) TryParseDaysAgo(string expr, DateTimeOffset now)
    {
        var match = Regex.Match(expr, @"(\d+)\s*days?\s*ago");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var days))
        {
            return (now.AddDays(-days), now);
        }

        return (null, null);
    }

    [GeneratedRegex(@"^(?<type>files?|documents?|photos?|images?|pictures?|videos?|music|audio|songs?|code|source)\s+(?:from|modified|opened|created)\s+(?<time>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TemporalFilePattern();

    [GeneratedRegex(@"files?\s+(?:over|larger\s+than|bigger\s+than)\s+(?<size>\d+)\s*(?<unit>kb|mb|gb|tb|bytes?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SizePattern();
}

public sealed record NaturalLanguageQuery(
    string[]? Extensions,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    long? MinSizeBytes,
    string? PathContains);
