using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ArtistGenres.Configuration;
using Jellyfin.Plugin.ArtistGenres.Providers;
using Jellyfin.Plugin.ArtistGenres.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;

namespace Jellyfin.Plugin.ArtistGenres.Services;

/// <summary>
/// Resolves the selected source and writes its genre names onto the music library.
/// </summary>
public sealed class GenreApplier
{
    private readonly ILibraryManager _libraryManager;

    private readonly MusicBrainzGenreClient _musicBrainz;

    private readonly DiscogsGenreClient _discogs;

    private readonly GenreMemory _memory;

    private readonly GenreCache _cache;

    private readonly ILogger<GenreApplier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenreApplier"/> class.
    /// </summary>
    public GenreApplier(
        ILibraryManager libraryManager,
        MusicBrainzGenreClient musicBrainz,
        DiscogsGenreClient discogs,
        GenreMemory memory,
        GenreCache cache,
        ILogger<GenreApplier> logger)
    {
        _libraryManager = libraryManager;
        _musicBrainz = musicBrainz;
        _discogs = discogs;
        _memory = memory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Looks up the selected source and replaces genres that differ.
    /// </summary>
    public async Task ApplyAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = CurrentConfiguration();
        if (config.Source == GenreSources.Discogs && string.IsNullOrWhiteSpace(config.DiscogsToken))
        {
            _logger.LogError("Discogs is selected, but no personal access token is set. Nothing was changed.");
            progress.Report(100);
            return;
        }

        var stats = new ApplyStats();
        var albums = Load(BaseItemKind.MusicAlbum);
        var songs = Load(BaseItemKind.Audio);
        var artists = Load(BaseItemKind.MusicArtist);
        var albumSource = new Dictionary<Guid, IReadOnlyList<string>>();
        var artistLabels = new Dictionary<Guid, IReadOnlyList<string>>();
        var artistsByName = new Dictionary<string, MusicArtist>(StringComparer.OrdinalIgnoreCase);
        var songGenres = new Dictionary<Guid, IReadOnlyList<string>>();
        var total = Math.Max(1, albums.Count + songs.Count + artists.Count);
        var done = 0;

        try
        {
            foreach (var item in albums)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var album = (MusicAlbum)item;
                albumSource[album.Id] = GenreCanonicalizer.Canonicalize(await ResolveAlbumAsync(album, config, cancellationToken).ConfigureAwait(false));
                done++;
                progress.Report(done * 100d / total);
            }

            foreach (var item in artists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var artist = (MusicArtist)item;
                artistsByName.TryAdd(artist.Name, artist);
                var direct = GenreCanonicalizer.Canonicalize(await ResolveArtistAsync(artist, albums, albumSource, config, cancellationToken).ConfigureAwait(false));
                artistLabels[artist.Id] = direct.Count > 0
                    ? direct
                    : GenreCanonicalizer.Canonicalize(artist.Genres);
            }

            foreach (var item in songs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var song = (AudioItem)item;
                var includeArtist = config.Source != GenreSources.MusicBrainz || config.MusicBrainzInheritAlbumGenres;
                var genres = GenresForSong(song, albumSource, artistLabels, artistsByName, includeArtist);
                songGenres[song.Id] = genres;
                if (config.UpdateSongs)
                {
                    await WriteAsync(song, genres, config, stats, cancellationToken).ConfigureAwait(false);
                }

                done++;
                progress.Report(done * 100d / total);
            }

            if (config.UpdateAlbums)
            {
                foreach (var item in albums)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var album = (MusicAlbum)item;
                    await WriteAsync(album, GenresForAlbum(album, albumSource, artistLabels, artistsByName), config, stats, cancellationToken).ConfigureAwait(false);
                }
            }

            if (config.UpdateArtists)
            {
                foreach (var item in artists)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var artist = (MusicArtist)item;
                    var fromSongs = Aggregate(SongsForArtist(artist, songs).Select(song =>
                        songGenres.TryGetValue(song.Id, out var genres) ? genres : []));
                    var labels = artistLabels.TryGetValue(artist.Id, out var known) ? known : [];
                    await WriteAsync(artist, Combine(labels, fromSongs), config, stats, cancellationToken).ConfigureAwait(false);
                    done++;
                    progress.Report(done * 100d / total);
                }
            }
        }
        finally
        {
            _memory.Save();
            _cache.Save();
            progress.Report(100);
        }

        _logger.LogInformation(
            "Artist Genres ({Source}) updated {Updated}, left {Unchanged} unchanged, skipped {Empty} with no source genres and {Locked} with genres locked.",
            config.Source,
            stats.Updated,
            stats.Unchanged,
            stats.SkippedEmpty,
            stats.SkippedLocked);
    }

    /// <summary>
    /// Puts the last applied genre lists back. A library scan reads file tags again, and this runs after that scan.
    /// </summary>
    public async Task ReapplyAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = CurrentConfiguration();
        if (!config.ReapplyAfterLibraryScan)
        {
            progress.Report(100);
            return;
        }

        var applied = _memory.AppliedSnapshot();
        if (applied.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var stats = new ApplyStats();
        var done = 0;
        foreach (var (itemId, genres) in applied)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = _libraryManager.GetItemById(itemId);
            if (item is not null)
            {
                await WriteAsync(item, genres, config, stats, cancellationToken).ConfigureAwait(false);
            }

            done++;
            progress.Report(done * 100d / applied.Count);
        }

        _memory.Save();
        if (stats.Updated > 0)
        {
            _logger.LogInformation("Artist Genres restored {Updated} items after a library scan.", stats.Updated);
        }
    }

    private async Task<IReadOnlyList<string>> ResolveAlbumAsync(MusicAlbum album, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var primary = await GenresForAlbumAsync(album, config.Source, config, cancellationToken).ConfigureAwait(false);
        if (primary.Count > 0 || !config.FallbackToOtherSource)
        {
            return primary;
        }

        var other = config.Source == GenreSources.Discogs ? GenreSources.MusicBrainz : GenreSources.Discogs;
        var fallback = await GenresForAlbumAsync(album, other, config, cancellationToken).ConfigureAwait(false);
        return fallback.Count > 0 ? fallback : primary;
    }

    private static IReadOnlyList<string> GenresForSong(
        AudioItem song,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> albumSource,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> artistLabels,
        IReadOnlyDictionary<string, MusicArtist> artistsByName,
        bool includeArtist)
    {
        var (aliases, plain) = SplitExisting(song.Genres);
        var album = FindAlbum(song);
        IReadOnlyList<string> fromAlbum = [];
        if (album is not null && albumSource.TryGetValue(album.Id, out var found))
        {
            fromAlbum = found;
        }

        var labels = includeArtist
            ? LabelsFor(ArtistNames(song), artistLabels, artistsByName)
            : [];
        return Combine(aliases, labels, fromAlbum, plain);
    }

    private static IReadOnlyList<string> GenresForAlbum(
        MusicAlbum album,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> albumSource,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> artistLabels,
        IReadOnlyDictionary<string, MusicArtist> artistsByName)
    {
        var (aliases, plain) = SplitExisting(album.Genres);
        albumSource.TryGetValue(album.Id, out var fromSource);
        var names = album.AlbumArtists is { Count: > 0 } ? album.AlbumArtists : album.Artists;
        return Combine(aliases, LabelsFor(names ?? [], artistLabels, artistsByName), fromSource ?? [], plain);
    }

    private static (IReadOnlyList<string> Aliases, IReadOnlyList<string> Plain) SplitExisting(IEnumerable<string>? genres)
    {
        var aliases = new List<string>();
        var plain = new List<string>();
        foreach (var entry in GenreCanonicalizer.Expand(genres))
        {
            if (entry.FromAlias)
            {
                aliases.Add(entry.Name);
            }
            else
            {
                plain.Add(entry.Name);
            }
        }

        return (aliases, plain);
    }

    private static IReadOnlyList<string> LabelsFor(
        IEnumerable<string> names,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> artistLabels,
        IReadOnlyDictionary<string, MusicArtist> artistsByName)
    {
        var labels = new List<string>();
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name)
                && artistsByName.TryGetValue(name, out var artist)
                && artistLabels.TryGetValue(artist.Id, out var known))
            {
                labels.AddRange(known);
            }
        }

        return labels;
    }

    private static IEnumerable<string> ArtistNames(AudioItem song)
    {
        if (song.AlbumArtists is { Count: > 0 })
        {
            return song.AlbumArtists;
        }

        return song.Artists ?? [];
    }

    private static IEnumerable<AudioItem> SongsForArtist(MusicArtist artist, IReadOnlyList<BaseItem> songs)
    {
        foreach (var item in songs)
        {
            if (item is not AudioItem song)
            {
                continue;
            }

            foreach (var name in ArtistNames(song))
            {
                if (string.Equals(name, artist.Name, StringComparison.OrdinalIgnoreCase))
                {
                    yield return song;
                    break;
                }
            }
        }
    }

    private static IReadOnlyList<string> Combine(params IEnumerable<string>?[] groups)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            if (group is null)
            {
                continue;
            }

            foreach (var name in group)
            {
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                {
                    list.Add(name);
                }
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<string>> ResolveArtistAsync(
        MusicArtist artist,
        IReadOnlyList<BaseItem> albums,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> albumGenres,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (config.Source == GenreSources.MusicBrainz)
        {
            var artistId = ProviderId(artist, "MusicBrainzArtist");
            if (artistId is not null)
            {
                var direct = await _musicBrainz.GetArtistGenresAsync(artistId, config.MusicBrainzMinimumVotes, cancellationToken).ConfigureAwait(false);
                if (direct.Count > 0)
                {
                    return direct;
                }
            }
        }

        var fromAlbums = Aggregate(AlbumsForArtist(artist, albums).Select(album =>
            albumGenres.TryGetValue(album.Id, out var genres) ? genres : []));
        if (fromAlbums.Count > 0 || !config.FallbackToOtherSource || config.Source != GenreSources.Discogs)
        {
            return fromAlbums;
        }

        var fallbackId = ProviderId(artist, "MusicBrainzArtist");
        return fallbackId is null
            ? fromAlbums
            : await _musicBrainz.GetArtistGenresAsync(fallbackId, config.MusicBrainzMinimumVotes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GenresForAlbumAsync(MusicAlbum album, string source, PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (source == GenreSources.Discogs)
        {
            return await DiscogsAlbumAsync(album, config, cancellationToken).ConfigureAwait(false);
        }

        var releaseGroupId = ProviderId(album, "MusicBrainzReleaseGroup");
        if (releaseGroupId is not null)
        {
            var fromGroup = await _musicBrainz.GetReleaseGroupGenresAsync(releaseGroupId, config.MusicBrainzMinimumVotes, cancellationToken).ConfigureAwait(false);
            if (fromGroup.Count > 0)
            {
                return fromGroup;
            }
        }

        var releaseId = ProviderId(album, "MusicBrainzAlbum");
        return releaseId is null
            ? []
            : await _musicBrainz.GetReleaseGenresAsync(releaseId, config.MusicBrainzMinimumVotes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> DiscogsAlbumAsync(MusicAlbum album, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var mode = config.DiscogsFields;
        var releaseId = ProviderId(album, "DiscogsRelease");
        if (releaseId is not null)
        {
            return await _discogs.GetReleaseGenresAsync(releaseId, mode, cancellationToken).ConfigureAwait(false);
        }

        var masterId = ProviderId(album, "DiscogsMaster");
        if (masterId is not null)
        {
            return await _discogs.GetMasterGenresAsync(masterId, mode, cancellationToken).ConfigureAwait(false);
        }

        var musicBrainzRelease = ProviderId(album, "MusicBrainzAlbum");
        if (musicBrainzRelease is not null)
        {
            var pointer = await _musicBrainz.FindDiscogsPointerAsync(musicBrainzRelease, releaseGroup: false, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(pointer))
            {
                return await _discogs.GetPointerGenresAsync(pointer, mode, cancellationToken).ConfigureAwait(false);
            }
        }

        var musicBrainzGroup = ProviderId(album, "MusicBrainzReleaseGroup");
        if (musicBrainzGroup is null)
        {
            return [];
        }

        var groupPointer = await _musicBrainz.FindDiscogsPointerAsync(musicBrainzGroup, releaseGroup: true, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(groupPointer)
            ? []
            : await _discogs.GetPointerGenresAsync(groupPointer, mode, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync(BaseItem item, IReadOnlyList<string> genres, PluginConfiguration config, ApplyStats stats, CancellationToken cancellationToken)
    {
        var trimmed = Trim(genres, config.MaxGenres);
        if (trimmed.Count == 0)
        {
            stats.SkippedEmpty++;
            return;
        }

        if (item.LockedFields.Contains(MetadataField.Genres))
        {
            stats.SkippedLocked++;
            return;
        }

        var current = item.Genres ?? [];
        if (Same(current, trimmed))
        {
            stats.Unchanged++;
            return;
        }

        if (config.KeepOriginalGenres)
        {
            _memory.RememberOriginal(item.Id, current);
        }

        item.Genres = trimmed.ToArray();
        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        _memory.RememberApplied(item.Id, trimmed);
        stats.Updated++;
    }

    private List<BaseItem> Load(BaseItemKind kind)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [kind],
            Recursive = true
        });

        var list = new List<BaseItem>();
        foreach (var item in items)
        {
            if (!item.IsVirtualItem)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static IEnumerable<MusicAlbum> AlbumsForArtist(MusicArtist artist, IReadOnlyList<BaseItem> albums)
    {
        foreach (var item in albums)
        {
            if (item is MusicAlbum album && BelongsTo(album, artist.Name))
            {
                yield return album;
            }
        }
    }

    private static bool BelongsTo(MusicAlbum album, string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return false;
        }

        return ContainsName(album.AlbumArtists, artistName) || ContainsName(album.Artists, artistName);
    }

    private static bool ContainsName(IReadOnlyList<string>? names, string artistName)
    {
        if (names is null)
        {
            return false;
        }

        foreach (var name in names)
        {
            if (string.Equals(name, artistName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static MusicAlbum? FindAlbum(BaseItem item)
    {
        for (var parent = item.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is MusicAlbum album)
            {
                return album;
            }
        }

        return null;
    }

    private static string? ProviderId(BaseItem item, string key)
    {
        if (item.ProviderIds is not null
            && item.ProviderIds.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return null;
    }

    private static IReadOnlyList<string> Aggregate(IEnumerable<IReadOnlyList<string>> lists)
    {
        var counts = new Dictionary<string, (int Count, string Name)>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in lists)
        {
            foreach (var name in list)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (counts.TryGetValue(name, out var existing))
                {
                    counts[name] = (existing.Count + 1, existing.Name);
                }
                else
                {
                    counts[name] = (1, name.Trim());
                }
            }
        }

        return counts.Values
            .OrderByDescending(entry => entry.Count)
            .Select(entry => entry.Name)
            .ToArray();
    }

    private static IReadOnlyList<string> Trim(IReadOnlyList<string> genres, int max)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var genre in genres)
        {
            if (string.IsNullOrWhiteSpace(genre) || !seen.Add(genre))
            {
                continue;
            }

            list.Add(genre.Trim());
            if (max > 0 && list.Count >= max)
            {
                break;
            }
        }

        return list;
    }

    private static bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static PluginConfiguration CurrentConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private sealed class ApplyStats
    {
        public int Updated { get; set; }

        public int Unchanged { get; set; }

        public int SkippedEmpty { get; set; }

        public int SkippedLocked { get; set; }
    }
}
