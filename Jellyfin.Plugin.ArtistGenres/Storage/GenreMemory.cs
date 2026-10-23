using System.Text.Json;

namespace Jellyfin.Plugin.ArtistGenres.Storage;

/// <summary>
/// Keeps the genres that were on each item before the first apply, and the genres last written.
/// </summary>
public sealed class GenreMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();

    private readonly string _path;

    private GenreMemoryFile _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenreMemory"/> class.
    /// </summary>
    public GenreMemory()
    {
        _path = Path.Combine(DataDirectory(), "genre-memory.json");
        _file = Load(_path);
    }

    /// <summary>
    /// Stores the item's genres the first time they are about to be replaced.
    /// </summary>
    public void RememberOriginal(Guid itemId, IReadOnlyList<string> genres)
    {
        lock (_gate)
        {
            var key = itemId.ToString("N");
            if (_file.Original.ContainsKey(key))
            {
                return;
            }

            _file.Original[key] = genres.ToArray();
        }
    }

    /// <summary>
    /// Stores the genre list written for an item.
    /// </summary>
    public void RememberApplied(Guid itemId, IReadOnlyList<string> genres)
    {
        lock (_gate)
        {
            _file.Applied[itemId.ToString("N")] = genres.ToArray();
        }
    }

    /// <summary>
    /// Returns the genres stored before the first apply.
    /// </summary>
    public IReadOnlyList<string> OriginalFor(Guid itemId)
    {
        lock (_gate)
        {
            return _file.Original.TryGetValue(itemId.ToString("N"), out var genres)
                ? genres
                : [];
        }
    }

    /// <summary>
    /// Returns the genre lists last written, keyed by item id.
    /// </summary>
    public IReadOnlyList<(Guid ItemId, string[] Genres)> AppliedSnapshot()
    {
        lock (_gate)
        {
            var list = new List<(Guid, string[])>(_file.Applied.Count);
            foreach (var (key, genres) in _file.Applied)
            {
                if (Guid.TryParseExact(key, "N", out var id))
                {
                    list.Add((id, genres));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Writes the memory file to disk.
    /// </summary>
    public void Save()
    {
        GenreMemoryFile snapshot;
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

    private static GenreMemoryFile Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<GenreMemoryFile>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    loaded.Original ??= new Dictionary<string, string[]>(StringComparer.Ordinal);
                    loaded.Applied ??= new Dictionary<string, string[]>(StringComparer.Ordinal);
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

        return new GenreMemoryFile();
    }

    private static string DataDirectory()
    {
        return Plugin.Instance?.GetDataDirectory()
            ?? Path.Combine(Path.GetTempPath(), "jellyfin-artist-genres");
    }
}

/// <summary>
/// On-disk record of original and applied genres.
/// </summary>
public sealed class GenreMemoryFile
{
    public Dictionary<string, string[]> Original { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string[]> Applied { get; set; } = new(StringComparer.Ordinal);
}
