using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace StreamLink.Services;

// Resolves a provider redirect without downloading or forwarding the media body.
public sealed class ExternalLinkService(IDataProtectionProvider protection, IHttpClientFactory clients, IShareLinkService shares)
{
    private readonly ITimeLimitedDataProtector grants = protection.CreateProtector("external-link-grants-v1").ToTimeLimitedDataProtector();

    public string CreateChannelGrant(string url) => grants.Protect(JsonSerializer.Serialize(new Grant(null, url)), TimeSpan.FromMinutes(5));
    public string CreateShareGrant(string token) => grants.Protect(JsonSerializer.Serialize(new Grant(token, null)), TimeSpan.FromMinutes(5));

    public async Task<string?> ResolveAsync(string grantToken, CancellationToken cancellationToken)
    {
        Grant? grant;
        try { grant = JsonSerializer.Deserialize<Grant>(grants.Unprotect(grantToken)); }
        catch (Exception) { return null; }
        if (grant is null) return null;

        // Shares are checked again on every click, not just when the page was opened.
        var source = grant.ShareToken is not null
            ? (await shares.GetPlaybackAsync(grant.ShareToken, cancellationToken))?.StreamUrl
            : grant.ChannelUrl;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var original) || original.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(original.UserInfo) || !TryGetCredentials(original, out var username, out var password)) return null;

        try
        {
            var client = clients.CreateClient("ExternalLinkRedirect");
            using var request = new HttpRequestMessage(HttpMethod.Get, original);
            // Redirects and bodies must never be followed/read by the app.
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is < 300 or > 399 || response.Headers.Location is null) return null;
            var target = new Uri(original, response.Headers.Location);
            if (target.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(target.UserInfo) ||
                target == original || target.Fragment.Length != 0 ||
                ContainsCredential(target, username) || ContainsCredential(target, password)) return null;
            return target.AbsoluteUri;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryGetCredentials(Uri uri, out string username, out string password)
    {
        username = password = "";
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !parts[0].Equals("live", StringComparison.OrdinalIgnoreCase)) return false;
        username = Uri.UnescapeDataString(parts[1]);
        password = Uri.UnescapeDataString(parts[2]);
        return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
    }

    private static bool ContainsCredential(Uri uri, string secret)
    {
        var text = uri.PathAndQuery;
        for (var i = 0; i < 3; i++)
        {
            if (text.Contains(secret, StringComparison.OrdinalIgnoreCase)) return true;
            var decoded = Uri.UnescapeDataString(text);
            if (decoded == text) break;
            text = decoded;
        }
        return false;
    }

    private sealed record Grant(string? ShareToken, string? ChannelUrl);
}