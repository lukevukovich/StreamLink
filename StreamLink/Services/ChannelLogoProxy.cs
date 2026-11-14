using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace StreamLink.Services;

public sealed record ChannelLogoImage(byte[] Content, string ContentType);

public sealed class ChannelLogoProxy(IDataProtectionProvider protection, ProviderAccessPolicy providerAccess, IHttpClientFactory clients)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private readonly ITimeLimitedDataProtector grants = protection.CreateProtector("channel-logo-v1").ToTimeLimitedDataProtector();
    private readonly SemaphoreSlim slots = new(12, 12);

    public string? GetSource(string? logoUrl)
    {
        if (logoUrl is null || logoUrl.Length > 4096 ||
            !Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !providerAccess.IsAllowed(uri))
            return logoUrl;

        return $"/channel-logo/{grants.Protect(uri.AbsoluteUri, TimeSpan.FromHours(1))}";
    }

    public async Task<ChannelLogoImage?> FetchAsync(string grant, CancellationToken cancellationToken)
    {
        if (grant.Length > 8192) return null;
        string source;
        try { source = grants.Unprotect(grant); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException) { return null; }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !providerAccess.IsAllowed(uri))
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await slots.WaitAsync(timeout.Token);
            try
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await clients.CreateClient("ChannelLogo").GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
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
