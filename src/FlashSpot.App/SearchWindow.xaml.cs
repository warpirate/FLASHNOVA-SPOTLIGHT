using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.App;

public partial class SearchWindow : Window
{
    private const int SearchDebounceMs = 120;
    private const int MaxUiResults = 40;

    private readonly IFileSearchService _fileSearchService;
    private readonly ICalculatorService _calculatorService;
    private readonly IIndexStatusService _indexStatusService;
    private readonly ObservableCollection<SearchListItem> _results = [];
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly DispatcherTimer _indexStatusTimer;
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _searchCts;
    private string _hotkeyHint = "Alt+Space";
    private bool _allowRealClose;
    private bool _statusRefreshInFlight;

    public SearchWindow(
        IFileSearchService fileSearchService,
        ICalculatorService calculatorService,
        IIndexStatusService indexStatusService)
    {
        _fileSearchService = fileSearchService;
        _calculatorService = calculatorService;
        _indexStatusService = indexStatusService;

        InitializeComponent();

        ResultsList.ItemsSource = _results;

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SearchDebounceMs)
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        _indexStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _indexStatusTimer.Tick += IndexStatusTimer_Tick;
        _indexStatusTimer.Start();

        KeyHintFooterText.Text = $"Enter: Open   Esc: Hide   {_hotkeyHint}: Toggle";
        _ = RefreshIndexStatusAsync();
    }

    public void ShowForInput()
    {
        if (!IsVisible)
        {
            Show();
        }

        Topmost = true;
        Activate();
        SearchInput.Focus();
        SearchInput.SelectAll();
        _ = RefreshIndexStatusAsync();
    }

    public void SetHotkeyHint(string hotkey)
    {
        _hotkeyHint = hotkey;
        HotkeyHintText.Text = hotkey;
        KeyHintFooterText.Text = $"Enter: Open   Esc: Hide   {hotkey}: Toggle";
    }

    public void AllowRealClose()
    {
        _allowRealClose = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowRealClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _searchDebounceTimer.Stop();
        _indexStatusTimer.Stop();
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        base.OnClosing(e);
    }

    private void SearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        await RunSearchAsync(SearchInput.Text);
    }

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var cancellationToken = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(query))
        {
            _results.Clear();
            SearchStatusText.Text = "Type to search your existing FlashSpot index.";
            return;
        }

        try
        {
            var localResults = new List<SearchListItem>(MaxUiResults);

            if (_calculatorService.TryEvaluate(query, out var calc))
            {
                localResults.Add(new SearchListItem
                {
                    Kind = SearchItemKind.Calculation,
                    IconText = "=",
                    IconImage = null,
                    Title = $"{query} = {calc}",
                    Subtitle = "Press Enter to copy result",
                    DateText = string.Empty,
                    SizeText = "Calc",
                    Value = calc
                });
            }

            var hits = await _fileSearchService.SearchAsync(query, MaxUiResults, cancellationToken);
            foreach (var hit in hits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                localResults.Add(MapHit(hit));
            }

            _results.Clear();
            foreach (var item in localResults.Take(MaxUiResults))
            {
                _results.Add(item);
            }

            ResultsList.SelectedIndex = _results.Count > 0 ? 0 : -1;
            SearchStatusText.Text = _results.Count > 0
                ? $"{_results.Count} result(s)"
                : "No matches found in current index.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _results.Clear();
            SearchStatusText.Text = $"Search error: {ex.Message}";
        }
    }

    private static SearchListItem MapHit(SearchHit hit)
    {
        return new SearchListItem
        {
            Kind = SearchItemKind.File,
            IconText = BadgeForExtension(hit.Extension),
            IconImage = GetIconImage(hit.Path, hit.Extension),
            Title = string.IsNullOrWhiteSpace(hit.Name) ? System.IO.Path.GetFileName(hit.Path) : hit.Name,
            Subtitle = CompactPath(hit.Path),
            DateText = hit.LastModifiedUtc?.ToLocalTime().ToString("dd-MM-yyyy HH:mm") ?? string.Empty,
            SizeText = hit.SizeBytes.HasValue ? FormatSize(hit.SizeBytes.Value) : (string.IsNullOrWhiteSpace(hit.Extension) ? "File" : hit.Extension.TrimStart('.').ToLowerInvariant()),
            Path = hit.Path
        };
    }

    private static string BadgeForExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "file";
        }

        var ext = extension.Trim().TrimStart('.').ToLowerInvariant();
        if (ext.Length > 4)
        {
            ext = ext[..4];
        }

        return $".{ext}";
    }

    private static ImageSource? GetIconImage(string? path, string extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.Trim().ToLowerInvariant();
        var cacheKey = ShouldCacheByPath(ext) && !string.IsNullOrWhiteSpace(path)
            ? path!
            : $"ext:{ext}";

        if (IconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        ImageSource? result = null;

        if (!string.IsNullOrWhiteSpace(path))
        {
            result = TryGetShellIcon(path!, useFileAttributes: false);
        }

        if (result is null && !string.IsNullOrWhiteSpace(ext))
        {
            result = TryGetShellIcon(ext, useFileAttributes: true);
        }

        IconCache[cacheKey] = result;
        return result;
    }

    private static bool ShouldCacheByPath(string extension)
    {
        return extension is ".exe" or ".lnk" or ".ico";
    }

    private static ImageSource? TryGetShellIcon(string pathOrExtension, bool useFileAttributes)
    {
        var attributes = useFileAttributes ? FileAttributeNormal : 0u;
        var flags = ShgfiIcon | ShgfiSmallIcon | (useFileAttributes ? ShgfiUseFileAttributes : 0u);

        var info = new SHFILEINFO();
        var result = SHGetFileInfo(
            pathOrExtension,
            attributes,
            out info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(20, 20));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            _ = DestroyIcon(info.hIcon);
        }
    }

    private static string CompactPath(string path, int maxLength = 130)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= maxLength)
        {
            return path;
        }

        var root = System.IO.Path.GetPathRoot(path) ?? string.Empty;
        var fileName = System.IO.Path.GetFileName(path);
        var budget = Math.Max(12, maxLength - root.Length - fileName.Length - 5);
        var middle = path.Substring(root.Length);

        if (middle.Length > budget)
        {
            middle = middle[..budget];
        }

        return $"{root}{middle}...\\{fileName}";
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var order = 0;
        while (value >= 1024 && order < sizes.Length - 1)
        {
            value /= 1024;
            order++;
        }

        return $"{value:0.##} {sizes[order]}";
    }

    private async void IndexStatusTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshIndexStatusAsync();
    }

    private async Task RefreshIndexStatusAsync()
    {
        if (_statusRefreshInFlight)
        {
            return;
        }

        _statusRefreshInFlight = true;
        try
        {
            var snapshot = await Task.Run(_indexStatusService.GetStatus);
            ApplyStatus(snapshot);
        }
        catch
        {
        }
        finally
        {
            _statusRefreshInFlight = false;
        }
    }

    private void ApplyStatus(IndexStatusSnapshot snapshot)
    {
        var headerMode = snapshot.IsIndexing ? "Indexing" : "Ready";
        IndexHeaderText.Text = $"{headerMode} | Indexed {snapshot.IndexedItemCount:n0} items";
        QueueFooterText.Text = $"Pending {snapshot.PendingCount:n0} | Failed {snapshot.FailedCount:n0}";
        IndexingProgressBar.Visibility = snapshot.IsIndexing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ActivateSelectedResult();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                ActivateSelectedResult();
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_results.Count == 0)
        {
            return;
        }

        var index = ResultsList.SelectedIndex;
        if (index < 0)
        {
            index = 0;
        }
        else
        {
            index = Math.Clamp(index + delta, 0, _results.Count - 1);
        }

        ResultsList.SelectedIndex = index;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void ActivateSelectedResult()
    {
        if (ResultsList.SelectedItem is not SearchListItem selected)
        {
            return;
        }

        if (selected.Kind == SearchItemKind.Calculation)
        {
            Clipboard.SetText(selected.Value ?? string.Empty);
            SearchStatusText.Text = "Calculator result copied to clipboard.";
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.Path))
        {
            return;
        }

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = selected.Path,
                UseShellExecute = true
            };
            Process.Start(processInfo);
            Hide();
        }
        catch
        {
            SearchStatusText.Text = "Could not open selected item.";
        }
    }

    private void ToolsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var toolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FlashSpot");

            var processInfo = new ProcessStartInfo
            {
                FileName = toolsPath,
                UseShellExecute = true
            };
            Process.Start(processInfo);
        }
        catch
        {
            SearchStatusText.Text = "Could not open FlashSpot tools folder.";
        }
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private enum SearchItemKind
    {
        File,
        Calculation
    }

    private sealed class SearchListItem
    {
        public SearchItemKind Kind { get; init; }
        public required string IconText { get; init; }
        public ImageSource? IconImage { get; init; }
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required string DateText { get; init; }
        public required string SizeText { get; init; }
        public string? Path { get; init; }
        public string? Value { get; init; }
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x000000080;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
