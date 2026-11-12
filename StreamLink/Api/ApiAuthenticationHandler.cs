using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Api;

public sealed record ApiSessionFeature(XtreamSession Session);

public sealed class ApiAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiSessionTokens tokens,
    ProviderAccessPolicy providerAccess) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "StreamLinkApi";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());
        var session = tokens.Read(header[7..].Trim());
        if (session is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired API token."));
        try { providerAccess.EnsureAllowedBase(session.ServerUrl); }
        catch (ProviderAccessException) { return Task.FromResult(AuthenticateResult.Fail("Provider is no longer approved.")); }
        Context.Features.Set(new ApiSessionFeature(session));
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, SessionState.GetOwnerKey(session))], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication required").ExecuteAsync(Context);
    }
}
