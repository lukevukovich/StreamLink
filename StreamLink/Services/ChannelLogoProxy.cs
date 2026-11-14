using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace StreamLink.Services;

public sealed record ChannelLogoImage(byte[] Content, string ContentType);

public sealed class ChannelLogoProxy(IDataProtectionProvider protection, ProviderAccessPolicy providerAccess, IHttpClientFactory clients)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private readonly ITimeLimitedDataProtector grants = protection.CreateProtector("channel-logo-v1").ToTimeLimitedDataProtector();
    private readonly SemaphoreSlim slots = new(12, 12);

    public string? GetSource(string? logoUrl, bool requireProxy = false, string? providerUrl = null, bool allowHttpFallback = false)
    {
        if (logoUrl is null || logoUrl.Length > 4096 ||
            !Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            return requireProxy ? null : logoUrl;

        var providerAllowed = IsApprovedProvider(providerUrl);
        if (!providerAllowed && !providerAccess.IsAllowed(uri))
            return requireProxy ? null : logoUrl;
        if (!providerAllowed && uri.Scheme != Uri.UriSchemeHttp && !requireProxy) return logoUrl;

        var grant = new LogoGrant(uri.AbsoluteUri, providerAllowed ? providerUrl : null,
            allowHttpFallback && string.IsNullOrEmpty(uri.Query));
        return $"/channel-logo/{grants.Protect(JsonSerializer.Serialize(grant), TimeSpan.FromHours(1))}";
    }

    public async Task<ChannelLogoImage?> FetchAsync(string grant, CancellationToken cancellationToken)
    {
        if (grant.Length > 8192) return null;
        string source;
        try { source = grants.Unprotect(grant); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException) { return null; }
        LogoGrant? data;
        try { data = source.StartsWith('{') ? JsonSerializer.Deserialize<LogoGrant>(source) : new LogoGrant(source, null, false); }
        catch (JsonException) { return null; }
        if (data is null || !Uri.TryCreate(data.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || (!IsApprovedProvider(data.ProviderUrl) && !providerAccess.IsAllowed(uri)))
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await slots.WaitAsync(timeout.Token);
            try
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await FetchResponseAsync(uri, data.AllowHttpFallback, timeout.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
                    throw new ChannelLogoFetchException();

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[16384];
                int count;
                while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
                {
                    if (buffer.Length + count > MaxBytes) throw new ChannelLogoFetchException();
                    buffer.Write(chunk, 0, count);
                }

                var content = buffer.ToArray();
                var contentType = GetContentType(content, response.Content.Headers.ContentType?.MediaType);
                if (contentType is null) throw new ChannelLogoFetchException();
                return new ChannelLogoImage(content, contentType);
            }
            finally { slots.Release(); }
        }
        catch (HttpRequestException) { throw new ChannelLogoFetchException(); }
        catch (IOException) { throw new ChannelLogoFetchException(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ChannelLogoFetchException(); }
    }

    private bool IsApprovedProvider(string? providerUrl) =>
        Uri.TryCreate(providerUrl, UriKind.Absolute, out var provider) &&
        string.IsNullOrEmpty(provider.Query) && providerAccess.IsAllowed(provider);

    private async Task<HttpResponseMessage> FetchResponseAsync(Uri uri, bool allowHttpFallback, CancellationToken cancellationToken)
    {
        try { return await SendAsync(uri, cancellationToken); }
        catch (HttpRequestException exception) when (allowHttpFallback && uri.Scheme == Uri.UriSchemeHttps && IsTlsFailure(exception))
        {
            var fallback = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttp };
            if (uri.IsDefaultPort) fallback.Port = 80;
            return await SendAsync(fallback.Uri, cancellationToken);
        }
    }

    private Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken) =>
        clients.CreateClient(providerAccess.IsAllowed(uri) ? "ChannelLogo" : "PublicChannelLogo")
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    private static bool IsTlsFailure(HttpRequestException exception) =>
        exception.HttpRequestError == HttpRequestError.SecureConnectionError ||
        exception.InnerException is AuthenticationException;

    private sealed record LogoGrant(string Url, string? ProviderUrl, bool AllowHttpFallback);

    private static string? GetContentType(ReadOnlySpan<byte> content, string? contentType)
    {
        if (content.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (content.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8)) return "image/gif";
        if (content.Length >= 12 && content.StartsWith("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (content.StartsWith("BM"u8)) return "image/bmp";
        if (content.StartsWith(new byte[] { 0, 0, 1, 0 })) return "image/x-icon";
        if (content.Length >= 12 && content.Slice(4, 4).SequenceEqual("ftyp"u8) &&
            (content.Slice(8, 4).SequenceEqual("avif"u8) || content.Slice(8, 4).SequenceEqual("avis"u8))) return "image/avif";
        return contentType?.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase) == true ? "image/svg+xml" : null;
    }
}

public sealed class ChannelLogoFetchException() : Exception("The channel image could not be loaded.");
