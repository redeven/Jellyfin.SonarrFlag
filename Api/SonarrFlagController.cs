using JellyfinSonarrFlag.Sonarr;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyfinSonarrFlag.Api;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("SonarrFlag")]
public class SonarrFlagController : ControllerBase
{
    private readonly BrokenEpisodeService _service;
    private readonly SonarrClient _sonarr;
    private readonly ILogger<SonarrFlagController> _logger;

    public SonarrFlagController(BrokenEpisodeService service, SonarrClient sonarr, ILogger<SonarrFlagController> logger)
    {
        _service = service;
        _sonarr = sonarr;
        _logger = logger;
    }

    [HttpPost("Broken/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MarkBrokenResult>> MarkBroken([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
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
}

public record TestRequest(string? Url, string? ApiKey);

public record TestResult(bool Ok, string? Version, string? Error);

public record ErrorResponse(string Message);
