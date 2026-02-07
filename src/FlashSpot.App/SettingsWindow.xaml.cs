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
    private readonly IFlashSpotSettingsProvider _settingsProvider;
    private readonly Action<SpotlightUiPreferences> _onPreferencesChanged;
    private readonly DispatcherTimer _statusTimer;

    private SpotlightUiPreferences _preferences;
    private bool _refreshInFlight;

    public SettingsWindow(
        IIndexStatusService indexStatusService,
        IFlashSpotSettingsProvider settingsProvider,
        string hotkeyHint,
        SpotlightUiPreferences preferences,
        Action<SpotlightUiPreferences> onPreferencesChanged)
    {
        _indexStatusService = indexStatusService;
        _settingsProvider = settingsProvider;
        _preferences = Clone(preferences);
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
        base.OnClosed(e);
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshStatusAsync();
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

        _onPreferencesChanged(Clone(_preferences));
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

    private static SpotlightUiPreferences Clone(SpotlightUiPreferences preferences)
    {
        return new SpotlightUiPreferences
        {
            ShowKeyHintsInFooter = preferences.ShowKeyHintsInFooter,
            ShowIndexStatusInHeader = preferences.ShowIndexStatusInHeader,
            ShowFilterChips = preferences.ShowFilterChips
        };
    }
}
