using StreamLink.Models;

namespace StreamLink.Services;

public sealed class StreamUrlService : IStreamUrlService
{
    public StreamLinks BuildLinks(XtreamSession session, int streamId)
    {
        var baseUrl = session.ServerUrl;
        var info = session.ServerInfo;
        if (!string.IsNullOrWhiteSpace(info.Url) && !string.IsNullOrWhiteSpace(info.Protocol))
        {
            var port = info.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) ? info.HttpsPort : info.Port;
            baseUrl = $"{info.Protocol}://{info.Url}" + (int.TryParse(port, out var parsed) && !((info.Protocol == "https" && parsed == 443) || (info.Protocol != "https" && parsed == 80)) ? $":{parsed}" : "");
        }
        var hls = $"{baseUrl}/live/{Uri.EscapeDataString(session.Username)}/{Uri.EscapeDataString(session.Password)}/{streamId}.m3u8";
        var ts = $"{baseUrl}/live/{Uri.EscapeDataString(session.Username)}/{Uri.EscapeDataString(session.Password)}/{streamId}.ts";
        var formats = session.UserInfo.AllowedOutputFormats.Select(x => x.ToLowerInvariant()).ToHashSet();
        return new StreamLinks(hls, ts, formats.Count == 0 || formats.Contains("m3u8"), formats.Count == 0 || formats.Contains("ts"));
    }
}
