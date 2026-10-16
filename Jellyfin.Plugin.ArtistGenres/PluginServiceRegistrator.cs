using Jellyfin.Plugin.ArtistGenres.Providers;
using Jellyfin.Plugin.ArtistGenres.Services;
using Jellyfin.Plugin.ArtistGenres.Storage;
using Jellyfin.Plugin.ArtistGenres.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ArtistGenres;

/// <summary>
/// Registers the genre clients and the apply task.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<RequestPacer>();
        serviceCollection.AddSingleton<GenreCache>();
        serviceCollection.AddSingleton<GenreMemory>();
        serviceCollection.AddSingleton<MusicBrainzGenreClient>();
        serviceCollection.AddSingleton<DiscogsGenreClient>();
        serviceCollection.AddSingleton<GenreApplier>();
        serviceCollection.AddSingleton<IScheduledTask, ApplyGenresTask>();
        serviceCollection.AddSingleton<ILibraryPostScanTask, ReapplyGenresPostScanTask>();
    }
}
