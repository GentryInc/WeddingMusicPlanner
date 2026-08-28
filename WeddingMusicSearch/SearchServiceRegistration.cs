using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Infrastructure;
using WeddingMusicSearch.Providers;

namespace WeddingMusicSearch;

public static class SearchServiceRegistration
{
    /// <summary>
    /// Registers the local + remote search providers, their typed HttpClients with
    /// resilient retry policies, and the aggregating <see cref="AggregateSearchService"/>.
    /// </summary>
    public static IServiceCollection AddWeddingMusicSearch(
        this IServiceCollection services,
        Action<SpotifyOptions> configureSpotify,
        Action<MusicBrainzOptions>? configureMusicBrainz = null,
        Action<DiscogsOptions>? configureDiscogs = null,
        Action<AppleMusicOptions>? configureApple = null,
        Action<YouTubeMusicOptions>? configureYouTube = null)
    {
        services.Configure(configureSpotify);
        services.Configure(configureMusicBrainz ?? (_ => { }));
        services.Configure(configureDiscogs ?? (_ => { }));
        services.Configure(configureApple ?? (_ => { }));
        services.Configure(configureYouTube ?? (_ => { }));

        // Local + Spotify providers (Spotify manages its own HttpClient internally).
        services.AddScoped<ISearchService, LocalSearchService>();
        services.AddSingleton<ISearchService, SpotifySearchService>();

        // MusicBrainz typed client with retry + circuit breaker.
        services.AddHttpClient<ISearchService, MusicBrainzSearchService>()
                .AddPolicyHandler(BuildRetryPolicy())
                .AddPolicyHandler(BuildCircuitBreakerPolicy());

        // Discogs typed client with retry + circuit breaker.
        services.AddHttpClient<ISearchService, DiscogsSearchService>()
                .AddPolicyHandler(BuildRetryPolicy())
                .AddPolicyHandler(BuildCircuitBreakerPolicy());

        // Apple Music (MusicKit) typed client with retry + circuit breaker.
        services.AddHttpClient<ISearchService, AppleMusicSearchService>()
                .AddPolicyHandler(BuildRetryPolicy())
                .AddPolicyHandler(BuildCircuitBreakerPolicy());

        // YouTube Data API v3 typed client with retry + circuit breaker.
        services.AddHttpClient<ISearchService, YouTubeMusicSearchService>()
                .AddPolicyHandler(BuildRetryPolicy())
                .AddPolicyHandler(BuildCircuitBreakerPolicy());

        // YouTube trending (videos.list?chart=mostPopular) shares the same options/key.
        services.AddHttpClient<Trending.ITrendingService, Trending.YouTubeTrendingService>()
                .AddPolicyHandler(BuildRetryPolicy())
                .AddPolicyHandler(BuildCircuitBreakerPolicy());

        // Streaming metadata resolver: refreshes aged reference-only display caches.
        services.AddHttpClient<Streaming.IStreamingMetadataResolver, Streaming.YouTubeMetadataResolver>()
                .AddPolicyHandler(BuildRetryPolicy())
                .AddPolicyHandler(BuildCircuitBreakerPolicy());

        services.AddScoped<AggregateSearchService>(sp =>
            new AggregateSearchService(sp.GetServices<ISearchService>()));

        return services;
    }

    /// <summary>
    /// Exponential backoff with jitter; also honours HTTP 429 Retry-After and
    /// retries on transient 5xx/408/socket errors.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> BuildRetryPolicy()
    {
        var delay = Backoff.DecorrelatedJitterBackoffV2(
            medianFirstRetryDelay: TimeSpan.FromMilliseconds(500), retryCount: 4);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(delay, onRetryAsync: (outcome, timespan, _, _) =>
            {
                // Respect an explicit Retry-After header if the API provided one.
                var retryAfter = outcome.Result?.Headers.RetryAfter?.Delta;
                if (retryAfter is { } ra && ra > timespan)
                    return Task.Delay(ra - timespan);
                return Task.CompletedTask;
            });
    }

    private static IAsyncPolicy<HttpResponseMessage> BuildCircuitBreakerPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(handledEventsAllowedBeforeBreaking: 5,
                                 durationOfBreak: TimeSpan.FromSeconds(30));
}
