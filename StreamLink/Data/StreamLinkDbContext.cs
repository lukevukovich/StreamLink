using Microsoft.EntityFrameworkCore;
using StreamLink.Models;

namespace StreamLink.Data;

public sealed class StreamLinkDbContext(DbContextOptions<StreamLinkDbContext> options) : DbContext(options)
{
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShareLink>().HasIndex(x => x.Token).IsUnique();
        modelBuilder.Entity<ShareLink>().Property(x => x.Token).HasMaxLength(64);
        modelBuilder.Entity<ShareLink>().Property(x => x.OwnerKey).HasMaxLength(128);
    }
}
