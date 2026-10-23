using System.Globalization;
using Jellyfin.Plugin.ArtistGenres.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ArtistGenres;

/// <summary>
/// Writes one genre vocabulary, MusicBrainz or Discogs, across the music library.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Artist Genres";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("c4e8a1d7-5b39-4f6a-8e2c-91d0f7a4b6e3");

    /// <inheritdoc />
    public override string Description => "Write normalized MusicBrainz or Discogs genres across artists, albums, and songs.";

    /// <summary>
    /// Gets the folder that holds the genre cache and the original file-tag genres.
    /// </summary>
    public string GetDataDirectory()
    {
        var path = Path.Combine(ApplicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.ArtistGenres");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
