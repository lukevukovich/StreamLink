using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StreamLink.Data;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed class ShareLinkService(IDbContextFactory<StreamLinkDbContext> dbFactory, IStreamUrlService streamUrls, SessionPersistence persistence) : IShareLinkService
{
    public async Task<ShareLink> CreateAsync(XtreamSession session, string ownerKey, XtreamChannel channel, string categoryName, ShareExpiration expiration, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var streamLinks = streamUrls.BuildLinks(session, channel.StreamId);
        if (!streamLinks.HlsAllowed && !streamLinks.TsAllowed) throw new InvalidOperationException("Playback is not allowed for this account.");
        var sources = new ShareSources(streamLinks.HlsAllowed ? streamLinks.HlsUrl : null, streamLinks.TsAllowed ? streamLinks.TsUrl : null);
        var link = new ShareLink { Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant(), ChannelName = channel.Name, CategoryName = categoryName, StreamId = channel.StreamId, CreatedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddHours((int)expiration), OwnerKey = ownerKey, ProtectedHlsUrl = persistence.ProtectString(JsonSerializer.Serialize(sources)) };
        await db.ShareLinks.AddAsync(link, cancellationToken); await db.SaveChangesAsync(cancellationToken); return link;
    }

    public async Task<IReadOnlyList<ShareLink>> GetActiveAsync(string ownerKey, CancellationToken cancellationToken = default)
    { await using var db = await dbFactory.CreateDbContextAsync(cancellationToken); return await db.ShareLinks.Where(x => x.OwnerKey == ownerKey && !x.Revoked && x.ExpiresUtc > DateTime.UtcNow).OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken); }
    public async Task<ShareLink?> GetAsync(string token, CancellationToken cancellationToken = default)
    { await using var db = await dbFactory.CreateDbContextAsync(cancellationToken); return await db.ShareLinks.SingleOrDefaultAsync(x => x.Token == token, cancellationToken); }
    public async Task<SharePlayback?> GetPlaybackAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var link = await db.ShareLinks.SingleOrDefaultAsync(x => x.Token == token, cancellationToken);
        if (link is null || link.Revoked || link.ExpiresUtc <= DateTime.UtcNow) return null;
        var saved = persistence.UnprotectString(link.ProtectedHlsUrl);
        if (string.IsNullOrWhiteSpace(saved)) return null;
        // Older shares protected a single HLS URL; new shares keep both allowed
        // formats in the same encrypted database field (no SQLite migration).
        if (!saved.StartsWith('{'))
        {
            var legacy = new Uri(saved);
            var ts = legacy.AbsolutePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ? saved : null;
            // Pre-upgrade shares have only an HLS URL; the previous player derived
            // the corresponding Xtream TS URL. Keep those active shares working.
            if (ts is null && legacy.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                ts = new UriBuilder(legacy) { Path = legacy.AbsolutePath[..^6] + ".ts" }.Uri.ToString();
            return new SharePlayback(link, saved, ts);
        }
        var sources = JsonSerializer.Deserialize<ShareSources>(saved);
        if (sources is null) return null;
        var primary = sources.HlsUrl ?? sources.TsUrl;
        return string.IsNullOrWhiteSpace(primary) ? null : new SharePlayback(link, primary, sources.TsUrl);
    }
    public async Task RevokeAsync(string token, string ownerKey, CancellationToken cancellationToken = default)
    { await using var db = await dbFactory.CreateDbContextAsync(cancellationToken); var link = await db.ShareLinks.SingleOrDefaultAsync(x => x.Token == token && x.OwnerKey == ownerKey, cancellationToken); if (link is not null) { link.Revoked = true; await db.SaveChangesAsync(cancellationToken); } }

    private sealed record ShareSources(string? HlsUrl, string? TsUrl);
}

