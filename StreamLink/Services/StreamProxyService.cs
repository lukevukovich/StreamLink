using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed class StreamProxyService(
    IHttpClientFactory httpClientFactory,
    IShareLinkService shareLinks,
    IStreamUrlService streamUrls,
    SessionPersistence persistence,
    ILogger<StreamProxyService> logger) : IStreamProxyService
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxPlaylistBytes = 2 * 1024 * 1024;

    public string CreatePrivateToken(XtreamSession session, int streamId)
    {
        var links = streamUrls.BuildLinks(session, streamId);
        var grant = new PrivateGrant(links.HlsUrl, DateTimeOffset.UtcNow.AddHours(12), links.TsAllowed ? links.TsUrl : null);
        var protectedGrant = persistence.ProtectString(JsonSerializer.Serialize(grant, jsonOptions));
        return "sl1_" + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedGrant));
    }

    public async Task<StreamProxyResult?> GetManifestAsync(string token, CancellationToken cancellationToken = default)
    {
        var upstreamUrl = await ResolveStreamUrlAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(upstreamUrl) || new Uri(upstreamUrl).AbsolutePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) return null;

        using var response = await GetUpstreamAsync(upstreamUrl, cancellationToken);
        if (response is null) return null;
        if (response.Content.Headers.ContentLength > MaxPlaylistBytes) return null;
        var playlistBytes = await ReadLimitedAsync(response.Content, MaxPlaylistBytes, cancellationToken);
        if (playlistBytes is null) return null;
        var playlist = Encoding.UTF8.GetString(playlistBytes);
        if (!playlist.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#EXTM3U", StringComparison.Ordinal)) return null;
        var finalUrl = response.RequestMessage?.RequestUri ?? new Uri(upstreamUrl);
        var rewritten = RewritePlaylist(playlist, finalUrl, token);
        return new StreamProxyResult(Encoding.UTF8.GetBytes(rewritten), "application/vnd.apple.mpegurl");
    }


    public async Task<StreamProxyResourceResult?> GetResourceAsync(string token, string resourceToken, CancellationToken cancellationToken = default)
    {
        var upstreamUrl = await ResolveStreamUrlAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(upstreamUrl)) return null;

        string targetUrl;
        try
        {
            var protectedTarget = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(resourceToken));
            var payload = persistence.UnprotectString(protectedTarget) ?? "";
            var bound = JsonSerializer.Deserialize<ResourceGrant>(payload, jsonOptions);
            targetUrl = bound is not null && bound.Token == token ? bound.Url : "";
            var target = new Uri(targetUrl);
            // Resource URLs are generated only while rewriting a provider playlist and are
            // protected with Data Protection. Providers may host redirected media on a CDN.
            if (!target.IsAbsoluteUri || target.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(target.Host)) return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Stream resource token validation failed: {ErrorType}", exception.GetType().Name);
            return null;
        }

        var response = await GetUpstreamAsync(targetUrl, cancellationToken);
        if (response is null) return null;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        if (!contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) &&
            !new Uri(targetUrl).AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return new StreamProxyResourceResult(null, contentType, response);
        }
        using (response)
        {
            if (response.Content.Headers.ContentLength > MaxPlaylistBytes) return null;
            var content = await ReadLimitedAsync(response.Content, MaxPlaylistBytes, cancellationToken);
            if (content is null) return null;
            // Rewrite variant, audio and subtitle playlists against their final upstream URL.
            var finalUrl = response.RequestMessage?.RequestUri ?? new Uri(targetUrl);
            if (!Encoding.UTF8.GetString(content.AsSpan(0, Math.Min(content.Length, 32))).TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#EXTM3U", StringComparison.Ordinal)) return null;
            return new StreamProxyResourceResult(Encoding.UTF8.GetBytes(RewritePlaylist(Encoding.UTF8.GetString(content), finalUrl, token)), "application/vnd.apple.mpegurl", null);
        }
    }

    public async Task<StreamProxyLiveResult?> GetLiveTsAsync(string token, CancellationToken cancellationToken = default)
    {
        string? tsUrl;
        if (token.StartsWith("sl1_", StringComparison.Ordinal))
        {
            try
            {
                var protectedGrant = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token[4..]));
                var grantJson = persistence.UnprotectString(protectedGrant);
                var grant = grantJson is null ? null : JsonSerializer.Deserialize<PrivateGrant>(grantJson, jsonOptions);
                if (grant is null || grant.ExpiresUtc <= DateTimeOffset.UtcNow) return null;
                tsUrl = grant.TsUrl;
            }
            catch (Exception) { return null; }
        }
        else
        {
            var shared = await shareLinks.GetPlaybackAsync(token, cancellationToken);
            if (shared is null) return null;
            tsUrl = shared.TsUrl;
        }

        if (!Uri.TryCreate(tsUrl, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")) return null;
        var response = await GetUpstreamAsync(tsUrl, cancellationToken);
        return response is null ? null : new StreamProxyLiveResult(response);
    }

    private async Task<string?> ResolveStreamUrlAsync(string token, CancellationToken cancellationToken)
    {
        if (!token.StartsWith("sl1_", StringComparison.Ordinal))
        {
            var shared = await shareLinks.GetPlaybackAsync(token, cancellationToken);
            if (shared is not null) return shared.StreamUrl;
        }

        try
        {
            var encodedGrant = token.StartsWith("sl1_", StringComparison.Ordinal) ? token[4..] : token;
            var protectedGrant = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedGrant));
            var grantJson = persistence.UnprotectString(protectedGrant);
            var grant = grantJson is null ? null : JsonSerializer.Deserialize<PrivateGrant>(grantJson, jsonOptions);
            return grant is not null && grant.ExpiresUtc > DateTimeOffset.UtcNow ? grant.StreamUrl : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Stream playback token could not be resolved: {ErrorType}", exception.GetType().Name);
            return null;
        }
    }

    private async Task<HttpResponseMessage?> GetUpstreamAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("StreamProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            headerTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            var origin = new UriBuilder(new Uri(url)) { Path = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Authority);
            request.Headers.TryAddWithoutValidation("Referer", origin + "/");
            request.Headers.TryAddWithoutValidation("Origin", origin);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Upstream stream request returned HTTP {StatusCode}", (int)response.StatusCode);
                response.Dispose();
                return null;
            }
            return response;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    private string RewritePlaylist(string playlist, Uri baseUri, string token)
    {
        var lines = playlist.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                lines[i] = Regex.Replace(lines[i], "URI=\"([^\"]+)\"", match => $"URI=\"{ToResourceUrl(match.Groups[1].Value, baseUri, token)}\"");
                continue;
            }
            lines[i] = ToResourceUrl(line, baseUri, token);
        }
        return string.Join('\n', lines);
    }

    private string ToResourceUrl(string relativeOrAbsolute, Uri baseUri, string token)
    {
        var target = new Uri(baseUri, relativeOrAbsolute).ToString();
        var protectedTarget = persistence.ProtectString(JsonSerializer.Serialize(new ResourceGrant(target, token), jsonOptions));
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedTarget));
        return $"/stream/{token}/resource/{encoded}";
    }

    private sealed record PrivateGrant(string StreamUrl, DateTimeOffset ExpiresUtc, string? TsUrl = null);
    private sealed record ResourceGrant(string Url, string Token);

    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(bytes, cancellationToken)) != 0)
        {
            if (buffer.Length + count > maxBytes) return null;
            buffer.Write(bytes, 0, count);
        }
        return buffer.ToArray();
    }
}