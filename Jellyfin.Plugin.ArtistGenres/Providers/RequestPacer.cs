namespace Jellyfin.Plugin.ArtistGenres.Providers;

/// <summary>
/// Keeps outbound lookups at about one request a second, which is MusicBrainz's limit
/// and is also under Discogs' authenticated limit.
/// </summary>
public sealed class RequestPacer
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _next = DateTimeOffset.MinValue;

    /// <summary>
    /// Waits until the next request is allowed.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delay = _next - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _next = DateTimeOffset.UtcNow.AddMilliseconds(1100);
        }
        finally
        {
            _gate.Release();
        }
    }
}
