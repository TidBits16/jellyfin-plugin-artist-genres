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
        ["rap"] = ["rap"],
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
        ["alt pop"] = ["alternative pop"],
        ["kpop"] = ["k-pop"],
        ["k pop"] = ["k-pop"],
        ["jpop"] = ["j-pop"],
        ["j pop"] = ["j-pop"],
        ["electropop"] = ["electropop"],
        ["electro pop"] = ["electropop"],
        ["drum & bass"] = ["drum and bass"],
        ["drum n bass"] = ["drum and bass"],
        ["dnb"] = ["drum and bass"],
        ["xmas"] = ["christmas music"],
        ["x-mas"] = ["christmas music"],
        ["christmas"] = ["christmas music"],
        ["holiday"] = ["christmas music"],
        ["alt-pop"] = ["alternative pop"],
        ["altpop"] = ["alternative pop"],
        ["bubblegum"] = ["bubblegum pop"],
        ["synthpunk"] = ["electropunk"],
        ["synth-punk"] = ["electropunk"],
        ["reggae gospel"] = ["gospel"],
        ["religious"] = ["christian"],
        ["minecraft"] = ["video game music"],
        ["beyond skyrim"] = ["video game music"],
        ["the elder scrolls"] = ["video game music"],
        ["game mod"] = ["video game music"],
        ["murder drones"] = ["soundtrack"],
        ["acoustic"] = [],
        ["vocal"] = [],
        ["remix"] = [],
        ["single"] = [],
        ["charity"] = [],
        ["protogen"] = [],
        ["voiceroid"] = [],
        ["drum"] = [],
        ["bass"] = [],
        ["artist name on cover"] = [],
        ["character on cover"] = [],
        ["robot on cover"] = [],
        ["animal on cover"] = [],
        ["anthro on cover"] = [],
        ["character: n"] = [],
        ["character: v"] = [],
        ["character: uzi doorman"] = [],
    };

    private static readonly Dictionary<string, string[]> CompactAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hiphop"] = ["hip hop"],
        ["synthpop"] = ["synth-pop"],
        ["altpop"] = ["alternative pop"],
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

    private static readonly HashSet<string> KeepWhole = new(StringComparer.OrdinalIgnoreCase)
    {
        "ballad opera",
        "black metal",
        "christmas music",
        "cumbia chilena",
        "dark wave",
        "death metal",
        "doom metal",
        "dream pop",
        "drum and bass",
        "heavy metal",
        "hip hop",
        "industrial musical",
        "j-pop",
        "k-pop",
        "new wave",
        "no wave",
        "nueva canción chilena",
        "opera-ballet",
        "rock and roll",
        "rock musical",
        "rock opera",
        "synth-pop",
        "thrash metal",
        "western classical",
        "western swing"
    };

    private static readonly Dictionary<string, string[]> Parents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video game music"] = ["soundtrack"],
        ["chiptune"] = ["electronic"],
        ["synth-pop"] = ["pop", "electronic"],
        ["electropop"] = ["pop", "electronic"],
        ["electro"] = ["electronic"],
        ["house"] = ["electronic"],
        ["dubstep"] = ["electronic"],
        ["future bass"] = ["electronic"],
        ["drum and bass"] = ["electronic"],
        ["orchestral"] = ["classical"],
        ["vocal jazz"] = ["jazz"],
        ["trap"] = ["hip hop"],
        ["rock musical"] = ["musical"],
        ["rap"] = ["hip hop"],
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
                    var allowed = GenreLexicon.CanonicalName(alias);
                    if (allowed is null)
                    {
                        continue;
                    }

                    var written = SplitCompound(allowed);
                    var split = written.Count > 1
                        || (written.Count == 1 && !string.Equals(written[0], allowed, StringComparison.OrdinalIgnoreCase));
                    foreach (var nameToWrite in written)
                    {
                        Add(list, seen, nameToWrite, mapped.FromAlias || split);
                        if (!Parents.TryGetValue(nameToWrite, out var parents))
                        {
                            continue;
                        }

                        foreach (var parent in parents)
                        {
                            var parentName = GenreLexicon.CanonicalName(parent);
                            if (parentName is not null)
                            {
                                Add(list, seen, parentName, fromAlias: true);
                            }
                        }
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

    private static IReadOnlyList<string> SplitCompound(string name)
    {
        if (KeepWhole.Contains(name))
        {
            return [name];
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parts = new List<string>();
        if (words.Length >= 2 && TryCover(words, 0, parts, skipFull: true) && parts.Count >= 2)
        {
            return FlattenParts(parts);
        }

        if (words.Length >= 2)
        {
            for (var start = 1; start < words.Length; start++)
            {
                var suffix = string.Join(' ', words[start..]);
                if (!IsPiece(suffix))
                {
                    continue;
                }

                var prefix = string.Join(' ', words[..start]);
                if (IsPiece(prefix))
                {
                    return FlattenParts([prefix, suffix]);
                }

                if (suffix.Contains(' '))
                {
                    return SplitCompound(suffix);
                }
            }
        }

        var hyphen = name.IndexOf('-');
        if (hyphen > 0 && hyphen == name.LastIndexOf('-'))
        {
            var left = name[..hyphen].Trim();
            var right = name[(hyphen + 1)..].Trim();
            if (left.Length >= 2 && right.Length >= 2 && IsPiece(left) && IsPiece(right))
            {
                return FlattenParts([left, right]);
            }
        }

        return [name];
    }

    private static bool TryCover(string[] words, int index, List<string> parts, bool skipFull)
    {
        if (index >= words.Length)
        {
            return parts.Count > 0;
        }

        for (var end = words.Length; end > index; end--)
        {
            if (skipFull && index == 0 && end == words.Length)
            {
                continue;
            }

            var piece = string.Join(' ', words[index..end]);
            if (!IsPiece(piece))
            {
                continue;
            }

            parts.Add(piece);
            if (TryCover(words, end, parts, skipFull: false))
            {
                return true;
            }

            parts.RemoveAt(parts.Count - 1);
        }

        return false;
    }

    private static IReadOnlyList<string> FlattenParts(IReadOnlyList<string> parts)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            foreach (var inner in SplitCompound(ResolveHead(part)))
            {
                foreach (var name in ResolvePiece(inner))
                {
                    if (seen.Add(name))
                    {
                        list.Add(name);
                    }
                }
            }
        }

        return list.Count >= 2 ? list : parts;
    }

    private static string ResolveHead(string piece)
    {
        var names = ResolvePiece(piece);
        return names.Count == 1 ? names[0] : piece;
    }

    private static bool IsPiece(string piece)
    {
        if (GenreLexicon.IsAllowed(piece))
        {
            return true;
        }

        var names = ResolvePiece(piece);
        if (names.Count != 1)
        {
            return false;
        }

        return Compact(names[0]) == Compact(Key(piece));
    }

    private static IReadOnlyList<string> ResolvePiece(string piece)
    {
        var key = Key(piece);
        if (key.Length == 0)
        {
            return [];
        }

        if (Aliases.TryGetValue(key, out var mapped))
        {
            return CanonicalNames(mapped);
        }

        var compact = Compact(key);
        if (compact.Length > 0 && CompactAliases.TryGetValue(compact, out mapped))
        {
            return CanonicalNames(mapped);
        }

        var allowed = GenreLexicon.CanonicalName(piece);
        return allowed is null ? [] : [allowed];
    }

    private static IReadOnlyList<string> CanonicalNames(IReadOnlyList<string> names)
    {
        var list = new List<string>();
        foreach (var name in names)
        {
            var allowed = GenreLexicon.CanonicalName(name);
            if (allowed is not null)
            {
                list.Add(allowed);
            }
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

        return GenreLexicon.IsAllowed(key) ? ([key], false) : ([], false);
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
