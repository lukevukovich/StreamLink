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

    public string CreatePrivateToken(XtreamSession session, int streamId)
    {
        var links = streamUrls.BuildLinks(session, streamId);
        var grant = new PrivateGrant(links.HlsUrl, DateTimeOffset.UtcNow.AddHours(2));
        var protectedGrant = persistence.ProtectString(JsonSerializer.Serialize(grant, jsonOptions));
        return "sl1_" + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedGrant));
    }

    public async Task<StreamProxyResult?> GetManifestAsync(string token, CancellationToken cancellationToken = default)
    {
        var upstreamUrl = await ResolveStreamUrlAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(upstreamUrl)) return null;

        using var response = await GetUpstreamAsync(upstreamUrl, cancellationToken);
        if (response is null) return null;
        var playlist = await response.Content.ReadAsStringAsync(cancellationToken);
        var baseUri = new Uri(upstreamUrl);
        var rewritten = RewritePlaylist(playlist, baseUri, token);
        return new StreamProxyResult(Encoding.UTF8.GetBytes(rewritten), "application/vnd.apple.mpegurl");
    }


    public async Task<StreamProxyResult?> GetResourceAsync(string token, string resourceToken, string? queryString, CancellationToken cancellationToken = default)
    {
        var upstreamUrl = await ResolveStreamUrlAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(upstreamUrl)) return null;

        string targetUrl;
        try
        {
            var protectedTarget = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(resourceToken));
                targetUrl = persistence.UnprotectString(protectedTarget) ?? "";
            if (!string.IsNullOrWhiteSpace(queryString))
            {
                var separator = targetUrl.Contains('?') ? "&" : "?";
                targetUrl += separator + queryString.TrimStart('?', '&');
            }
            var target = new Uri(targetUrl);
            var source = new Uri(upstreamUrl);
            if (!target.IsAbsoluteUri || !target.Host.EndsWith(source.Host, StringComparison.OrdinalIgnoreCase)) return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Stream resource token validation failed: {ErrorType}", exception.GetType().Name);
            return null;
        }

        using var response = await GetUpstreamAsync(targetUrl, cancellationToken, sourceHost: new Uri(upstreamUrl).Host);
        if (response is null) return null;
        return new StreamProxyResult(await response.Content.ReadAsByteArrayAsync(cancellationToken), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
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

    private async Task<HttpResponseMessage?> GetUpstreamAsync(string url, CancellationToken cancellationToken, string? sourceHost = null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Xtream");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Referer", $"https://{sourceHost ?? new Uri(url).Host}/");
            request.Headers.TryAddWithoutValidation("Origin", $"https://{sourceHost ?? new Uri(url).Host}");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
        var protectedTarget = persistence.ProtectString(target);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedTarget));
        return $"/stream/{token}/resource/{encoded}";
    }

    private sealed record PrivateGrant(string StreamUrl, DateTimeOffset ExpiresUtc);
}