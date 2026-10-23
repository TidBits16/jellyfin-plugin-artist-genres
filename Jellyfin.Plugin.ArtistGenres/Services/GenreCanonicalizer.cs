namespace Jellyfin.Plugin.ArtistGenres.Services;

/// <summary>
/// Folds spellings into MusicBrainz names and adds a broader parent so filters can find related songs.
/// </summary>
internal static class GenreCanonicalizer
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["game"] = ["video game music"],
        ["video game"] = ["video game music"],
        ["video games"] = ["video game music"],
        ["videogame"] = ["video game music"],
        ["vgm"] = ["video game music"],
        ["ost"] = ["soundtrack"],
        ["original soundtrack"] = ["soundtrack"],
        ["film score"] = ["soundtrack"],
        ["film scores"] = ["soundtrack"],
        ["film soundtrack"] = ["soundtrack"],
        ["film soundtracks"] = ["soundtrack"],
        ["soundtracks"] = ["soundtrack"],
        ["tv series"] = ["soundtrack"],
        ["score"] = ["soundtrack"],
        ["humour"] = ["comedy"],
        ["humor"] = ["comedy"],
        ["alternative & indie"] = ["alternative", "indie"],
        ["alternative and indie"] = ["alternative", "indie"],
        ["alterantive"] = ["alternative"],
        ["elecronic"] = ["electronic"],
        ["electric"] = ["electronic"],
        ["edm"] = ["electronic"],
        ["insturmental"] = ["instrumental"],
        ["hip-hop"] = ["hip hop"],
        ["hiphop"] = ["hip hop"],
        ["rap"] = ["hip hop"],
        ["darkwave"] = ["dark wave"],
        ["ai-generated"] = ["ai generated"],
        ["ai generated"] = ["ai generated"],
        ["classical: world"] = ["classical"],
        ["r & b"] = ["r&b"],
        ["r and b"] = ["r&b"],
        ["rn b"] = ["r&b"],
        ["rnb"] = ["r&b"],
        ["rhythm and blues"] = ["r&b"],
        ["synthpop"] = ["synth-pop"],
        ["synth pop"] = ["synth-pop"],
        ["alt pop"] = ["alt-pop"],
        ["kpop"] = ["k-pop"],
        ["k pop"] = ["k-pop"],
        ["jpop"] = ["j-pop"],
        ["j pop"] = ["j-pop"],
        ["electropop"] = ["electropop"],
        ["electro pop"] = ["electropop"],
        ["drum & bass"] = ["drum and bass"],
        ["drum n bass"] = ["drum and bass"],
        ["dnb"] = ["drum and bass"],
        ["xmas"] = ["christmas"],
        ["x-mas"] = ["christmas"],
    };

    private static readonly Dictionary<string, string[]> CompactAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hiphop"] = ["hip hop"],
        ["synthpop"] = ["synth-pop"],
        ["altpop"] = ["alt-pop"],
        ["kpop"] = ["k-pop"],
        ["jpop"] = ["j-pop"],
        ["rnb"] = ["r&b"],
        ["randb"] = ["r&b"],
        ["dnb"] = ["drum and bass"],
        ["drumnbass"] = ["drum and bass"],
        ["videogame"] = ["video game music"],
        ["videogames"] = ["video game music"],
        ["videogamemusic"] = ["video game music"],
    };

    private static readonly Dictionary<string, string[]> Parents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["indie pop"] = ["indie", "pop"],
        ["indie rock"] = ["indie", "rock"],
        ["indie folk"] = ["indie", "folk"],
        ["alternative rock"] = ["alternative", "rock"],
        ["alternative pop"] = ["alternative", "pop"],
        ["alt-pop"] = ["pop"],
        ["christian hip hop"] = ["hip hop"],
        ["christian rock"] = ["rock"],
        ["pop rock"] = ["pop", "rock"],
        ["pop rap"] = ["hip hop", "pop"],
        ["folk rock"] = ["folk", "rock"],
        ["folk pop"] = ["folk", "pop"],
        ["country rock"] = ["country", "rock"],
        ["video game music"] = ["soundtrack"],
        ["chiptune"] = ["electronic"],
        ["synth-pop"] = ["pop", "electronic"],
        ["electropop"] = ["pop", "electronic"],
        ["electro"] = ["electronic"],
        ["electro house"] = ["electronic"],
        ["house"] = ["electronic"],
        ["dubstep"] = ["electronic"],
        ["future bass"] = ["electronic"],
        ["drum and bass"] = ["electronic"],
        ["modern classical"] = ["classical"],
        ["orchestral"] = ["classical"],
        ["vocal jazz"] = ["jazz"],
        ["trap"] = ["hip hop"],
        ["conscious hip hop"] = ["hip hop"],
        ["emo rap"] = ["hip hop"],
        ["bedroom pop"] = ["pop"],
        ["art pop"] = ["pop"],
        ["dream pop"] = ["pop"],
        ["rock musical"] = ["musical"],
        ["k-pop"] = ["pop"],
        ["j-pop"] = ["pop"],
    };

    /// <summary>
    /// One expanded genre name, and whether it came from the alias list rather than a plain case fold.
    /// </summary>
    internal readonly record struct Entry(string Name, bool FromAlias);

    /// <summary>
    /// Expands each raw genre into canonical names, then broader parents. Compound values split on semicolons.
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
                var mapped = Map(piece);
                foreach (var alias in mapped.Names)
                {
                    Add(list, seen, alias, mapped.FromAlias);
                    if (!Parents.TryGetValue(alias, out var parents))
                    {
                        continue;
                    }

                    foreach (var parent in parents)
                    {
                        Add(list, seen, parent, fromAlias: true);
                    }
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

    private static (IReadOnlyList<string> Names, bool FromAlias) Map(string piece)
    {
        var key = Key(piece);
        if (key.Length == 0)
        {
            return ([], false);
        }

        if (Aliases.TryGetValue(key, out var mapped))
        {
            return (mapped, true);
        }

        var compact = Compact(key);
        if (compact.Length > 0 && CompactAliases.TryGetValue(compact, out mapped))
        {
            return (mapped, true);
        }

        return ([key], false);
    }

    private static string Key(string value)
    {
        var chars = value.Trim().ToLowerInvariant().ToCharArray();
        var written = 0;
        var spaced = false;
        foreach (var character in chars)
        {
            if (character is '_' or '\t')
            {
                if (written > 0)
                {
                    spaced = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (written > 0)
                {
                    spaced = true;
                }

                continue;
            }

            if (spaced)
            {
                chars[written++] = ' ';
                spaced = false;
            }

            chars[written++] = character;
        }

        return new string(chars, 0, written);
    }

    private static string Compact(string key)
    {
        var chars = new char[key.Length];
        var written = 0;
        foreach (var character in key)
        {
            if (character is ' ' or '-' or '&')
            {
                continue;
            }

            chars[written++] = character;
        }

        return new string(chars, 0, written);
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
