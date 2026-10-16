using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ArtistGenres.Configuration;

/// <summary>
/// Which catalogue's genre names get written onto the library.
/// </summary>
public static class GenreSources
{
    public const string MusicBrainz = "MusicBrainz";

    public const string Discogs = "Discogs";
}

/// <summary>
/// Which Discogs lists are copied into Jellyfin's genre field.
/// </summary>
public static class DiscogsFieldModes
{
    public const string Styles = "Styles";

    public const string Genres = "Genres";

    public const string StylesAndGenres = "StylesAndGenres";
}

/// <summary>
/// Saved from the plugin configuration page.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Source = GenreSources.MusicBrainz;
        UpdateArtists = true;
        UpdateAlbums = true;
        UpdateSongs = true;
        FallbackToOtherSource = true;
        KeepOriginalGenres = true;
        ReapplyAfterLibraryScan = true;
        MaxGenres = 5;

        MusicBrainzServer = "https://musicbrainz.org";
        MusicBrainzContact = string.Empty;
        MusicBrainzMinimumVotes = 1;
        MusicBrainzInheritAlbumGenres = true;

        DiscogsToken = string.Empty;
        DiscogsFields = DiscogsFieldModes.Styles;
    }

    /// <summary>
    /// Gets or sets the selected source. <see cref="GenreSources.MusicBrainz"/> or <see cref="GenreSources.Discogs"/>.
    /// </summary>
    public string Source { get; set; }

    public bool UpdateArtists { get; set; }

    public bool UpdateAlbums { get; set; }

    public bool UpdateSongs { get; set; }

    public bool FallbackToOtherSource { get; set; }

    public bool KeepOriginalGenres { get; set; }

    public bool ReapplyAfterLibraryScan { get; set; }

    /// <summary>
    /// Gets or sets how many genre names to keep. Zero means no limit.
    /// </summary>
    public int MaxGenres { get; set; }

    public string MusicBrainzServer { get; set; }

    public string MusicBrainzContact { get; set; }

    public int MusicBrainzMinimumVotes { get; set; }

    public bool MusicBrainzInheritAlbumGenres { get; set; }

    public string DiscogsToken { get; set; }

    public string DiscogsFields { get; set; }
}
