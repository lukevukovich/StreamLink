using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StreamLink.Data;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed class ShareLinkService(IDbContextFactory<StreamLinkDbContext> dbFactory, IStreamUrlService streamUrls, SessionPersistence persistence) : IShareLinkService
{
    private const int MaxActiveLinksPerOwner = 20;

    // Serialize creation within this process; the SQLite write transaction also
    // serializes the count and insert across multiple app instances.
    private static readonly SemaphoreSlim CreationGate = new(1, 1);

    public async Task<ShareLink> CreateAsync(XtreamSession session, string ownerKey, XtreamChannel channel, string categoryName, int expirationHours, CancellationToken cancellationToken = default)
    {
        if (expirationHours is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(expirationHours), "Choose a lifetime from 1 to 24 hours.");
        await CreationGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTime.UtcNow;
            await db.ShareLinks.Where(x => x.Revoked || x.ExpiresUtc <= now).ExecuteDeleteAsync(cancellationToken);
            var existing = await db.ShareLinks.Where(x => x.OwnerKey == ownerKey && x.StreamId == channel.StreamId)
                .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var activeCount = await db.ShareLinks.CountAsync(x => x.OwnerKey == ownerKey, cancellationToken);
            if (activeCount >= MaxActiveLinksPerOwner)
            {
                await transaction.CommitAsync(cancellationToken);
                throw new InvalidOperationException($"You have reached the limit of {MaxActiveLinksPerOwner} active share links. Revoke an existing link to create another.");
            }

            var streamLinks = streamUrls.BuildLinks(session, channel.StreamId);
            if (!streamLinks.HlsAllowed && !streamLinks.TsAllowed) throw new InvalidOperationException("No supported stream format is allowed for this account.");
            var sources = new ShareSources(streamLinks.HlsAllowed ? streamLinks.HlsUrl : null, streamLinks.TsAllowed ? streamLinks.TsUrl : null);
            var link = new ShareLink { Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant(), ChannelName = channel.Name, CategoryName = categoryName, StreamId = channel.StreamId, CreatedUtc = now, ExpiresUtc = now.AddHours(expirationHours), OwnerKey = ownerKey, ProtectedHlsUrl = persistence.ProtectString(JsonSerializer.Serialize(sources)) };
            await db.ShareLinks.AddAsync(link, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return link;
        }
        finally { CreationGate.Release(); }
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
        // Older shares protected a single URL; newer records keep allowed formats
        // in the same encrypted database field (no SQLite migration).
        if (!saved.StartsWith('{'))
        {
            return IsProviderUrl(saved) ? new SharePlayback(link, saved) : null;
        }
        var sources = JsonSerializer.Deserialize<ShareSources>(saved);
        if (sources is null) return null;
        var primary = sources.HlsUrl ?? sources.TsUrl;
        return IsProviderUrl(primary) ? new SharePlayback(link, primary!) : null;
    }
    public async Task RevokeAsync(string token, string ownerKey, CancellationToken cancellationToken = default)
    { await using var db = await dbFactory.CreateDbContextAsync(cancellationToken); var link = await db.ShareLinks.SingleOrDefaultAsync(x => x.Token == token && x.OwnerKey == ownerKey, cancellationToken); if (link is not null) { link.Revoked = true; await db.SaveChangesAsync(cancellationToken); } }

    private sealed record ShareSources(string? HlsUrl, string? TsUrl);

    private static bool IsProviderUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(uri.UserInfo);
}

