using System.Security.Cryptography;
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
        if (!streamLinks.HlsAllowed) throw new InvalidOperationException("HLS playback is not allowed for this account.");
        var link = new ShareLink { Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant(), ChannelName = channel.Name, CategoryName = categoryName, StreamId = channel.StreamId, CreatedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddHours((int)expiration), OwnerKey = ownerKey, ProtectedHlsUrl = persistence.ProtectString(streamLinks.HlsUrl) };
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
        var streamUrl = persistence.UnprotectString(link.ProtectedHlsUrl);
        return string.IsNullOrWhiteSpace(streamUrl) ? null : new SharePlayback(link, streamUrl);
    }
    public async Task RevokeAsync(string token, string ownerKey, CancellationToken cancellationToken = default)
    { await using var db = await dbFactory.CreateDbContextAsync(cancellationToken); var link = await db.ShareLinks.SingleOrDefaultAsync(x => x.Token == token && x.OwnerKey == ownerKey, cancellationToken); if (link is not null) { link.Revoked = true; await db.SaveChangesAsync(cancellationToken); } }
}

