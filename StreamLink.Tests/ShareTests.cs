using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using StreamLink.Services;
using StreamLink.Models;

namespace StreamLink.Tests;

public class ShareTests : IDisposable
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"streamlink-tests-{Guid.NewGuid():N}.db");
    private readonly IConfiguration shareConfiguration = TestSupport.ShareConfiguration();
    private int MaxActiveLinks => shareConfiguration.GetValue<int>("ShareLinks:MaxActivePerOwner");
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(database);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public async Task Create_RejectsInvalidLifetime(int hours)
    {
        var (service, _, _) = TestSupport.Shares(database);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync(TestSupport.Session(), "owner", Channel(1), "News", hours));
    }

    [Fact]
    public async Task Create_ProtectsSourceAndReusesLiveShareEvenUnderConcurrency()
    {
        var (service, factory, _) = TestSupport.Shares(database);
        var links = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.CreateAsync(TestSupport.Session(), "owner", Channel(1), "News", 4)));
        Assert.Single(links.Select(x => x.Token).Distinct());
        var link = links[0];
        Assert.Equal(36, link.Token.Length);
        Assert.InRange(link.ExpiresUtc - link.CreatedUtc, TimeSpan.FromHours(4), TimeSpan.FromHours(4));
        Assert.DoesNotContain("user name", link.ProtectedHlsUrl!);
        Assert.Contains("/live/user%20name/p%40ss%20word/1.m3u8", (await service.GetPlaybackAsync(link.Token))!.StreamUrl);
        Assert.Equal(link.Token, (await service.CreateAsync(TestSupport.Session(), "owner", Channel(1), "News", 24)).Token);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.ShareLinks.CountAsync());
    }

    [Fact]
    public async Task Shares_IsolateOwnersAndEnforceActiveLimit()
    {
        var (service, _, _) = TestSupport.Shares(database);
        var first = await service.CreateAsync(TestSupport.Session(), "a", Channel(0), "News", 1);
        for (var id = 1; id < MaxActiveLinks; id++) await service.CreateAsync(TestSupport.Session(), "a", Channel(id), "News", 1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(TestSupport.Session(), "a", Channel(MaxActiveLinks), "News", 1));
        Assert.Contains($"{MaxActiveLinks} active share links", error.Message);
        Assert.Equal(first.Token, (await service.CreateAsync(TestSupport.Session(), "a", Channel(0), "News", 1)).Token);
        Assert.Empty(await service.GetActiveAsync("b"));
        await service.RevokeAsync(first.Token, "b");
        Assert.NotNull(await service.GetPlaybackAsync(first.Token));
        await service.RevokeAsync(first.Token, "a");
        Assert.Null(await service.GetPlaybackAsync(first.Token));
        Assert.Equal(MaxActiveLinks - 1, (await service.GetActiveAsync("a")).Count);
        Assert.NotNull(await service.CreateAsync(TestSupport.Session(), "a", Channel(MaxActiveLinks), "News", 1));
        Assert.NotNull(await service.CreateAsync(TestSupport.Session(), "b", Channel(1), "News", 1));
    }

    [Fact]
    public async Task Create_UsesConfiguredLimitAndRejectsInvalidConfiguration()
    {
        var (_, factory, persistence) = TestSupport.Shares(database);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShareLinks:MaxActivePerOwner"] = "2"
        }).Build();
        var service = new ShareLinkService(factory, new StreamUrlService(), persistence, config);
        await service.CreateAsync(TestSupport.Session(), "owner", Channel(1), "News", 1);
        await service.CreateAsync(TestSupport.Session(), "owner", Channel(2), "News", 1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(TestSupport.Session(), "owner", Channel(3), "News", 1));
        Assert.Contains("2 active share links", error.Message);

        foreach (var invalid in new[] { "0", "101", "not-a-number" })
        {
            config["ShareLinks:MaxActivePerOwner"] = invalid;
            Assert.Throws<InvalidOperationException>(() => new ShareLinkService(factory, new StreamUrlService(), persistence, config));
        }
        config["ShareLinks:MaxActivePerOwner"] = null;
        Assert.Throws<InvalidOperationException>(() => new ShareLinkService(factory, new StreamUrlService(), persistence, config));
    }

    [Fact]
    public async Task Create_PrunesExpiredAndRevokedRowsWithoutRemovingLiveLinks()
    {
        var (service, factory, _) = TestSupport.Shares(database);
        var expired = await service.CreateAsync(TestSupport.Session(), "owner", Channel(1), "News", 1);
        var revoked = await service.CreateAsync(TestSupport.Session(), "owner", Channel(2), "News", 1);
        var live = await service.CreateAsync(TestSupport.Session(), "owner", Channel(3), "News", 1);
        await service.RevokeAsync(revoked.Token, "owner");
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.ShareLinks.SingleAsync(x => x.Token == expired.Token);
            row.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        await service.CreateAsync(TestSupport.Session(), "owner", Channel(4), "News", 1);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(2, await check.ShareLinks.CountAsync());
        Assert.NotNull(await service.GetPlaybackAsync(live.Token));
        Assert.Null(await service.GetAsync(expired.Token));
        Assert.Null(await service.GetAsync(revoked.Token));
    }

    [Fact]
    public async Task Create_EnforcesLimitAcrossServiceInstances()
    {
        var (service, factory, persistence) = TestSupport.Shares(database);
        var other = new ShareLinkService(factory, new StreamUrlService(), persistence, shareConfiguration);
        var attempts = Enumerable.Range(0, MaxActiveLinks + 6).Select(async id =>
        {
            try { await (id % 2 == 0 ? service : other).CreateAsync(TestSupport.Session(), "owner", Channel(id), "News", 1); }
            catch (InvalidOperationException) { }
        });
        await Task.WhenAll(attempts);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(MaxActiveLinks, await db.ShareLinks.CountAsync());
    }

    [Fact]
    public async Task Playback_RejectsExpiredInvalidAndUndecryptableSources()
    {
        var (service, factory, persistence) = TestSupport.Shares(database);
        var link = await service.CreateAsync(TestSupport.Session("ts"), "owner", Channel(1), "News", 1);
        Assert.EndsWith("/1.ts", (await service.GetPlaybackAsync(link.Token))!.StreamUrl);
        Assert.Null(await service.GetPlaybackAsync("missing"));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.ShareLinks.SingleAsync();
            saved.ProtectedHlsUrl = persistence.ProtectString("https://legacy.example/live/u/p/1.ts");
            await db.SaveChangesAsync();
        }
        Assert.Equal("https://legacy.example/live/u/p/1.ts", (await service.GetPlaybackAsync(link.Token))!.StreamUrl);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.ShareLinks.SingleAsync();
            saved.ProtectedHlsUrl = persistence.ProtectString("file:///private/source");
            await db.SaveChangesAsync();
        }
        Assert.Null(await service.GetPlaybackAsync(link.Token));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.ShareLinks.SingleAsync();
            saved.ProtectedHlsUrl = "invalid-protected-data";
            await db.SaveChangesAsync();
        }
        Assert.Null(await service.GetPlaybackAsync(link.Token));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var saved = await db.ShareLinks.SingleAsync();
            saved.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.Null(await service.GetPlaybackAsync(link.Token));
    }

    private static XtreamChannel Channel(int id) => new() { StreamId = id, Name = $"Channel {id}" };
}