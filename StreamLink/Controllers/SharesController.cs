using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamLink.Api;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Controllers;

[ApiController]
[Route("api/shares")]
[Authorize(AuthenticationSchemes = ApiAuthenticationHandler.SchemeName)]
public sealed class SharesController(IShareLinkService shares, ICatalogService catalog) : ControllerBase
{
    /// <summary>List the account's active shares.</summary>
    /// <remarks>Sort: newest (default), oldest, expiring, latest, channel, channel-desc, category, or category-desc.</remarks>
    [HttpGet(Name = "GetApiShares")]
    [ProducesResponseType(typeof(IReadOnlyList<ShareResponse>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<IActionResult> GetShares([FromQuery] string? sort, CancellationToken cancellationToken)
    {
        sort ??= ShareSortOptions.Default;
        if (!ShareSortOptions.IsSupported(sort)) return Problem(statusCode: 400, title: "Invalid share sort order.");
        var links = await shares.GetActiveAsync(ApiValues.Owner(HttpContext), cancellationToken);
        return Ok(ShareSortOptions.Sort(links, sort).Select(ApiValues.Share).ToArray());
    }

    /// <summary>Get an active share owned by this account.</summary>
    [HttpGet("{code}", Name = "GetApiShare")]
    [ProducesResponseType(typeof(ShareResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetShare(string code, CancellationToken cancellationToken)
    {
        var link = await FindOwnedActiveShare(ApiValues.Owner(HttpContext), code, cancellationToken);
        return link is null ? Problem(statusCode: 404, title: "Share not found.") : Ok(ApiValues.Share(link));
    }

    /// <summary>Create or reuse a channel share.</summary>
    /// <remarks>Looks up the channel by streamId and optional categoryId. Returns 201 for a new share or 200 when an active share is reused; reuse keeps its existing expiration.</remarks>
    [HttpPost(Name = "CreateApiShare")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(ShareResponse), 201)]
    [ProducesResponseType(typeof(ShareResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> CreateShare([FromBody] CreateShareRequest request, CancellationToken cancellationToken)
    {
        if (request.StreamId <= 0 || request.ExpirationHours is < 1 or > 24 || request.CategoryId?.Length > 256)
            return Problem(statusCode: 400, title: "Invalid channel, category, or share lifetime.");
        var session = ApiValues.Session(HttpContext);
        var channel = await catalog.FindChannelAsync(session, request.StreamId, request.CategoryId, cancellationToken);
        if (channel is null) return Problem(statusCode: 404, title: "Channel not found.");
        try
        {
            var result = await shares.CreateOrReuseAsync(session, ApiValues.Owner(HttpContext), channel.Channel,
                channel.CategoryName, request.ExpirationHours, cancellationToken);
            var response = ApiValues.Share(result.Link);
            return result.Reused ? Ok(response) : Created($"/api/shares/{result.Link.Token}", response);
        }
        catch (InvalidOperationException exception) when (exception is not ProviderAccessException)
        {
            return Problem(statusCode: 409, title: "The share could not be created: the active share limit may have been reached or no supported stream format is available.");
        }
    }

    /// <summary>Update an active share's expiration.</summary>
    /// <remarks>Resets the share lifetime from now without changing its code.</remarks>
    [HttpPut("{code}/expiration", Name = "UpdateApiShareExpiration")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(ShareResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> UpdateShare(string code, [FromBody] UpdateShareRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpirationHours is < 1 or > 24)
            return Problem(statusCode: 400, title: "Share lifetime must be between 1 and 24 hours.");
        var link = await shares.UpdateExpirationAsync(code, ApiValues.Owner(HttpContext), request.ExpirationHours, cancellationToken);
        return link is null ? Problem(statusCode: 404, title: "Share not found.") : Ok(ApiValues.Share(link));
    }

    /// <summary>Revoke an active share owned by this account.</summary>
    [HttpDelete("{code}", Name = "RevokeApiShare")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> RevokeShare(string code, CancellationToken cancellationToken)
    {
        var owner = ApiValues.Owner(HttpContext);
        if (await FindOwnedActiveShare(owner, code, cancellationToken) is null)
            return Problem(statusCode: 404, title: "Share not found.");
        await shares.RevokeAsync(code, owner, cancellationToken);
        return NoContent();
    }

    private async Task<ShareLink?> FindOwnedActiveShare(string owner, string code, CancellationToken cancellationToken)
    {
        var link = await shares.GetAsync(code, cancellationToken);
        return link is not null && link.OwnerKey == owner && !link.Revoked && link.ExpiresUtc > DateTime.UtcNow ? link : null;
    }
}
