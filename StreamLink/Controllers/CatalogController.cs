using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamLink.Api;
using StreamLink.Services;

namespace StreamLink.Controllers;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = ApiAuthenticationHandler.SchemeName)]
public sealed class CatalogController(IXtreamApiService xtream, ICatalogService catalog) : ControllerBase
{
    /// <summary>List live TV categories.</summary>
    /// <remarks>Search matches names case-insensitively. Sort: provider (default), name, or name-desc.</remarks>
    [HttpGet("categories", Name = "GetApiCategories")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> GetCategories([FromQuery] string? search, [FromQuery] string? sort, CancellationToken cancellationToken)
    {
        sort ??= "provider";
        if (!ValidCatalogQuery(search, sort)) return Problem(statusCode: 400, title: "Invalid search or sort.");
        var categories = await xtream.GetCategoriesAsync(ApiValues.Session(HttpContext), cancellationToken);
        return Ok(CatalogSort.Apply(categories, item => item.Name, search, sort)
            .Select(item => new CategoryResponse(item.Id, item.Name)).ToArray());
    }

    /// <summary>List live TV channels.</summary>
    /// <remarks>categoryId is required. Search is optional. Sort: provider (default), name, or name-desc.</remarks>
    [HttpGet("channels", Name = "GetApiChannels")]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelResponse>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> GetChannels([FromQuery, Required] string categoryId, [FromQuery] string? search,
        [FromQuery] string? sort, CancellationToken cancellationToken)
    {
        sort ??= "provider";
        if (!ValidCatalogQuery(search, sort) || string.IsNullOrWhiteSpace(categoryId) || categoryId.Length > 256)
            return Problem(statusCode: 400, title: "Invalid category, search, or sort.");
        var channels = await xtream.GetChannelsAsync(ApiValues.Session(HttpContext), categoryId, cancellationToken);
        return Ok(CatalogSort.Apply(channels, item => item.Name, search, sort).Select(ApiValues.Channel).ToArray());
    }

    /// <summary>Find a channel by stream ID.</summary>
    /// <remarks>categoryId is required to look up the channel within its category.</remarks>
    [HttpGet("channels/{streamId:int}", Name = "GetApiChannel")]
    [ProducesResponseType(typeof(ChannelResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> GetChannel(int streamId, [FromQuery, Required] string categoryId, CancellationToken cancellationToken)
    {
        if (streamId <= 0 || string.IsNullOrWhiteSpace(categoryId) || categoryId.Length > 256)
            return Problem(statusCode: 400, title: "Invalid channel or category ID.");
        var result = await catalog.FindChannelAsync(ApiValues.Session(HttpContext), streamId, categoryId, cancellationToken);
        return result is null ? Problem(statusCode: 404, title: "Channel not found.") : Ok(ApiValues.Channel(result.Channel));
    }

    private static bool ValidCatalogQuery(string? search, string sort) =>
        (search is null || search.Length <= 256) && CatalogSort.IsSupported(sort);
}
