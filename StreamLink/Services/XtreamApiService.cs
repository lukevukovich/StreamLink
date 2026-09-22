using System.Net.Http.Json;
using System.Text.Json;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed class XtreamApiService(IHttpClientFactory httpClientFactory) : IXtreamApiService
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<XtreamSession?> LoginAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        serverUrl = serverUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")) return null;
        var response = await GetAsync<XtreamLoginResponse>(serverUrl, username, password, null, cancellationToken);
        if (response?.UserInfo is null || response.UserInfo.Auth != 1) return null;
        return new XtreamSession { ServerUrl = serverUrl, Username = username, Password = password, UserInfo = response.UserInfo, ServerInfo = response.ServerInfo ?? new() };
    }

    public async Task<XtreamSession?> RefreshAccountAsync(XtreamSession session, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<XtreamLoginResponse>(session.ServerUrl, session.Username, session.Password, null, cancellationToken);
        if (response?.UserInfo is null || response.UserInfo.Auth != 1) return null;
        return new XtreamSession { ServerUrl = session.ServerUrl, Username = session.Username, Password = session.Password, UserInfo = response.UserInfo, ServerInfo = response.ServerInfo ?? session.ServerInfo };
    }

    public async Task<IReadOnlyList<XtreamCategory>> GetCategoriesAsync(XtreamSession session, CancellationToken cancellationToken = default)
    {
        var json = await GetRawAsync(session.ServerUrl, session.Username, session.Password, ("get_live_categories", null), cancellationToken);
        return DeserializeList<XtreamCategory>(json);
    }

    public async Task<IReadOnlyList<XtreamChannel>> GetChannelsAsync(XtreamSession session, string categoryId, CancellationToken cancellationToken = default)
        => await GetListAsync<XtreamChannel>(session, "get_live_streams", categoryId, cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(XtreamSession session, string action, string? categoryId, CancellationToken cancellationToken)
    {
        var response = await GetRawAsync(session.ServerUrl, session.Username, session.Password, (action, categoryId), cancellationToken);
        if (string.IsNullOrWhiteSpace(response)) return [];

        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                return DeserializeRows<T>(document.RootElement.GetRawText());

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var rows = new List<T>();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array) continue;
                    rows.AddRange(DeserializeRows<T>(property.Value.GetRawText()));
                }
                return rows;
            }
        }
        catch (JsonException) { }

        return [];
    }

    private IReadOnlyList<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? DeserializeRows<T>(document.RootElement.GetRawText())
                : [];
        }
        catch (JsonException) { return []; }
    }

    private List<T> DeserializeRows<T>(string json)
    {
        try { return JsonSerializer.Deserialize<List<T>>(json, jsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private async Task<T?> GetAsync<T>(string serverUrl, string username, string password, (string Action, string? CategoryId)? query, CancellationToken cancellationToken)
    {
        var json = await GetRawAsync(serverUrl, username, password, query, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, jsonOptions); }
        catch (JsonException) { return default; }
    }

    private async Task<string?> GetRawAsync(string serverUrl, string username, string password, (string Action, string? CategoryId)? query, CancellationToken cancellationToken)
    {
        var builder = new UriBuilder($"{serverUrl}/player_api.php");
        var parameters = new List<string> { $"username={Uri.EscapeDataString(username)}", $"password={Uri.EscapeDataString(password)}" };
        if (query is { } q) { parameters.Add($"action={Uri.EscapeDataString(q.Action)}"); if (!string.IsNullOrWhiteSpace(q.CategoryId)) parameters.Add($"category_id={Uri.EscapeDataString(q.CategoryId)}"); }
        builder.Query = string.Join('&', parameters);
        try
        {
            using var response = await httpClientFactory.CreateClient("Xtream").GetAsync(builder.Uri, cancellationToken);
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException) { return default; }
        catch (UriFormatException) { return default; }
        catch (InvalidOperationException) { return default; }
        catch (TaskCanceledException) { return default; }
    }

    private sealed class XtreamLoginResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("user_info")] public XtreamUserInfo? UserInfo { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("server_info")] public XtreamServerInfo? ServerInfo { get; set; }
    }
}
