using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FlashSpot.App.Infrastructure;
using FlashSpot.App.Models;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;
using FlashSpot.Core.Services;

namespace FlashSpot.App;

public partial class SearchWindow : Window
{
    private const int SearchDebounceMs = 250;
    private const int MaxUiResults = 40;
    private const string EmptyQueryText = "Type to search";

    private readonly SearchAggregator _aggregator;
    private readonly IIndexStatusService _indexStatusService;
    private readonly IFileIndexingService _indexingService;
    private readonly IFlashSpotSettingsProvider _settingsProvider;
    private readonly UsageTracker? _usageTracker;
    private readonly ObservableCollection<SearchListItem> _results = [];
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly DispatcherTimer _indexStatusTimer;

    private List<SearchListItem> _allResults = [];
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _iconCts;
    private SettingsWindow? _settingsWindow;
    private string _hotkeyHint = "Alt+Space";
    private bool _allowRealClose;
    private bool _statusRefreshInFlight;
    private SearchCategory _selectedCategory = SearchCategory.All;
    private SpotlightUiPreferences _uiPreferences = new()
    {
        ShowKeyHintsInFooter = true,
        ShowIndexStatusInHeader = false,
        ShowFilterChips = true
    };

    public SearchWindow(
        SearchAggregator aggregator,
        IIndexStatusService indexStatusService,
        IFileIndexingService indexingService,
        IFlashSpotSettingsProvider settingsProvider,
        UsageTracker? usageTracker = null)
    {
        _aggregator = aggregator;
        _indexStatusService = indexStatusService;
        _indexingService = indexingService;
        _settingsProvider = settingsProvider;
        _usageTracker = usageTracker;

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

        KeyHintFooterText.Text = "↵ Open  ⌃↵ Reveal  ⎋ Close";
        ApplyUiPreferences();
        _ = RefreshIndexStatusAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackdrop.TryApplyAcrylic(this);
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
        AnimateCardIn();
    }

    public void SetHotkeyHint(string hotkey)
    {
        _hotkeyHint = hotkey;
        HotkeyHintText.Text = hotkey;
        _settingsWindow?.ApplyHotkeyHint(hotkey);
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
        _iconCts?.Cancel();
        _iconCts?.Dispose();
        _settingsWindow?.Close();

        base.OnClosing(e);
    }

    // ─── Animation ──────────────────────────────────────────────

    private void AnimateCardIn()
    {
        SpotlightCard.Opacity = 0;
        CardScale.ScaleX = 0.96;
        CardScale.ScaleY = 0.96;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleX = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        SpotlightCard.BeginAnimation(OpacityProperty, fadeIn);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    // ─── Search Logic ───────────────────────────────────────────

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

        // Cancel any in-progress icon loading from previous search
        _iconCts?.Cancel();
        _iconCts?.Dispose();
        _iconCts = null;

        if (string.IsNullOrWhiteSpace(query))
        {
            _allResults = [];
            ReplaceDisplayedResults([]);
            ResultsList.SelectedIndex = -1;
            SearchStatusText.Text = EmptyQueryText;
            return;
        }

        try
        {
            var categoryFilter = _selectedCategory == SearchCategory.All
                ? null
                : _selectedCategory.ToString();

            var hits = await _aggregator.SearchAsync(query, categoryFilter, MaxUiResults, cancellationToken);

            var localResults = new List<SearchListItem>(hits.Count);
            foreach (var hit in hits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                localResults.Add(MapResult(hit));
            }

            _allResults = localResults;
            ApplyFilterToDisplayedResults();

            ResultsList.SelectedIndex = _results.Count > 0 ? 0 : -1;
            SearchStatusText.Text = _results.Count > 0
                ? $"{_results.Count} result{(_results.Count != 1 ? "s" : "")}"
                : "No matches found.";

            // Load icons asynchronously at background priority (not blocking the UI)
            _iconCts = new CancellationTokenSource();
            _ = LoadIconsAsync(_allResults, _iconCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _allResults = [];
            ReplaceDisplayedResults([]);
            SearchStatusText.Text = $"Search error: {ex.Message}";
        }
    }

    // ─── Mapping ────────────────────────────────────────────────

    private static SearchListItem MapResult(SearchResult result)
    {
        var category = ParseCategory(result.Category);
        var kind = ParseKind(result.Kind);

        return new SearchListItem
        {
            Category = category,
            Kind = kind,
            IconText = result.IconGlyph ?? BadgeForKind(kind, result.IconPath),
            // IconImage loaded asynchronously after results are displayed
            Title = result.Title,
            Subtitle = kind == SearchItemKind.File ? CompactPath(result.Subtitle) : result.Subtitle,
            DateText = result.Timestamp?.ToLocalTime().ToString("dd-MM-yyyy HH:mm") ?? string.Empty,
            SizeText = result.SizeBytes.HasValue
                ? FormatSize(result.SizeBytes.Value)
                : KindLabel(kind, result.IconPath),
            Path = result.IconPath ?? result.ActionUri,
            Value = result.InlineValue,
            ActionUri = result.ActionUri,
            SecondaryActionUri = result.SecondaryActionUri,
            InlineValue = result.InlineValue
        };
    }

    private async Task LoadIconsAsync(IReadOnlyList<SearchListItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (item.Path is null)
            {
                continue;
            }

            var path = item.Path;
            var ext = Path.GetExtension(path);

            // Load each icon at Background priority so the UI thread processes input first
            await Dispatcher.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    item.IconImage = ShellIconExtractor.GetIconImage(path, ext);
                }
            }, DispatcherPriority.Background);
        }
    }

    private static SearchCategory ParseCategory(string category)
    {
        return Enum.TryParse<SearchCategory>(category, ignoreCase: true, out var parsed)
            ? parsed
            : SearchCategory.All;
    }

    private static SearchItemKind ParseKind(string kind)
    {
        return Enum.TryParse<SearchItemKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : SearchItemKind.File;
    }

    private static string BadgeForKind(SearchItemKind kind, string? iconPath)
    {
        return kind switch
        {
            SearchItemKind.Calculation => "=",
            SearchItemKind.SystemCommand => "cmd",
            SearchItemKind.Definition => "Aa",
            SearchItemKind.UnitConversion => "unit",
            SearchItemKind.Weather => "wx",
            SearchItemKind.WebResult => "web",
            SearchItemKind.ClipboardItem => "clip",
            SearchItemKind.Bookmark => "link",
            SearchItemKind.QuickAction => ">_",
            _ => BadgeForExtension(iconPath)
        };
    }

    private static string KindLabel(SearchItemKind kind, string? iconPath)
    {
        return kind switch
        {
            SearchItemKind.Calculation => "Calc",
            SearchItemKind.SystemCommand => "System",
            SearchItemKind.Definition => "Definition",
            SearchItemKind.UnitConversion => "Convert",
            SearchItemKind.Weather => "Weather",
            SearchItemKind.WebResult => "Web",
            SearchItemKind.Application => "App",
            SearchItemKind.ClipboardItem => "Clipboard",
            SearchItemKind.Bookmark => "Bookmark",
            SearchItemKind.QuickAction => "Action",
            _ when iconPath is not null => ExtensionLabel(iconPath),
            _ => "File"
        };
    }

    private static string ExtensionLabel(string path)
    {
        var ext = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(ext) ? "File" : ext.TrimStart('.').ToLowerInvariant();
    }

    private static string BadgeForExtension(string? path)
    {
        if (path is null)
        {
            return "file";
        }

        var ext = Path.GetExtension(path)?.TrimStart('.').ToLowerInvariant() ?? "";
        if (ext.Length > 4)
        {
            ext = ext[..4];
        }

        return ext.Length > 0 ? $".{ext}" : "file";
    }

    private static string CompactPath(string path, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= maxLength)
        {
            return path;
        }

        var root = Path.GetPathRoot(path) ?? string.Empty;
        var fileName = Path.GetFileName(path);
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

    // ─── Index Status ───────────────────────────────────────────

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

        QueueFooterText.Visibility = snapshot.PendingCount > 0 || snapshot.FailedCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ─── UI Preferences ────────────────────────────────────────

    private void ApplyUiPreferences()
    {
        KeyHintFooterText.Visibility = _uiPreferences.ShowKeyHintsInFooter ? Visibility.Visible : Visibility.Collapsed;
        IndexHeaderText.Visibility = _uiPreferences.ShowIndexStatusInHeader ? Visibility.Visible : Visibility.Collapsed;
        FilterChipsScrollViewer.Visibility = _uiPreferences.ShowFilterChips ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                _indexStatusService,
                _indexingService,
                _settingsProvider,
                _hotkeyHint,
                _uiPreferences.Clone(),
                OnUiPreferencesChanged);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        else
        {
            _settingsWindow.ApplyHotkeyHint(_hotkeyHint);
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
        Hide();
    }

    private void OnUiPreferencesChanged(SpotlightUiPreferences preferences)
    {
        _uiPreferences = preferences.Clone();
        ApplyUiPreferences();
    }

    // ─── Filtering ──────────────────────────────────────────────

    private void FilterPill_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton radioButton)
        {
            return;
        }

        var tag = radioButton.Tag?.ToString();
        if (!Enum.TryParse<SearchCategory>(tag, ignoreCase: true, out var next))
        {
            next = SearchCategory.All;
        }

        if (next == _selectedCategory)
        {
            return;
        }

        _selectedCategory = next;

        if (!string.IsNullOrWhiteSpace(SearchInput.Text))
        {
            _ = RunSearchAsync(SearchInput.Text);
            return;
        }

        ApplyFilterToDisplayedResults();
        ResultsList.SelectedIndex = _results.Count > 0 ? 0 : -1;

        if (string.IsNullOrWhiteSpace(SearchInput.Text))
        {
            SearchStatusText.Text = EmptyQueryText;
        }
        else if (_results.Count > 0)
        {
            SearchStatusText.Text = $"{_results.Count} result{(_results.Count != 1 ? "s" : "")}";
        }
        else
        {
            SearchStatusText.Text = "No matches found.";
        }
    }

    private void ApplyFilterToDisplayedResults()
    {
        // Detach ItemsSource to prevent per-item UI updates
        ResultsList.ItemsSource = null;

        _results.Clear();
        foreach (var item in _allResults.Take(MaxUiResults))
        {
            if (ShouldIncludeInFilter(item, _selectedCategory))
            {
                _results.Add(item);
            }
        }

        // Reattach — single UI layout pass
        ResultsList.ItemsSource = _results;
    }

    private void ReplaceDisplayedResults(IReadOnlyList<SearchListItem> items)
    {
        ResultsList.ItemsSource = null;
        _results.Clear();
        foreach (var item in items)
        {
            _results.Add(item);
        }
        ResultsList.ItemsSource = _results;
    }

    private static bool ShouldIncludeInFilter(SearchListItem item, SearchCategory filter)
    {
        if (filter == SearchCategory.All)
        {
            return true;
        }

        if (item.Kind == SearchItemKind.Calculation)
        {
            return false;
        }

        return item.Category == filter;
    }

    // ─── Actions ────────────────────────────────────────────────

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
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    OpenSelectedResultLocation();
                }
                else
                {
                    ActivateSelectedResult();
                }
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

        var actionUri = selected.ActionUri ?? selected.Path;

        // Record usage for ranking
        if (actionUri is not null)
        {
            _usageTracker?.RecordActivation(actionUri);
        }

        // Handle copy:// scheme (calculator results, definitions)
        if (actionUri is not null && actionUri.StartsWith("copy://", StringComparison.OrdinalIgnoreCase))
        {
            var valueToCopy = actionUri["copy://".Length..];
            Clipboard.SetText(valueToCopy);
            SearchStatusText.Text = "Result copied to clipboard.";
            return;
        }

        // Handle cmd:// scheme (system commands)
        if (actionUri is not null && actionUri.StartsWith("cmd://", StringComparison.OrdinalIgnoreCase))
        {
            if (SystemCommandExecutor.TryExecute(actionUri, out var errorMessage))
            {
                Hide();
            }
            else if (errorMessage is not null)
            {
                SearchStatusText.Text = errorMessage;
            }
            return;
        }

        // Handle shell:// scheme (quick action shell commands)
        if (actionUri is not null && actionUri.StartsWith("shell://", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = actionUri["shell://".Length..];
            try
            {
                Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true });
                Hide();
            }
            catch
            {
                SearchStatusText.Text = $"Could not run: {cmd}";
            }
            return;
        }

        // Handle ms-settings: URIs
        if (actionUri is not null && actionUri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = actionUri, UseShellExecute = true });
                Hide();
            }
            catch
            {
                SearchStatusText.Text = "Could not open Settings.";
            }
            return;
        }

        // Handle http/https URLs (web results, bookmarks)
        if (actionUri is not null && (actionUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || actionUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = actionUri, UseShellExecute = true });
                Hide();
            }
            catch
            {
                SearchStatusText.Text = "Could not open URL.";
            }
            return;
        }

        // Handle file paths
        if (string.IsNullOrWhiteSpace(actionUri))
        {
            return;
        }

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = actionUri,
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

    private void OpenSelectedResultLocation()
    {
        if (ResultsList.SelectedItem is not SearchListItem selected)
        {
            return;
        }

        var path = selected.SecondaryActionUri ?? selected.Path;
        if (string.IsNullOrWhiteSpace(path) || !Path.Exists(path))
        {
            return;
        }

        try
        {
            var args = Directory.Exists(path)
                ? $"\"{path}\""
                : $"/select,\"{path}\"";

            var processInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = args,
                UseShellExecute = true
            };
            Process.Start(processInfo);
        }
        catch
        {
            SearchStatusText.Text = "Could not open item location.";
        }
    }

    // ─── Window Events ──────────────────────────────────────────

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Hide();
        }
    }

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
