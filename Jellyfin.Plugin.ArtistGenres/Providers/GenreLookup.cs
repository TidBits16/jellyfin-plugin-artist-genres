using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jellyfin.Plugin.ArtistGenres.Configuration;
using Jellyfin.Plugin.ArtistGenres.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtistGenres.Providers;

/// <summary>
/// Reads genre names from MusicBrainz.
/// </summary>
public sealed class MusicBrainzGenreClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly RequestPacer _pacer;

    private readonly GenreCache _cache;

    private readonly ILogger<MusicBrainzGenreClient> _logger;

    private static readonly HashSet<string> TagDenylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "seen live", "favorites", "favourite", "favourites", "check", "beautiful",
        "love", "awesome", "albums i own", "all", "good", "sad", "happy", "chill",
        "relax", "party", "sexy", "funny", "spotify", "youtube", "american",
        "british", "canadian", "male vocalists", "female vocalists", "under 2000 listeners"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzGenreClient"/> class.
    /// </summary>
    public MusicBrainzGenreClient(RequestPacer pacer, GenreCache cache, ILogger<MusicBrainzGenreClient> logger)
    {
        _pacer = pacer;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Gets artist genres, most-voted first, dropping any below the vote floor.
    /// </summary>
    public Task<IReadOnlyList<string>> GetArtistGenresAsync(string artistId, int minimumVotes, CancellationToken cancellationToken)
    {
        return GetGenresAsync("artist", artistId, minimumVotes, cancellationToken);
    }

    /// <summary>
    /// Gets release-group genres.
    /// </summary>
    public Task<IReadOnlyList<string>> GetReleaseGroupGenresAsync(string releaseGroupId, int minimumVotes, CancellationToken cancellationToken)
    {
        return GetGenresAsync("release-group", releaseGroupId, minimumVotes, cancellationToken);
    }

    /// <summary>
    /// Gets release genres.
    /// </summary>
    public Task<IReadOnlyList<string>> GetReleaseGenresAsync(string releaseId, int minimumVotes, CancellationToken cancellationToken)
    {
        return GetGenresAsync("release", releaseId, minimumVotes, cancellationToken);
    }

    /// <summary>
    /// Gets recording genres.
    /// </summary>
    public Task<IReadOnlyList<string>> GetRecordingGenresAsync(string recordingId, int minimumVotes, CancellationToken cancellationToken)
    {
        return GetGenresAsync("recording", recordingId, minimumVotes, cancellationToken);
    }

    /// <summary>
    /// Finds a Discogs release or master linked from a MusicBrainz release or release group.
    /// The result is <c>release:123</c> or <c>master:123</c>, or null when there is no link.
    /// </summary>
    public async Task<string?> FindDiscogsPointerAsync(string musicBrainzId, bool releaseGroup, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(musicBrainzId, out _))
        {
            return null;
        }

        var key = (releaseGroup ? "mb-rg-discogs:" : "mb-rel-discogs:") + musicBrainzId;
        if (_cache.TryGetPointer(key, out var cached))
        {
            return cached;
        }

        var entity = releaseGroup ? "release-group" : "release";
        var url = BuildUrl($"{entity}/{musicBrainzId}?inc=url-rels&fmt=json");
        if (url is null)
        {
            return null;
        }

        var body = await GetBodyAsync(url, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        var pointer = ParseDiscogsPointer(body);
        _cache.SetPointer(key, pointer);
        return pointer;
    }

    private async Task<IReadOnlyList<string>> GetGenresAsync(string entity, string musicBrainzId, int minimumVotes, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(musicBrainzId, out _))
        {
            return [];
        }

        var votes = Math.Max(0, minimumVotes);
        var key = $"mb:{entity}:{musicBrainzId}:{votes}:gt";
        if (_cache.TryGetGenres(key, out var cached))
        {
            return cached;
        }

        var url = BuildUrl($"{entity}/{musicBrainzId}?inc=genres+tags&fmt=json");
        if (url is null)
        {
            return [];
        }

        var body = await GetBodyAsync(url, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return [];
        }

        var names = ParseGenres(body, votes);
        if (names.Count == 0)
        {
            names = ParseTags(body, votes);
        }

        _cache.SetGenres(key, names);
        return names;
    }

    private static IReadOnlyList<string> ParseGenres(string body, int minimumVotes)
    {
        return ParseNamedCounts(body, "genres", minimumVotes, skipDenylist: false);
    }

    private static IReadOnlyList<string> ParseTags(string body, int minimumVotes)
    {
        return ParseNamedCounts(body, "tags", minimumVotes, skipDenylist: true);
    }

    private static IReadOnlyList<string> ParseNamedCounts(string body, string property, int minimumVotes, bool skipDenylist)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var ranked = new List<(int Count, string Name)>();
            foreach (var entry in values.EnumerateArray())
            {
                if (!entry.TryGetProperty("name", out var nameProperty))
                {
                    continue;
                }

                var name = nameProperty.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var trimmed = name.Trim();
                if (skipDenylist && IsJunkTag(trimmed))
                {
                    continue;
                }

                var count = entry.TryGetProperty("count", out var countProperty) && countProperty.TryGetInt32(out var parsed)
                    ? parsed
                    : 0;
                if (count < minimumVotes)
                {
                    continue;
                }

                ranked.Add((count, trimmed));
            }

            return ranked
                .OrderByDescending(entry => entry.Count)
                .Select(entry => entry.Name)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsJunkTag(string name)
    {
        if (TagDenylist.Contains(name) || name.Length > 40)
        {
            return true;
        }

        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && name.Length <= 5
            && int.TryParse(name.AsSpan(0, name.Length - 1), out _))
        {
            return true;
        }

        return name.Contains("listener", StringComparison.OrdinalIgnoreCase)
            || name.Contains("discovered", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseDiscogsPointer(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? master = null;
            foreach (var relation in relations.EnumerateArray())
            {
                if (!relation.TryGetProperty("url", out var urlProperty)
                    || !urlProperty.TryGetProperty("resource", out var resourceProperty))
                {
                    continue;
                }

                var resource = resourceProperty.GetString();
                if (string.IsNullOrWhiteSpace(resource))
                {
                    continue;
                }

                var pointer = DiscogsPointer.FromUrl(resource);
                if (pointer is null)
                {
                    continue;
                }

                if (pointer.StartsWith("release:", StringComparison.Ordinal))
                {
                    return pointer;
                }

                master ??= pointer;
            }

            return master;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? BuildUrl(string relative)
    {
        var server = Plugin.Instance?.Configuration.MusicBrainzServer?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(server) || !Uri.TryCreate(server, UriKind.Absolute, out var serverUri))
        {
            return null;
        }

        if (serverUri.Scheme != Uri.UriSchemeHttps && serverUri.Scheme != Uri.UriSchemeHttp)
        {
            return null;
        }

        return server + "/ws/2/" + relative;
    }

    private async Task<string?> GetBodyAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await _pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return string.Empty;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MusicBrainz returned {StatusCode} for {Url}", (int)response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogWarning("MusicBrainz was busy for {Url}", url);
        return null;
    }

    private static string UserAgent()
    {
        var contact = Plugin.Instance?.Configuration.MusicBrainzContact?.Trim();
        if (string.IsNullOrWhiteSpace(contact))
        {
            return "ArtistGenres/0.1 (Jellyfin plugin)";
        }

        var safe = contact.Replace('(', ' ').Replace(')', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(safe)
            ? "ArtistGenres/0.1 (Jellyfin plugin)"
            : "ArtistGenres/0.1 (" + safe + ")";
    }
}

/// <summary>
/// Reads genre and style names from Discogs.
/// </summary>
public sealed class DiscogsGenreClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly RequestPacer _pacer;

    private readonly GenreCache _cache;

    private readonly ILogger<DiscogsGenreClient> _logger;

    private int _missingTokenWarning;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsGenreClient"/> class.
    /// </summary>
    public DiscogsGenreClient(RequestPacer pacer, GenreCache cache, ILogger<DiscogsGenreClient> logger)
    {
        _pacer = pacer;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Gets genres for a Discogs release.
    /// </summary>
    public Task<IReadOnlyList<string>> GetReleaseGenresAsync(string releaseId, string fieldMode, CancellationToken cancellationToken)
    {
        return GetGenresAsync("releases", releaseId, fieldMode, cancellationToken);
    }

    /// <summary>
    /// Gets genres for a Discogs master release.
    /// </summary>
    public Task<IReadOnlyList<string>> GetMasterGenresAsync(string masterId, string fieldMode, CancellationToken cancellationToken)
    {
        return GetGenresAsync("masters", masterId, fieldMode, cancellationToken);
    }

    /// <summary>
    /// Gets genres for a pointer of the form <c>release:123</c> or <c>master:123</c>.
    /// </summary>
    public Task<IReadOnlyList<string>> GetPointerGenresAsync(string pointer, string fieldMode, CancellationToken cancellationToken)
    {
        var separator = pointer.IndexOf(':');
        if (separator <= 0 || separator >= pointer.Length - 1)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var kind = pointer[..separator];
        var id = pointer[(separator + 1)..];
        return kind switch
        {
            "release" => GetReleaseGenresAsync(id, fieldMode, cancellationToken),
            "master" => GetMasterGenresAsync(id, fieldMode, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<string>>([])
        };
    }

    private async Task<IReadOnlyList<string>> GetGenresAsync(string collection, string id, string fieldMode, CancellationToken cancellationToken)
    {
        if (!IsDigits(id))
        {
            return [];
        }

        var token = Plugin.Instance?.Configuration.DiscogsToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            if (Interlocked.Exchange(ref _missingTokenWarning, 1) == 0)
            {
                _logger.LogWarning("Discogs personal access token is empty, so Discogs genres were skipped.");
            }

            return [];
        }

        var mode = string.IsNullOrWhiteSpace(fieldMode) ? DiscogsFieldModes.Styles : fieldMode;
        var key = $"discogs:{collection}:{id}:{mode}";
        if (_cache.TryGetGenres(key, out var cached))
        {
            return cached;
        }

        var body = await GetBodyAsync($"https://api.discogs.com/{collection}/{id}", token, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return [];
        }

        var names = ParseGenres(body, mode);
        _cache.SetGenres(key, names);
        return names;
    }

    private static IReadOnlyList<string> ParseGenres(string body, string fieldMode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var styles = ReadNames(document.RootElement, "styles");
            var genres = ReadNames(document.RootElement, "genres");
            return fieldMode switch
            {
                DiscogsFieldModes.Genres => genres,
                DiscogsFieldModes.StylesAndGenres => Dedupe(styles.Concat(genres)),
                _ => styles
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadNames(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = value.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        return names;
    }

    private static IReadOnlyList<string> Dedupe(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var name in names)
        {
            if (seen.Add(name))
            {
                list.Add(name);
            }
        }

        return list;
    }

    private async Task<string?> GetBodyAsync(string url, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await _pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent());
            request.Headers.Authorization = new AuthenticationHeaderValue("Discogs", "token=" + token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return string.Empty;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discogs returned {StatusCode} for {Url}", (int)response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogWarning("Discogs was busy for {Url}", url);
        return null;
    }

    private static string UserAgent()
    {
        var contact = Plugin.Instance?.Configuration.MusicBrainzContact?.Trim();
        if (string.IsNullOrWhiteSpace(contact))
        {
            return "ArtistGenres/0.1 (Jellyfin plugin)";
        }

        var safe = contact.Replace('(', ' ').Replace(')', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(safe)
            ? "ArtistGenres/0.1 (Jellyfin plugin)"
            : "ArtistGenres/0.1 (" + safe + ")";
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Pulls a Discogs release or master id out of a Discogs URL.
/// </summary>
internal static class DiscogsPointer
{
    /// <summary>
    /// Returns <c>release:123</c> or <c>master:123</c> when the URL points at one.
    /// </summary>
    public static string? FromUrl(string url)
    {
        var marker = url.IndexOf("discogs.com/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var rest = url[(marker + "discogs.com/".Length)..];
        var kind = rest.StartsWith("release/", StringComparison.OrdinalIgnoreCase) ? "release"
            : rest.StartsWith("master/", StringComparison.OrdinalIgnoreCase) ? "master"
            : null;
        if (kind is null)
        {
            return null;
        }

        var digitsStart = kind.Length + 1;
        var digitsEnd = digitsStart;
        while (digitsEnd < rest.Length && char.IsDigit(rest[digitsEnd]))
        {
            digitsEnd++;
        }

        if (digitsEnd == digitsStart)
        {
            return null;
        }

        return kind + ":" + rest[digitsStart..digitsEnd];
    }
}
