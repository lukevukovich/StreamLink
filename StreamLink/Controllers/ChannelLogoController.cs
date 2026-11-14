using Microsoft.AspNetCore.Mvc;
using StreamLink.Services;

namespace StreamLink.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("channel-logo")]
public sealed class ChannelLogoController(ChannelLogoProxy logos, ILogger<ChannelLogoController> logger) : ControllerBase
{
    [HttpGet("{grant}")]
    public async Task<IActionResult> Get(string grant, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        try
        {
            var image = await logos.FetchAsync(grant, cancellationToken);
            return image is null ? NotFound() : File(image.Content, image.ContentType);
        }
        catch (ChannelLogoFetchException)
        {
            logger.LogWarning("An approved channel image could not be loaded.");
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
