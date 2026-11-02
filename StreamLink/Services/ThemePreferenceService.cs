using Microsoft.EntityFrameworkCore;
using StreamLink.Data;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed record AppTheme(string Id, string Name, string MainColor, string AccentColor);

public static class AppThemes
{
    public const string Default = "green";

    public static IReadOnlyList<AppTheme> All { get; } =
    [
        new(Default, "Green", "#07181b", "#89c0b6"),
        new("light", "Light", "#fafafa", "#343434"),
        new("dark", "Dark", "#121212", "#e0e0e0"),
        new("graphite", "Graphite", "#202328", "#aebdcd"),
        new("night-light", "Night Light", "#f2efe9", "#687d6b"),
        new("terracotta", "Terracotta", "#f3e7d8", "#b85c45"),
        new("lemonade", "Lemonade", "#fff8cc", "#d94f91"),
        new("patriot", "Patriot", "#edf7ff", "#62b4eb"),
        new("blue", "Blue", "#091323", "#71aeff"),
        new("ocean", "Ocean", "#071b24", "#42dfc0"),
        new("purple", "Purple", "#160f23", "#be97ff"),
        new("orchid", "Orchid", "#24101f", "#ff69c7"),
        new("red", "Red", "#1f0d11", "#f08591"),
        new("rose-quartz", "Rose Quartz", "#f7e9ed", "#a74765"),
        new("ember", "Ember", "#19130f", "#f2b66d"),
        new("royal-gold", "Royal Gold", "#171528", "#d8bd73"),
        new("jack-o-lantern", "Jack-o'-Lantern", "#09090b", "#ff7417"),
        new("acid-lime", "Acid Lime", "#11140b", "#c9ef4a")
    ];

    public static bool IsSupported(string theme)
        => All.Any(option => string.Equals(option.Id, theme, StringComparison.Ordinal));
}

public sealed class ThemePreferenceService(IDbContextFactory<StreamLinkDbContext> dbFactory)
{
    public async Task<string> GetThemeAsync(string ownerKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var preference = await db.ThemePreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerKey == ownerKey, cancellationToken);
        return preference is not null && AppThemes.IsSupported(preference.Theme)
            ? preference.Theme
            : AppThemes.Default;
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
}
