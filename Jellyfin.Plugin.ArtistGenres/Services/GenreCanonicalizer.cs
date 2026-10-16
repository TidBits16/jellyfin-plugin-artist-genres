namespace Jellyfin.Plugin.ArtistGenres.Services;

/// <summary>
/// Folds the spellings already in this library into one genre name.
/// </summary>
internal static class GenreCanonicalizer
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["game"] = ["video game music"],
        ["video game"] = ["video game music"],
        ["video games"] = ["video game music"],
        ["film score"] = ["soundtrack"],
        ["film scores"] = ["soundtrack"],
        ["film soundtrack"] = ["soundtrack"],
        ["film soundtracks"] = ["soundtrack"],
        ["soundtracks"] = ["soundtrack"],
        ["tv series"] = ["soundtrack"],
        ["humour"] = ["comedy"],
        ["humor"] = ["comedy"],
        ["alternative & indie"] = ["alternative", "indie"],
        ["alternative and indie"] = ["alternative", "indie"],
        ["alterantive"] = ["alternative"],
        ["elecronic"] = ["electronic"],
        ["electric"] = ["electronic"],
        ["insturmental"] = ["instrumental"],
        ["hip-hop"] = ["hip hop"],
        ["hiphop"] = ["hip hop"],
        ["darkwave"] = ["dark wave"],
        ["ai-generated"] = ["ai generated"],
        ["ai generated"] = ["ai generated"],
        ["classical: world"] = ["classical"],
        ["r & b"] = ["r&b"],
        ["rhythm and blues"] = ["r&b"],
    };

    /// <summary>
    /// One expanded genre name, and whether it came from the alias list rather than a plain case fold.
    /// </summary>
    internal readonly record struct Entry(string Name, bool FromAlias);

    /// <summary>
    /// Expands each raw genre into canonical names. Compound values split on semicolons.
    /// </summary>
    public static IReadOnlyList<Entry> Expand(IEnumerable<string>? names)
    {
        var list = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names is null)
        {
            return list;
        }

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            foreach (var piece in Split(name.Trim()))
            {
                if (Aliases.TryGetValue(piece, out var mapped))
                {
                    foreach (var alias in mapped)
                    {
                        Add(list, seen, alias, fromAlias: true);
                    }
                }
                else
                {
                    Add(list, seen, piece.ToLowerInvariant(), fromAlias: false);
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Returns the canonical names in order, without the alias flag.
    /// </summary>
    public static IReadOnlyList<string> Canonicalize(IEnumerable<string>? names)
    {
        var expanded = Expand(names);
        var list = new string[expanded.Count];
        for (var index = 0; index < expanded.Count; index++)
        {
            list[index] = expanded[index].Name;
        }

        return list;
    }

    private static IEnumerable<string> Split(string value)
    {
        if (value.Contains(';') || value.Contains('|'))
        {
            return value.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return [value];
    }

    private static void Add(List<Entry> list, HashSet<string> seen, string name, bool fromAlias)
    {
        if (name.Length == 0 || !seen.Add(name))
        {
            return;
        }

        list.Add(new Entry(name, fromAlias));
    }
}
