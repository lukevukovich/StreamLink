using StreamLink.Models;

namespace StreamLink.Services;

public static class ChannelLogoUrl
{
    public static string? Resolve(string providerUrl, string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        logoUrl = logoUrl.Trim();
        if (logoUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return logoUrl;

        if (!Uri.TryCreate(providerUrl.TrimEnd('/') + "/", UriKind.Absolute, out var provider) ||
            !Uri.TryCreate(provider, logoUrl, out var resolved) ||
            resolved.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(resolved.UserInfo))
            return null;

        return resolved.AbsoluteUri;
    }

    public static string? ResolveForShare(XtreamSession session, string? logoUrl)
    {
        var resolved = Resolve(session.ServerUrl, logoUrl);
        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.IdnHost.Contains(session.Username, StringComparison.OrdinalIgnoreCase) ||
            uri.IdnHost.Contains(session.Password, StringComparison.OrdinalIgnoreCase) ||
            ExternalLinkService.ContainsCredential(uri, session.Username) ||
            ExternalLinkService.ContainsCredential(uri, session.Password))
            return null;

        return resolved;
    }
}
