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
    private IHost? _host;
    private GlobalHotkeyService? _hotkeys;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeddingMusicPlannerPro");
        var dbPath = Path.Combine(appData, "library.db");
        var cacheDir = Path.Combine(appData, "cache");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(cacheDir);

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                // CreateDefaultBuilder only auto-loads user-secrets in the Development
                // environment; a launched WPF app runs as Production, so load them
                // explicitly. Env vars win last so CI/ops can override without a rebuild.
                config.AddUserSecrets<App>(optional: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((ctx, services) =>
            {
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

                // Background worker that pre-caches upcoming timeline buckets.
                services.AddHostedService(sp => new TimelineCacheWorker(
                    sp.GetRequiredService<IDbContextFactory<WeddingMusicContext>>(),
                    sp.GetRequiredService<IOfflineCacheService>()));

                services.AddSingleton<GlobalHotkeyService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
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
            // Dispose the audio engine cleanly (stops WASAPI, releases streams).
            _host.Services.GetService<IPlaybackService>()?.GetType();
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
