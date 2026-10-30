using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Tests;

public class ExpiredShareLinkCleanupTests : IDisposable
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"streamlink-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(database);
    }

    [Fact]
    public async Task CleanupExpiredLinks_DeletesExpiredRowsAndKeepsActiveRows()
    {
        var (_, factory, _) = TestSupport.Shares(database);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ShareLinks.AddRange(
                new ShareLink { Token = "expired", ExpiresUtc = DateTime.UtcNow.AddMinutes(-1) },
                new ShareLink { Token = "active", ExpiresUtc = DateTime.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }

        var cleanup = new ExpiredShareLinkCleanupService(factory, NullLogger<ExpiredShareLinkCleanupService>.Instance);
        Assert.Equal(1, await cleanup.CleanupExpiredLinksAsync());

        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal("active", (await check.ShareLinks.SingleAsync()).Token);
    }
}
