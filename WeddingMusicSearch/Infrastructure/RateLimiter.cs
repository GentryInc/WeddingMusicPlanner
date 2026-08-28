namespace WeddingMusicSearch.Infrastructure;

/// <summary>
/// Async token-bucket rate limiter. Each remote provider gets its own instance so
/// we honour that API's documented request ceiling (e.g. MusicBrainz ~1 req/s,
/// Discogs ~60 req/min authenticated).
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _minInterval;
    private DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

    /// <param name="requestsPerInterval">Allowed requests per <paramref name="interval"/>.</param>
    /// <param name="interval">The window over which the request budget applies.</param>
    public RateLimiter(int requestsPerInterval, TimeSpan interval)
    {
        if (requestsPerInterval <= 0) throw new ArgumentOutOfRangeException(nameof(requestsPerInterval));
        _minInterval = interval / requestsPerInterval;
    }

    /// <summary>Waits until the next request is permitted, then returns.</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var earliest = _lastRequestUtc + _minInterval;
            if (earliest > now)
                await Task.Delay(earliest - now, ct).ConfigureAwait(false);

            _lastRequestUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
