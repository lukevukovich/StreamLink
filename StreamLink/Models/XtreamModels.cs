using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamLink.Models;

public sealed class XtreamSession
{
    public required string ServerUrl { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required XtreamUserInfo UserInfo { get; init; }
    public XtreamServerInfo ServerInfo { get; init; } = new();
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class XtreamUserInfo
{
    [JsonPropertyName("auth")] public int Auth { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("exp_date")] public string? ExpirationDate { get; set; }
    [JsonPropertyName("is_trial")] public string? IsTrial { get; set; }
    [JsonPropertyName("active_cons")] public int ActiveConnections { get; set; }
    [JsonPropertyName("max_connections")] public int MaxConnections { get; set; }
    [JsonPropertyName("allowed_output_formats")] public List<string> AllowedOutputFormats { get; set; } = [];

    [JsonIgnore]
    public DateTimeOffset? Expiration
        => long.TryParse(ExpirationDate, out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null;
}

public sealed class XtreamServerInfo
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("server_protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("port")] public string? Port { get; set; }
    [JsonPropertyName("https_port")] public string? HttpsPort { get; set; }
}

public sealed class XtreamCategory
{
    [JsonPropertyName("category_id")] public string Id { get; set; } = "";
    [JsonPropertyName("category_name")] public string Name { get; set; } = "";
    [JsonPropertyName("parent_id")] public JsonElement ParentId { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class XtreamChannel
{
    [JsonPropertyName("stream_id")] public int StreamId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("stream_icon")] public string? LogoUrl { get; set; }
    [JsonPropertyName("epg_channel_id")] public string? EpgChannelId { get; set; }
}

public sealed record StreamLinks(string HlsUrl, string TsUrl, bool HlsAllowed, bool TsAllowed);
