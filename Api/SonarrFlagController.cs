using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using JellyfinSonarrFlag.Sonarr;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyfinSonarrFlag.Api;

[ApiController]
[Authorize]
[Route("SonarrFlag")]
public class SonarrFlagController : ControllerBase
{
    /// <summary>The claim holding the signed-in user's id. Mirrors Jellyfin.Api's <c>InternalClaimTypes.UserId</c>, which isn't published as a package.</summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    private readonly BrokenEpisodeService _service;
    private readonly SonarrClient _sonarr;
    private readonly IUserManager _userManager;
    private readonly ILogger<SonarrFlagController> _logger;

    public SonarrFlagController(BrokenEpisodeService service, SonarrClient sonarr, IUserManager userManager, ILogger<SonarrFlagController> logger)
    {
        _service = service;
        _sonarr = sonarr;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet("Permission")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PermissionResult> Permission() => new PermissionResult(CanMarkBroken());

    [HttpPost("Broken/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MarkBrokenResult>> MarkBroken([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        if (!CanMarkBroken())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("You are not allowed to mark episodes as broken."));
        }

        try
        {
            return await _service.MarkBrokenAsync(itemId, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokenEpisodeException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse(ex.Message));
        }
        catch (SonarrException ex)
        {
            _logger.LogWarning(ex, "Sonarr request failed while marking {ItemId} as broken.", itemId);
            return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse(ex.Message));
        }
    }

    [HttpPost("Test")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestResult>> Test([FromBody] TestRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return new TestResult(false, null, "Enter both the Sonarr URL and the API key.");
        }

        try
        {
            var status = await _sonarr.GetSystemStatusAsync(new SonarrConnection(request.Url, request.ApiKey), cancellationToken).ConfigureAwait(false);
            return new TestResult(true, status.Version, null);
        }
        catch (SonarrException ex)
        {
            return new TestResult(false, null, ex.Message);
        }
    }

    /// <summary>Administrators can always mark episodes as broken; other users only when they're allowed in the plugin's settings.</summary>
    private bool CanMarkBroken()
    {
        if (!Guid.TryParse(User.FindFirst(UserIdClaim)?.Value, out var userId) || userId == Guid.Empty)
        {
            return false;
        }

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return false;
        }

        return user.HasPermission(PermissionKind.IsAdministrator)
            || (Plugin.Instance?.Configuration.AllowedUserIds.Contains(userId) ?? false);
    }
}

public record PermissionResult(bool CanMarkBroken);

public record TestRequest(string? Url, string? ApiKey);

public record TestResult(bool Ok, string? Version, string? Error);

public record ErrorResponse(string Message);
