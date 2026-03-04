using System.IO;
using System.Windows;
using System.Windows.Input;
using FlashSpot.App.Infrastructure;
using FlashSpot.Core.Abstractions;
using FlashSpot.Core.Providers;
using FlashSpot.Core.Services;

namespace FlashSpot.App;

public partial class App : Application
{
    private GlobalHotkey? _searchHotkey;
    private SearchWindow? _searchWindow;
    private LuceneFileSearchService? _searchService;
    private FileSystemIndexStatusService? _indexStatusService;
    private LuceneFileIndexingService? _indexingService;
    private WebSearchService? _webSearchService;
    private DictionaryService? _dictionaryService;
    private CurrencyService? _currencyService;
    private WeatherService? _weatherService;
    private FileWatcherService? _fileWatcherService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        IFlashSpotSettingsProvider settingsProvider = new JsonFlashSpotSettingsProvider();
        var settings = settingsProvider.Load();

        _searchService = new LuceneFileSearchService(settingsProvider);
        _indexStatusService = new FileSystemIndexStatusService(settingsProvider);
        _indexingService = new LuceneFileIndexingService(settingsProvider);
        ICalculatorService calculator = new NCalcCalculatorService();

        // App catalog (scans Start Menu, desktop shortcuts)
        var appCatalog = new AppCatalogService();
        _ = appCatalog.RefreshAsync();

        // Bookmark catalog
        var bookmarkCatalog = new BookmarkCatalogService();

        // Network services
        _webSearchService = new WebSearchService();
        _dictionaryService = new DictionaryService();
        _currencyService = new CurrencyService();
        _weatherService = new WeatherService();

        // Usage tracker
        var dataDir = Path.GetDirectoryName(settings.SettingsPath)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlashSpot");
        var usageTracker = new UsageTracker(dataDir);

        // File watcher for real-time index updates
        _fileWatcherService = new FileWatcherService(settingsProvider, _indexingService);

        // Build search providers (ordered by priority)
        var providers = new List<ISearchProvider>
        {
            new CalculatorProvider(calculator),                 // Priority 1
            new AppSearchProvider(appCatalog),                   // Priority 2
            new UnitConversionProvider(),                        // Priority 5
            new CurrencyConversionProvider(_currencyService),    // Priority 8
            new SystemCommandProvider(),                         // Priority 10
            new QuickActionProvider(),                           // Priority 12
            new DictionaryProvider(_dictionaryService),          // Priority 15
            new FileSearchProvider(_searchService),              // Priority 20
            new WeatherProvider(_weatherService),                // Priority 20
            new BookmarkProvider(bookmarkCatalog),               // Priority 25
            new NaturalLanguageFileProvider(_searchService),     // Priority 35
            new WebSearchProvider(_webSearchService),            // Priority 50
        };

        var aggregator = new SearchAggregator(providers, usageTracker);

        _searchWindow = new SearchWindow(aggregator, _indexStatusService, _indexingService, settingsProvider, usageTracker);

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

        _fileWatcherService?.Dispose();
        _searchService?.Dispose();
        _webSearchService?.Dispose();
        _dictionaryService?.Dispose();
        _currencyService?.Dispose();
        _weatherService?.Dispose();
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
