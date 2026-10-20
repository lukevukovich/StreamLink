using Microsoft.EntityFrameworkCore;
using StreamLink.Data;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed record AppTheme(string Id, string Name);

public static class AppThemes
{
    public const string Default = "green";

    public static IReadOnlyList<AppTheme> All { get; } =
    [
        new(Default, "Green"),
        new("blue", "Blue"),
        new("red", "Red"),
        new("purple", "Purple"),
        new("dark", "Dark"),
        new("light", "Light")
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
