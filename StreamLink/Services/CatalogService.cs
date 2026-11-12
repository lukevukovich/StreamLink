using StreamLink.Models;

namespace StreamLink.Services;

public sealed record CatalogChannel(XtreamChannel Channel, string CategoryName);

public sealed class CatalogService(IXtreamApiService xtream, ILogger<CatalogService> logger) : ICatalogService
{
    public async Task<CatalogChannel?> FindChannelAsync(XtreamSession session, int streamId, string? categoryId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            var categoriesInAccount = await xtream.GetCategoriesAsync(session, cancellationToken);
            var category = categoriesInAccount.FirstOrDefault(item => item.Id == categoryId);
            if (category is null) return null;
            var matching = (await xtream.GetChannelsAsync(session, categoryId, cancellationToken))
                .FirstOrDefault(item => item.StreamId == streamId &&
                    (string.IsNullOrWhiteSpace(item.CategoryId) || item.CategoryId == categoryId));
            return matching is null ? null : new(matching, category.Name);
        }
        XtreamChannel? channel;
        ProviderResponseException? initialFailure = null;
        try
        {
            channel = (await xtream.GetChannelsAsync(session, "", cancellationToken))
                .FirstOrDefault(item => item.StreamId == streamId);
        }
        catch (ProviderResponseException exception)
        {
            logger.LogInformation("The provider did not return all channels; trying each category.");
            initialFailure = exception;
            channel = null;
        }
        IReadOnlyList<XtreamCategory>? categories = null;
        if (channel is null || !string.IsNullOrWhiteSpace(channel.CategoryId))
            categories = await xtream.GetCategoriesAsync(session, cancellationToken);
        if (channel is null && categories is not null)
        {
            if (categories.Count == 0 && initialFailure is not null) throw initialFailure;
            foreach (var category in categories)
            {
                channel = (await xtream.GetChannelsAsync(session, category.Id, cancellationToken))
                    .FirstOrDefault(item => item.StreamId == streamId);
                if (channel is not null)
                    return new(channel, categories.FirstOrDefault(item => item.Id == channel.CategoryId)?.Name ?? category.Name);
            }
        }
        return channel is null ? null : new(channel,
            categories?.FirstOrDefault(item => item.Id == channel.CategoryId)?.Name ?? "Live TV");
    }
}

public static class CatalogSort
{
    public static bool IsSupported(string sort) => sort is "provider" or "name" or "name-desc";

    public static IEnumerable<T> Apply<T>(IEnumerable<T> items, Func<T, string> name, string? search, string sort)
    {
        if (!string.IsNullOrWhiteSpace(search))
            items = items.Where(item => name(item).Contains(search, StringComparison.OrdinalIgnoreCase));
        return sort switch
        {
            "provider" => items,
            "name" => items.OrderBy(name, StringComparer.OrdinalIgnoreCase),
            "name-desc" => items.OrderByDescending(name, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentException("Unsupported catalog sort order.", nameof(sort))
        };
    }
}
