namespace Jellyfin.Plugin.ArtistGenres.Services;

/// <summary>
/// The short list of fundamental genre names the plugin will write.
/// </summary>
internal static class GenreLexicon
{
    private static readonly Dictionary<string, string> Canonical = Load();

    /// <summary>
    /// Returns the standardized spelling when <paramref name="name"/> is a fundamental genre.
    /// </summary>
    public static string? CanonicalName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Canonical.TryGetValue(name.Trim(), out var value) ? value : null;
    }

    /// <summary>
    /// True when the name is one of the fundamental genres written onto the library.
    /// </summary>
    public static bool IsAllowed(string? name)
    {
        return CanonicalName(name) is not null;
    }

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(GenreLexicon).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith("extra-genres.txt", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                var name = line.Trim();
                if (name.Length > 0)
                {
                    map[name] = name;
                }
            }
        }

        return map;
    }
}
