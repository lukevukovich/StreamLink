using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Api;

public interface IApiSessionTokens
{
    (string Token, DateTimeOffset ExpiresAt) Issue(XtreamSession session);
    XtreamSession? Read(string token);
}

public sealed class ApiSessionTokens(IDataProtectionProvider protection) : IApiSessionTokens
{
    private readonly ITimeLimitedDataProtector protector = protection.CreateProtector("StreamLink.Api.Session.v1").ToTimeLimitedDataProtector();

    public (string Token, DateTimeOffset ExpiresAt) Issue(XtreamSession session)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var token = protector.Protect(JsonSerializer.Serialize(session), expiresAt);
        if (token.Length > 16384) throw new ProviderResponseException();
        return (token, expiresAt);
    }

    public XtreamSession? Read(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384) return null;
        try
        {
            var session = JsonSerializer.Deserialize<XtreamSession>(protector.Unprotect(token));
            return session is { UserInfo.Auth: 1 } && !string.IsNullOrWhiteSpace(session.ServerUrl) &&
                !string.IsNullOrWhiteSpace(session.Username) && !string.IsNullOrWhiteSpace(session.Password)
                ? session : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }
}
