namespace StreamLink.Services;

public sealed class ProviderAccessPolicy
{
    private readonly HashSet<string> allowedOrigins;

    public ProviderAccessPolicy(IConfiguration configuration)
    {
        allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuration.GetSection("ProviderSecurity:AllowedOrigins").GetChildren())
        {
            var value = entry.Value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !IsHttpUri(uri) || uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("ProviderSecurity:AllowedOrigins must contain only HTTP or HTTPS origins without paths, credentials, queries, or fragments.");
            allowedOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
        }
    }

    public bool IsAllowed(Uri uri) =>
        IsHttpUri(uri) && string.IsNullOrEmpty(uri.Fragment) &&
        allowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));

    public void EnsureAllowedBase(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.Query) || !IsAllowed(uri))
            throw new ProviderAccessException();
    }

    private static bool IsHttpUri(Uri uri) =>
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
}

public sealed class ProviderAccessException() : InvalidOperationException(
    "This provider address is not approved. Ask the server administrator to configure the approved provider origins.");
