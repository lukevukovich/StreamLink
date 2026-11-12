using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StreamLink.Api;
using StreamLink.Services;

namespace StreamLink.Controllers;

[ApiController]
[Route("api")]
public sealed class AccountController(IXtreamApiService xtream, IApiSessionTokens tokens) : ControllerBase
{
    /// <summary>Verify provider credentials and create an API session.</summary>
    /// <remarks>The provider origin must be approved. The bearer token expires after one hour. Use HTTPS.</remarks>
    [HttpPost("sessions", Name = "CreateApiSession")]
    [EnableRateLimiting("api-login")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(SessionResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 429)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ServerUrl) || request.ServerUrl.Length > 2048 ||
            string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 512 ||
            string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 512 ||
            !Uri.TryCreate(request.ServerUrl.Trim(), UriKind.Absolute, out var server) || server.Scheme is not ("http" or "https"))
            return Problem(statusCode: 400, title: "Invalid provider credentials or address.");

        var session = await xtream.LoginAsync(server.ToString(), request.Username.Trim(), request.Password, cancellationToken);
        if (session is null) return Problem(statusCode: 401, title: "Provider credentials were not accepted.");
        var (token, expires) = tokens.Issue(session);
        return Ok(new SessionResponse(token, expires, ApiValues.Account(session)));
    }

    /// <summary>Refresh provider account status.</summary>
    [HttpGet("account", Name = "GetApiAccount")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationHandler.SchemeName)]
    [ProducesResponseType(typeof(AccountResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> GetAccount(CancellationToken cancellationToken)
    {
        var session = await xtream.RefreshAccountAsync(ApiValues.Session(HttpContext), cancellationToken);
        return session is null
            ? Problem(statusCode: 401, title: "Provider credentials were not accepted.")
            : Ok(ApiValues.Account(session));
    }
}
