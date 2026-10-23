using Jellyfin.Plugin.ArtistGenres.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ArtistGenres.Api;

/// <summary>
/// Plugin endpoints used by the settings page.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ArtistGenres")]
public sealed class ArtistGenresController : ControllerBase
{
    private readonly GenreResetRunner _reset;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistGenresController"/> class.
    /// </summary>
    public ArtistGenresController(GenreResetRunner reset)
    {
        _reset = reset;
    }

    /// <summary>
    /// Clears cached lookups and current genres, then fetches them again.
    /// </summary>
    [HttpPost("Reset")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult StartReset()
    {
        if (!_reset.TryStart())
        {
            return Conflict();
        }

        return Accepted();
    }
}
