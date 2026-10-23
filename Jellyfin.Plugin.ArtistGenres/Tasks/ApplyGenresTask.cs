using Jellyfin.Plugin.ArtistGenres.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtistGenres.Tasks;

/// <summary>
/// Manual task that writes the selected source's genres onto the music library.
/// </summary>
public sealed class ApplyGenresTask : IScheduledTask
{
    private readonly GenreApplier _applier;

    private readonly ILogger<ApplyGenresTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyGenresTask"/> class.
    /// </summary>
    public ApplyGenresTask(GenreApplier applier, ILogger<ApplyGenresTask> logger)
    {
        _applier = applier;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Apply artist genres";

    /// <inheritdoc />
    public string Key => "ArtistGenresApply";

    /// <inheritdoc />
    public string Description => "Rewrites music genres from the source selected in the Artist Genres plugin.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await _applier.ApplyAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Apply artist genres failed.");
            throw;
        }
    }
}

/// <summary>
/// Clears the lookup cache and current genres, then fetches them again from the selected source.
/// </summary>
public sealed class ResetGenresTask : IScheduledTask
{
    private readonly GenreApplier _applier;

    private readonly ILogger<ResetGenresTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetGenresTask"/> class.
    /// </summary>
    public ResetGenresTask(GenreApplier applier, ILogger<ResetGenresTask> logger)
    {
        _applier = applier;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Reset artist genres";

    /// <inheritdoc />
    public string Key => "ArtistGenresReset";

    /// <inheritdoc />
    public string Description => "Clears cached lookups and current genres, then fetches them again from the source selected in the Artist Genres plugin.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await _applier.ResetAndApplyAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reset artist genres failed.");
            throw;
        }
    }
}

/// <summary>
/// Puts the last applied genres back after a library scan re-reads the file tags.
/// </summary>
public sealed class ReapplyGenresPostScanTask : MediaBrowser.Controller.Library.ILibraryPostScanTask
{
    private readonly GenreApplier _applier;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReapplyGenresPostScanTask"/> class.
    /// </summary>
    public ReapplyGenresPostScanTask(GenreApplier applier)
    {
        _applier = applier;
    }

    /// <inheritdoc />
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return _applier.ReapplyAsync(progress, cancellationToken);
    }
}
