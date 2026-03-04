using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.Core.Providers;

public sealed class SystemCommandProvider : ISearchProvider
{
    private static readonly HashSet<string> _categories = ["All", "System"];

    private static readonly SystemCommand[] _commands =
    [
        new("Lock", "Lock the computer", "cmd://lock"),
        new("Sleep", "Put the computer to sleep", "cmd://sleep"),
        new("Shutdown", "Shut down the computer", "cmd://shutdown"),
        new("Restart", "Restart the computer", "cmd://restart"),
        new("Sign Out", "Sign out of Windows", "cmd://signout"),
        new("Log Off", "Log off the current user", "cmd://signout"),
        new("Empty Recycle Bin", "Empty the Recycle Bin", "cmd://emptyrecyclebin"),
        new("Task Manager", "Open Task Manager", "cmd://taskmgr"),
        new("Control Panel", "Open Control Panel", "cmd://controlpanel"),
        new("Settings", "Open Windows Settings", "cmd://settings"),
        new("Device Manager", "Open Device Manager", "cmd://devicemanager"),
        new("Disk Cleanup", "Run Disk Cleanup", "cmd://diskcleanup"),
        new("Screen Saver", "Start the screen saver", "cmd://screensaver"),
        new("Command Prompt", "Open Command Prompt", "cmd://cmd"),
        new("PowerShell", "Open PowerShell", "cmd://powershell"),
        new("Terminal", "Open Windows Terminal", "cmd://terminal"),
        new("File Explorer", "Open File Explorer", "cmd://explorer"),
        new("Calculator", "Open Calculator app", "cmd://calc"),
        new("Notepad", "Open Notepad", "cmd://notepad"),
        new("Snipping Tool", "Open Snipping Tool", "cmd://snippingtool"),
    ];

    public string Name => "System Commands";
    public int Priority => 10;
    public IReadOnlySet<string> Categories => _categories;

    public bool CanHandle(string query) => query.Length >= 2;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var q = query.Trim();
        var results = _commands
            .Select(cmd =>
            {
                var score = MatchScore(cmd.Name, q);
                return (cmd, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .Select(x => new SearchResult
            {
                ProviderId = "system",
                Category = "System",
                Kind = "SystemCommand",
                Title = x.cmd.Name,
                Subtitle = x.cmd.Description,
                IconGlyph = "cmd",
                ActionUri = x.cmd.ActionUri,
                Score = x.score
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private static float MatchScore(string name, string query)
    {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 200f;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 150f;
        }

        var nameWords = name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in nameWords)
        {
            if (word.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return 100f;
            }
        }

        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 50f;
        }

        return 0f;
    }

    private sealed record SystemCommand(string Name, string Description, string ActionUri);
}
