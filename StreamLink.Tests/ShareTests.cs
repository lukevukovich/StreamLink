using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StreamLink.Models;

namespace StreamLink.Tests;

public class ShareTests : IDisposable
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"streamlink-tests-{Guid.NewGuid():N}.db");
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
        for (var id = 1; id < 25; id++) await service.CreateAsync(TestSupport.Session(), "a", Channel(id), "News", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(TestSupport.Session(), "a", Channel(25), "News", 1));
        Assert.Empty(await service.GetActiveAsync("b"));
        await service.RevokeAsync(first.Token, "b");
        Assert.NotNull(await service.GetPlaybackAsync(first.Token));
        await service.RevokeAsync(first.Token, "a");
        Assert.Null(await service.GetPlaybackAsync(first.Token));
        Assert.Equal(24, (await service.GetActiveAsync("a")).Count);
        Assert.NotNull(await service.CreateAsync(TestSupport.Session(), "a", Channel(25), "News", 1));
        Assert.NotNull(await service.CreateAsync(TestSupport.Session(), "b", Channel(1), "News", 1));
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