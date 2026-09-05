using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WeddingAudioEngine;
using WeddingMusicData;
using WeddingMusicData.Caching;
using WeddingMusicSearch;
using WeddingMusicPlannerPro.Wpf.Services;
using WeddingMusicPlannerPro.Wpf.ViewModels;
using WeddingMusicPlannerPro.Wpf.Views;

namespace WeddingMusicPlannerPro.Wpf;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private IHost? _host;
    private GlobalHotkeyService? _hotkeys;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Single-instance guard — the Inno Setup installer uses this mutex name
        // to detect a running instance before installing/upgrading.
        _singleInstanceMutex = new Mutex(true, "WeddingMusicPlannerPro_SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "Wedding Music Planner Pro is already running.",
                "Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeddingMusicPlannerPro");
        var dbPath = Path.Combine(appData, "library.db");
        var cacheDir = Path.Combine(appData, "cache");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(cacheDir);

        // Load user settings from disk (API keys configured via UI)
        var settingsService = new SettingsService();
        settingsService.Load();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                // Layer configuration sources:
                // 1. Settings file (UI-configured API keys) - highest priority
                // 2. User secrets (dev convenience)
                // 3. Environment variables (ops/CI overrides)

                // Inject settings from the UI into configuration with proper key structure
                var settingsDict = new Dictionary<string, string?>();
                if (!string.IsNullOrWhiteSpace(settingsService.YouTubeApiKey))
                    settingsDict["YouTube:ApiKey"] = settingsService.YouTubeApiKey;
                if (!string.IsNullOrWhiteSpace(settingsService.SpotifyClientId))
                    settingsDict["Spotify:ClientId"] = settingsService.SpotifyClientId;
                if (!string.IsNullOrWhiteSpace(settingsService.SpotifyClientSecret))
                    settingsDict["Spotify:ClientSecret"] = settingsService.SpotifyClientSecret;

                if (settingsDict.Count > 0)
                    config.AddInMemoryCollection(settingsDict!);

                config.AddUserSecrets<App>(optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((ctx, services) =>
            {
                // Register the pre-loaded settings service as a singleton
                services.AddSingleton<ISettingsService>(settingsService);

                services.AddDbContextFactory<WeddingMusicContext>(o => o.UseSqlite($"Data Source={dbPath}"));

                // A scoped context resolved from the factory so the scoped local
                // search provider (and the aggregate search) can be constructed.
                services.AddScoped(sp =>
                    sp.GetRequiredService<IDbContextFactory<WeddingMusicContext>>().CreateDbContext());

                // Unified search: local library + third-party providers.
                // Only Spotify is configured for now; unconfigured providers
                // short-circuit and report a friendly per-source status.
                services.AddWeddingMusicSearch(
                    configureSpotify: o =>
                    {
                        o.ClientId = ctx.Configuration["Spotify:ClientId"] ?? string.Empty;
                        o.ClientSecret = ctx.Configuration["Spotify:ClientSecret"] ?? string.Empty;
                    },
                    configureYouTube: o =>
                    {
                        // YouTube Data API v3 key; supplied via user-secrets/env, never hard-coded.
                        o.ApiKey = ctx.Configuration["YouTube:ApiKey"] ?? string.Empty;
                    });
                services.AddSingleton<ISearchGateway, SearchGateway>();
                services.AddSingleton<ITrendingGateway, TrendingGateway>();
                services.AddSingleton<IStreamingCacheRefresher, StreamingCacheRefresher>();
                services.AddSingleton<SearchViewModel>();

                // Real dual-deck NAudio engine (Master → default, Cue → secondary device).
                services.AddSingleton(_ => DualDeckAudioEngine.CreateWasapi());

                // Offline caching + fail-safe layer.
                services.AddHttpClient();
                services.AddSingleton<IStreamSourceLocator, YouTubeStreamLocator>();
                services.AddSingleton<IOfflineCacheService>(sp => new OfflineCacheService(
                    sp.GetRequiredService<IDbContextFactory<WeddingMusicContext>>(),
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("cache"),
                    cacheDir,
                    sp.GetRequiredService<IStreamSourceLocator>()));

                services.AddSingleton<IPlaybackService, PlaybackService>();
                services.AddSingleton<ILibraryService, LibraryService>();
                services.AddSingleton<IImportService, ImportService>();

                // Wedding keepsake export: record played songs and export a playlist.
                services.AddSingleton<IPlayHistoryService, PlayHistoryService>();
                services.AddSingleton<IPlaylistExportService, PlaylistExportService>();

                // Guest song requests: embedded LAN web server + persistence.
                services.AddSingleton<ISongRequestService, SongRequestService>();
                services.AddSingleton<ITipPaymentService, TipPaymentService>();
                services.AddSingleton<RequestWebServer>();
                services.AddSingleton<IRequestWebServer>(sp => sp.GetRequiredService<RequestWebServer>());
                services.AddHostedService(sp => sp.GetRequiredService<RequestWebServer>());

                // Optional UPnP port forwarding so off-network guests can reach the page.
                services.AddSingleton<PortForwardingService>();
                services.AddSingleton<IPortForwardingService>(sp => sp.GetRequiredService<PortForwardingService>());
                services.AddHostedService(sp => sp.GetRequiredService<PortForwardingService>());

                // Background worker that pre-caches upcoming timeline buckets.
                services.AddHostedService(sp => new TimelineCacheWorker(
                    sp.GetRequiredService<IDbContextFactory<WeddingMusicContext>>(),
                    sp.GetRequiredService<IOfflineCacheService>()));

                services.AddSingleton<GlobalHotkeyService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();

                // Settings dialog
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddSingleton<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
            })
            .Build();

        // Ensure the database/schema exist before the UI queries it.
        // Applies EF Core migrations (and self-heals a legacy EnsureCreated database)
        // so the schema always matches the current model.
        using (var scope = _host.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WeddingMusicContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await DatabaseInitializer.InitializeAsync(db);
        }

        // Start the background worker(s).
        await _host.StartAsync();

        // Register the global Spacebar → Emergency Fade hotkey.
        _hotkeys = _host.Services.GetRequiredService<GlobalHotkeyService>();
        _hotkeys.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
