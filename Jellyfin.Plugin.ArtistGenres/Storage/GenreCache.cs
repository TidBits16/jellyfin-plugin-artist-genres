using System.Text.Json;

namespace Jellyfin.Plugin.ArtistGenres.Storage;

/// <summary>
/// Remembers genre lookups so a second apply does not ask MusicBrainz and Discogs again.
/// </summary>
public sealed class GenreCache
{
    private static readonly TimeSpan GenreLifetime = TimeSpan.FromDays(30);

    private static readonly TimeSpan PointerLifetime = TimeSpan.FromDays(180);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();

    private readonly string _path;

    private GenreCacheFile _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenreCache"/> class.
    /// </summary>
    public GenreCache()
    {
        _path = Path.Combine(DataDirectory(), "genre-cache.json");
        _file = Load(_path);
    }

    /// <summary>
    /// Reads a cached genre list. Returns false when the entry is missing or older than 30 days.
    /// </summary>
    public bool TryGetGenres(string key, out IReadOnlyList<string> genres)
    {
        lock (_gate)
        {
            if (_file.Genres.TryGetValue(key, out var entry)
                && DateTimeOffset.UtcNow - entry.FetchedAt < GenreLifetime)
            {
                genres = entry.Names;
                return true;
            }
        }

        genres = [];
        return false;
    }

    /// <summary>
    /// Stores a successful lookup, including a real empty list.
    /// </summary>
    public void SetGenres(string key, IReadOnlyList<string> genres)
    {
        lock (_gate)
        {
            _file.Genres[key] = new CachedGenres
            {
                Names = genres.ToArray(),
                FetchedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Reads a cached Discogs release or master id discovered from MusicBrainz.
    /// </summary>
    public bool TryGetPointer(string key, out string? pointer)
    {
        lock (_gate)
        {
            if (_file.Pointers.TryGetValue(key, out var entry)
                && DateTimeOffset.UtcNow - entry.FetchedAt < PointerLifetime)
            {
                pointer = string.IsNullOrEmpty(entry.Value) ? null : entry.Value;
                return true;
            }
        }

        pointer = null;
        return false;
    }

    /// <summary>
    /// Stores a Discogs pointer. An empty value means MusicBrainz had no Discogs link.
    /// </summary>
    public void SetPointer(string key, string? pointer)
    {
        lock (_gate)
        {
            _file.Pointers[key] = new CachedPointer
            {
                Value = pointer ?? string.Empty,
                FetchedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Writes the cache to disk.
    /// </summary>
    public void Save()
    {
        GenreCacheFile snapshot;
        lock (_gate)
        {
            snapshot = _file;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }

    private static GenreCacheFile Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<GenreCacheFile>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    loaded.Genres ??= new Dictionary<string, CachedGenres>(StringComparer.Ordinal);
                    loaded.Pointers ??= new Dictionary<string, CachedPointer>(StringComparer.Ordinal);
                    return loaded;
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new GenreCacheFile();
    }

    private static string DataDirectory()
    {
        return Plugin.Instance?.GetDataDirectory()
            ?? Path.Combine(Path.GetTempPath(), "jellyfin-artist-genres");
    }
}

/// <summary>
/// On-disk cache document.
/// </summary>
public sealed class GenreCacheFile
{
    public Dictionary<string, CachedGenres> Genres { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, CachedPointer> Pointers { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A cached genre list.
/// </summary>
public sealed class CachedGenres
{
    public string[] Names { get; set; } = [];

    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>
/// A cached Discogs id, or an empty string when the lookup found nothing.
/// </summary>
public sealed class CachedPointer
{
    public string Value { get; set; } = string.Empty;

    public DateTimeOffset FetchedAt { get; set; }
}
