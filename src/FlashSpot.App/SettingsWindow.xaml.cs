using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Models;

namespace FlashSpot.App;

public partial class SettingsWindow : Window
{
    private readonly IIndexStatusService _indexStatusService;
    private readonly IFileIndexingService _indexingService;
    private readonly IFlashSpotSettingsProvider _settingsProvider;
    private readonly Action<SpotlightUiPreferences> _onPreferencesChanged;
    private readonly DispatcherTimer _statusTimer;

    private SpotlightUiPreferences _preferences;
    private bool _refreshInFlight;
    private CancellationTokenSource? _indexCts;

    public SettingsWindow(
        IIndexStatusService indexStatusService,
        IFileIndexingService indexingService,
        IFlashSpotSettingsProvider settingsProvider,
        string hotkeyHint,
        SpotlightUiPreferences preferences,
        Action<SpotlightUiPreferences> onPreferencesChanged)
    {
        _indexStatusService = indexStatusService;
        _indexingService = indexingService;
        _settingsProvider = settingsProvider;
        _preferences = preferences.Clone();
        _onPreferencesChanged = onPreferencesChanged;

        InitializeComponent();

        ApplyHotkeyHint(hotkeyHint);
        KeyHintsToggle.IsChecked = _preferences.ShowKeyHintsInFooter;
        IndexHeaderToggle.IsChecked = _preferences.ShowIndexStatusInHeader;
        FilterChipsToggle.IsChecked = _preferences.ShowFilterChips;

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _statusTimer.Tick += StatusTimer_Tick;
        _statusTimer.Start();

        _indexingService.ProgressChanged += OnIndexingProgress;

        UpdateReindexButtonState();
        _ = RefreshStatusAsync();
    }

    public void ApplyHotkeyHint(string hotkeyHint)
    {
        HotkeyShortcutKeyText.Text = $"{hotkeyHint}:";
        HotkeyShortcutValueText.Text = "Toggle";
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        _indexingService.ProgressChanged -= OnIndexingProgress;
        base.OnClosed(e);
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshStatusAsync();
        UpdateReindexButtonState();
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshInFlight)
        {
            return;
        }

        _refreshInFlight = true;
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
            _refreshInFlight = false;
        }
    }

    private void ApplyStatus(IndexStatusSnapshot snapshot)
    {
        var stateText = snapshot.IsIndexing ? "Indexing" : "Ready";
        IndexStatusValueText.Text = $"{stateText} | Indexed {snapshot.IndexedItemCount:n0} items";
        QueueStatusValueText.Text = $"Pending {snapshot.PendingCount:n0} | Failed {snapshot.FailedCount:n0}";

        var updatedLocal = snapshot.UpdatedAtUtc?.ToLocalTime();
        LastUpdatedText.Text = updatedLocal.HasValue
            ? $"Last updated: {updatedLocal.Value:MMM d, yyyy h:mm tt}"
            : "Last updated: Just now";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshStatusAsync();
    }

    private async void ReindexButton_Click(object sender, RoutedEventArgs e)
    {
        if (_indexingService.IsIndexing)
        {
            // Cancel ongoing indexing
            _indexCts?.Cancel();
            return;
        }

        _indexCts?.Dispose();
        _indexCts = new CancellationTokenSource();

        ReindexButton.Content = "Cancel";
        ReindexButton.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B3A3A")!);
        ReindexButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D65A5A")!);
        IndexStatusValueText.Text = "Indexing...";

        try
        {
            await _indexingService.RunFullIndexAsync(_indexCts.Token);
        }
        catch (OperationCanceledException)
        {
            IndexStatusValueText.Text = "Indexing cancelled.";
        }
        catch (Exception ex)
        {
            IndexStatusValueText.Text = $"Indexing error: {ex.Message}";
        }
        finally
        {
            UpdateReindexButtonState();
            await RefreshStatusAsync();
        }
    }

    private void UpdateReindexButtonState()
    {
        if (_indexingService.IsIndexing)
        {
            ReindexButton.Content = "Cancel";
            ReindexButton.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B3A3A")!);
            ReindexButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D65A5A")!);
        }
        else
        {
            ReindexButton.Content = "Reindex";
            ReindexButton.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3A6B9F")!);
            ReindexButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5A99D6")!);
        }
    }

    private void OnIndexingProgress(object? sender, IndexingProgressEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            if (args.IsComplete)
            {
                IndexStatusValueText.Text = $"Done | Indexed {args.FilesProcessed:n0} files ({args.FilesFailed:n0} failed)";
            }
            else
            {
                IndexStatusValueText.Text = $"Indexing... {args.FilesProcessed:n0} files";
            }
        });
    }

    private void ViewQueueButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsProvider.Load();
            var appDataPath = Path.GetDirectoryName(settings.SettingsPath);
            if (string.IsNullOrWhiteSpace(appDataPath) || !Directory.Exists(appDataPath))
            {
                return;
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = appDataPath,
                UseShellExecute = true
            };
            Process.Start(processInfo);
        }
        catch
        {
        }
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        _preferences = new SpotlightUiPreferences
        {
            ShowKeyHintsInFooter = KeyHintsToggle.IsChecked == true,
            ShowIndexStatusInHeader = IndexHeaderToggle.IsChecked == true,
            ShowFilterChips = FilterChipsToggle.IsChecked == true
        };

        _onPreferencesChanged(_preferences.Clone());
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
