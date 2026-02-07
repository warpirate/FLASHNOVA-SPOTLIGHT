using System.Windows;
using System.Windows.Input;
using FlashSpot.App.Infrastructure;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Services;

namespace FlashSpot.App;

public partial class App : Application
{
    private GlobalHotkey? _searchHotkey;
    private SearchWindow? _searchWindow;
    private LuceneFileSearchService? _searchService;
    private FileSystemIndexStatusService? _indexStatusService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        IFlashSpotSettingsProvider settingsProvider = new JsonFlashSpotSettingsProvider();
        _searchService = new LuceneFileSearchService(settingsProvider);
        _indexStatusService = new FileSystemIndexStatusService(settingsProvider);
        ICalculatorService calculator = new SimpleCalculatorService();

        _searchWindow = new SearchWindow(_searchService, calculator, _indexStatusService);

        _searchHotkey = TryRegisterHotkey();
        if (_searchHotkey is not null)
        {
            _searchHotkey.Pressed += OnSearchHotkeyPressed;
            _searchWindow.SetHotkeyHint(_searchHotkey.DisplayText);
        }
        else
        {
            _searchWindow.SetHotkeyHint("No global hotkey");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_searchHotkey is not null)
        {
            _searchHotkey.Pressed -= OnSearchHotkeyPressed;
            _searchHotkey.Dispose();
        }

        _searchService?.Dispose();
        _searchWindow?.AllowRealClose();
        _searchWindow?.Close();

        base.OnExit(e);
    }

    private void OnSearchHotkeyPressed(object? sender, EventArgs e)
    {
        if (_searchWindow is null)
        {
            return;
        }

        Dispatcher.Invoke(_searchWindow.ShowForInput);
    }

    private static GlobalHotkey? TryRegisterHotkey()
    {
        try
        {
            return new GlobalHotkey(ModifierKeys.Alt, Key.Space);
        }
        catch
        {
            try
            {
                return new GlobalHotkey(ModifierKeys.Control, Key.Space);
            }
            catch
            {
                return null;
            }
        }
    }
}
