using Microsoft.EntityFrameworkCore;
using StreamLink.Models;

namespace StreamLink.Data;

public sealed class StreamLinkDbContext(DbContextOptions<StreamLinkDbContext> options) : DbContext(options)
{
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<ThemePreference> ThemePreferences => Set<ThemePreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShareLink>().HasIndex(x => x.Token).IsUnique();
        modelBuilder.Entity<ShareLink>().Property(x => x.Token).HasMaxLength(64);
        modelBuilder.Entity<ShareLink>().Property(x => x.OwnerKey).HasMaxLength(128);
        modelBuilder.Entity<ThemePreference>().HasKey(x => x.OwnerKey);
        modelBuilder.Entity<ThemePreference>().Property(x => x.OwnerKey).HasMaxLength(128);
        modelBuilder.Entity<ThemePreference>().Property(x => x.Theme).HasMaxLength(16).IsRequired();
    }
}
