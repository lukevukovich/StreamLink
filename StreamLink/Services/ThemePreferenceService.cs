using Microsoft.EntityFrameworkCore;
using StreamLink.Data;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed record AppTheme(string Id, string Name, string MainColor, string AccentColor);

public sealed record ShareSortOption(string Id, string Name);

public static class ShareSortOptions
{
    public const string Expiring = "expiring";
    public const string Latest = "latest";
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string Channel = "channel";
    public const string ChannelDescending = "channel-desc";
    public const string Category = "category";
    public const string CategoryDescending = "category-desc";
    public const string Default = Newest;

    public static IReadOnlyList<ShareSortOption> All { get; } =
    [
        new(Expiring, "Soonest to expire"),
        new(Latest, "Latest to expire"),
        new(Newest, "Recently created"),
        new(Oldest, "Oldest created"),
        new(Channel, "Channel A-Z"),
        new(ChannelDescending, "Channel Z-A"),
        new(Category, "Category A-Z"),
        new(CategoryDescending, "Category Z-A")
    ];

    public static bool IsSupported(string value) =>
        All.Any(option => string.Equals(option.Id, value, StringComparison.Ordinal));

    public static IEnumerable<ShareLink> Sort(IEnumerable<ShareLink> links, string sort) => sort switch
    {
        Newest => links.OrderByDescending(link => link.CreatedUtc),
        Oldest => links.OrderBy(link => link.CreatedUtc),
        Latest => links.OrderByDescending(link => link.ExpiresUtc).ThenByDescending(link => link.CreatedUtc),
        Channel => links.OrderBy(link => link.ChannelName, StringComparer.OrdinalIgnoreCase).ThenByDescending(link => link.CreatedUtc),
        ChannelDescending => links.OrderByDescending(link => link.ChannelName, StringComparer.OrdinalIgnoreCase).ThenByDescending(link => link.CreatedUtc),
        Category => links.OrderBy(link => link.CategoryName, StringComparer.OrdinalIgnoreCase).ThenBy(link => link.ChannelName, StringComparer.OrdinalIgnoreCase).ThenByDescending(link => link.CreatedUtc),
        CategoryDescending => links.OrderByDescending(link => link.CategoryName, StringComparer.OrdinalIgnoreCase).ThenBy(link => link.ChannelName, StringComparer.OrdinalIgnoreCase).ThenByDescending(link => link.CreatedUtc),
        Expiring => links.OrderBy(link => link.ExpiresUtc),
        _ => throw new ArgumentException("Unsupported share sort order.", nameof(sort))
    };
}

public static class AppThemes
{
    public const string Default = "green";

    public static IReadOnlyList<AppTheme> All { get; } =
    [
        new(Default, "Green", "#07181b", "#89c0b6"),
        new("light", "Light", "#fafafa", "#343434"),
        new("dark", "Dark", "#090909", "#e0e0e0"),
        new("graphite", "Graphite", "#202328", "#aebdcd"),
        new("night-light", "Night Light", "#f2efe9", "#687d6b"),
        new("terracotta", "Terracotta", "#f3e7d8", "#b85c45"),
        new("lemonade", "Lemonade", "#fff8cc", "#d94f91"),
        new("patriot", "Patriot", "#edf7ff", "#62b4eb"),
        new("blue", "Blue", "#091323", "#4b91ff"),
        new("ocean", "Ocean", "#061f2b", "#00e6d0"),
        new("purple", "Purple", "#160f23", "#a66bff"),
        new("orchid", "Orchid", "#24101f", "#ff69c7"),
        new("red", "Red", "#1f0d11", "#f54b5e"),
        new("rose-quartz", "Rose Quartz", "#f7e9ed", "#a74765"),
        new("ember", "Ember", "#19130f", "#f2b66d"),
        new("royal-gold", "Royal Gold", "#171528", "#d8bd73"),
        new("halloween", "Halloween", "#09090b", "#ff7417"),
        new("acid-lime", "Acid Lime", "#11140b", "#c9ef4a")
    ];

    public static bool IsSupported(string theme)
        => All.Any(option => string.Equals(option.Id, theme, StringComparison.Ordinal));
}

public sealed class ThemePreferenceService(IDbContextFactory<StreamLinkDbContext> dbFactory)
{
    public async Task<string> GetThemeAsync(string ownerKey, CancellationToken cancellationToken = default) =>
        await GetSavedThemeAsync(ownerKey, cancellationToken) ?? AppThemes.Default;

    public async Task<string?> GetSavedThemeAsync(string ownerKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var preference = await db.ThemePreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerKey == ownerKey, cancellationToken);
        return preference is not null && AppThemes.IsSupported(preference.Theme)
            ? preference.Theme
            : null;
    }

    public async Task SaveThemeAsync(string ownerKey, string theme, CancellationToken cancellationToken = default)
    {
        if (!AppThemes.IsSupported(theme))
            throw new ArgumentException("The selected theme is not supported.", nameof(theme));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var preference = await db.ThemePreferences
            .SingleOrDefaultAsync(x => x.OwnerKey == ownerKey, cancellationToken);
        if (preference is null)
            db.ThemePreferences.Add(new ThemePreference { OwnerKey = ownerKey, Theme = theme });
        else
            preference.Theme = theme;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetShareSortAsync(string ownerKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var preference = await db.ThemePreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerKey == ownerKey, cancellationToken);
        return preference is not null && ShareSortOptions.IsSupported(preference.ShareSort)
            ? preference.ShareSort
            : ShareSortOptions.Default;
    }

    public async Task SaveShareSortAsync(string ownerKey, string sort, CancellationToken cancellationToken = default)
    {
        if (!ShareSortOptions.IsSupported(sort))
            throw new ArgumentException("The selected share sort order is not supported.", nameof(sort));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var preference = await db.ThemePreferences
            .SingleOrDefaultAsync(x => x.OwnerKey == ownerKey, cancellationToken);
        if (preference is null)
            db.ThemePreferences.Add(new ThemePreference { OwnerKey = ownerKey, ShareSort = sort });
        else
            preference.ShareSort = sort;

        await db.SaveChangesAsync(cancellationToken);
    }
}
