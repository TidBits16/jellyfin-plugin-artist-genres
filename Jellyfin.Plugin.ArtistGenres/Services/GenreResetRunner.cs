using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtistGenres.Services;

/// <summary>
/// Runs Reset All Genres in the background so it does not appear as a scheduled task.
/// </summary>
public sealed class GenreResetRunner
{
    private readonly GenreApplier _applier;

    private readonly ILogger<GenreResetRunner> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="GenreResetRunner"/> class.
    /// </summary>
    public GenreResetRunner(GenreApplier applier, ILogger<GenreResetRunner> logger)
    {
        _applier = applier;
        _logger = logger;
    }

    /// <summary>
    /// Starts a reset if one is not already running.
    /// </summary>
    /// <returns>False when a reset is already in progress.</returns>
    public bool TryStart()
    {
        if (!_gate.Wait(0))
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _applier.ResetAndApplyAsync(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Reset all genres failed.");
            }
            finally
            {
                _gate.Release();
            }
        });

        return true;
    }
}
