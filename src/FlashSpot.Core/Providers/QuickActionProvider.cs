using System.Text.RegularExpressions;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.Core.Providers;

public sealed partial class QuickActionProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All", "Actions"];

    private static readonly Dictionary<string, string> _settingsPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["display"] = "ms-settings:display",
        ["bluetooth"] = "ms-settings:bluetooth",
        ["wifi"] = "ms-settings:network-wifi",
        ["network"] = "ms-settings:network-status",
        ["sound"] = "ms-settings:sound",
        ["notifications"] = "ms-settings:notifications",
        ["power"] = "ms-settings:powersleep",
        ["battery"] = "ms-settings:batterysaver",
        ["storage"] = "ms-settings:storagesense",
        ["apps"] = "ms-settings:appsfeatures",
        ["default apps"] = "ms-settings:defaultapps",
        ["accounts"] = "ms-settings:yourinfo",
        ["time"] = "ms-settings:dateandtime",
        ["language"] = "ms-settings:regionlanguage",
        ["keyboard"] = "ms-settings:typing",
        ["mouse"] = "ms-settings:mousetouchpad",
        ["privacy"] = "ms-settings:privacy",
        ["update"] = "ms-settings:windowsupdate",
        ["windows update"] = "ms-settings:windowsupdate",
        ["themes"] = "ms-settings:themes",
        ["personalization"] = "ms-settings:personalization",
        ["colors"] = "ms-settings:colors",
        ["lock screen"] = "ms-settings:lockscreen",
        ["taskbar"] = "ms-settings:taskbar",
        ["startup"] = "ms-settings:startupapps",
        ["about"] = "ms-settings:about",
        ["vpn"] = "ms-settings:network-vpn",
        ["proxy"] = "ms-settings:network-proxy",
    };

    public string Name => "Quick Actions";
    public int Priority => 12;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query)
    {
        var q = query.Trim();
        return q.StartsWith('>') || UrlPattern().IsMatch(q);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var q = query.Trim();
        var results = new List<SearchResult>();

        // URL detection
        if (UrlPattern().IsMatch(q))
        {
            var url = q.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? q : $"https://{q}";
            results.Add(new SearchResult
            {
                ProviderId = "quickaction",
                Category = "Actions",
                Kind = "QuickAction",
                Title = $"Open {q}",
                Subtitle = "Open URL in default browser",
                IconGlyph = "web",
                ActionUri = url,
                Score = 600f
            });
        }

        // > prefix commands
        if (q.StartsWith('>'))
        {
            var cmd = q[1..].Trim().ToLowerInvariant();

            // Settings pages
            foreach (var (name, uri) in _settingsPages)
            {
                if (name.Contains(cmd, StringComparison.OrdinalIgnoreCase) || cmd.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new SearchResult
                    {
                        ProviderId = "quickaction",
                        Category = "Actions",
                        Kind = "QuickAction",
                        Title = $"Settings: {name}",
                        Subtitle = uri,
                        IconGlyph = ">_",
                        ActionUri = uri,
                        Score = 500f
                    });
                }

                if (results.Count >= maxResults)
                {
                    break;
                }
            }

            // Shell commands
            if (cmd.Length > 0 && results.Count == 0)
            {
                results.Add(new SearchResult
                {
                    ProviderId = "quickaction",
                    Category = "Actions",
                    Kind = "QuickAction",
                    Title = $"Run: {cmd}",
                    Subtitle = "Execute as shell command",
                    IconGlyph = ">_",
                    ActionUri = $"shell://{cmd}",
                    Score = 400f
                });
            }
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results.Take(maxResults).ToList());
    }

    [GeneratedRegex(@"^(https?://|www\.)\S+$|^\S+\.(com|org|net|io|dev|co|app|me)\S*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlPattern();
}
