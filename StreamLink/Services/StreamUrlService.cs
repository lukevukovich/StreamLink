using StreamLink.Models;

namespace StreamLink.Services;

public sealed class StreamUrlService : IStreamUrlService
{
    public StreamLinks BuildLinks(XtreamSession session, int streamId)
    {
        var baseUrl = session.ServerUrl;
        var info = session.ServerInfo;
        if (!string.IsNullOrWhiteSpace(info.Url) &&
            info.Protocol is not null &&
            (info.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) || info.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate($"{info.Protocol}://{info.Url}", UriKind.Absolute, out var provider) &&
            provider.Scheme is "http" or "https" && string.IsNullOrEmpty(provider.UserInfo) &&
            provider.AbsolutePath == "/" && string.IsNullOrEmpty(provider.Query) && string.IsNullOrEmpty(provider.Fragment))
        {
            var port = provider.Scheme == "https" ? info.HttpsPort : info.Port;
            var builder = new UriBuilder(provider);
            if (int.TryParse(port, out var parsed) && parsed is > 0 and <= 65535) builder.Port = parsed;
            baseUrl = builder.Uri.GetLeftPart(UriPartial.Authority);
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || origin.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            throw new InvalidOperationException("The provider returned an invalid stream server URL.");
        baseUrl = baseUrl.TrimEnd('/');
        var hls = $"{baseUrl}/live/{Uri.EscapeDataString(session.Username)}/{Uri.EscapeDataString(session.Password)}/{streamId}.m3u8";
        var ts = $"{baseUrl}/live/{Uri.EscapeDataString(session.Username)}/{Uri.EscapeDataString(session.Password)}/{streamId}.ts";
        var formats = session.UserInfo.AllowedOutputFormats.Select(x => x.ToLowerInvariant()).ToHashSet();
        return new StreamLinks(hls, ts, formats.Count == 0 || formats.Contains("m3u8"), formats.Count == 0 || formats.Contains("ts"));
    }
}
