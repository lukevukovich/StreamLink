using Microsoft.AspNetCore.Mvc;
using StreamLink.Services;

namespace StreamLink.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/external-link")]
public sealed class ExternalLinkController(ExternalLinkService links) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Resolve([FromBody] ExternalLinkRequest request, CancellationToken cancellationToken)
    {
        if (request.Grant is null || request.Grant.Length > 8192 || !Request.Headers.TryGetValue("Origin", out var origin) ||
            !Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var source) ||
            source.GetLeftPart(UriPartial.Authority) != $"{Request.Scheme}://{Request.Host}")
            return BadRequest();
        var url = await links.ResolveAsync(request.Grant, cancellationToken);
        return url is null ? UnprocessableEntity() : new JsonResult(new { url });
    }
}

public sealed record ExternalLinkRequest(string? Grant);
