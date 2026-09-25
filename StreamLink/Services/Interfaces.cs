using StreamLink.Models;

namespace StreamLink.Services;

public interface IXtreamApiService
{
    Task<XtreamSession?> LoginAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default);
    Task<XtreamSession?> RefreshAccountAsync(XtreamSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<XtreamCategory>> GetCategoriesAsync(XtreamSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<XtreamChannel>> GetChannelsAsync(XtreamSession session, string categoryId, CancellationToken cancellationToken = default);
}

public interface IStreamUrlService
{
    StreamLinks BuildLinks(XtreamSession session, int streamId);
}

public interface IShareLinkService
{
    Task<ShareLink> CreateAsync(XtreamSession session, string ownerKey, XtreamChannel channel, string categoryName, ShareExpiration expiration, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShareLink>> GetActiveAsync(string ownerKey, CancellationToken cancellationToken = default);
    Task<ShareLink?> GetAsync(string token, CancellationToken cancellationToken = default);
    Task<SharePlayback?> GetPlaybackAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeAsync(string token, string ownerKey, CancellationToken cancellationToken = default);
}

public interface IStreamProxyService
{
    string CreatePrivateToken(XtreamSession session, int streamId);
    Task<StreamProxyResult?> GetManifestAsync(string token, CancellationToken cancellationToken = default);
    Task<StreamProxyResourceResult?> GetResourceAsync(string token, string resourceToken, CancellationToken cancellationToken = default);
    Task<StreamProxyLiveResult?> GetLiveTsAsync(string token, CancellationToken cancellationToken = default);
}

public sealed record StreamProxyResult(byte[] Content, string ContentType);
public sealed record StreamProxyResourceResult(byte[]? Content, string ContentType, HttpResponseMessage? Upstream) : IDisposable
{
    public void Dispose() => Upstream?.Dispose();
}
public sealed record StreamProxyLiveResult(HttpResponseMessage Response) : IAsyncDisposable
{
    public ValueTask DisposeAsync() { Response.Dispose(); return ValueTask.CompletedTask; }
}
