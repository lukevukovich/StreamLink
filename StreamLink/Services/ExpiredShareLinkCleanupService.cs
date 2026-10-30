using Microsoft.EntityFrameworkCore;
using StreamLink.Data;

namespace StreamLink.Services;

public sealed class ExpiredShareLinkCleanupService(
    IDbContextFactory<StreamLinkDbContext> dbFactory,
    ILogger<ExpiredShareLinkCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deletedCount = await CleanupExpiredLinksAsync(stoppingToken);
                logger.LogInformation("Deleted {Count} expired share links.", deletedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to clean up expired share links.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    public async Task<int> CleanupExpiredLinksAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ShareLinks
            .Where(link => link.ExpiresUtc <= DateTime.UtcNow)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
