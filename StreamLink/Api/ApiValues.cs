using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Api;

internal static class ApiValues
{
    public static XtreamSession Session(HttpContext context) => context.Features.Get<ApiSessionFeature>()!.Session;
    public static string Owner(HttpContext context) => SessionState.GetOwnerKey(Session(context));

    public static AccountResponse Account(XtreamSession session) => new(session.Username, session.UserInfo.Status,
        session.UserInfo.Expiration, session.UserInfo.IsTrial == "1", session.UserInfo.ActiveConnections,
        session.UserInfo.MaxConnections, session.UserInfo.AllowedOutputFormats);

    public static ChannelResponse Channel(XtreamChannel channel) =>
        new(channel.StreamId, channel.Name, channel.CategoryId, channel.LogoUrl, channel.EpgChannelId);

    public static ShareResponse Share(ShareLink link) => new(link.Token, $"/watch/{link.Token}", link.StreamId,
        link.ChannelName, link.CategoryName, new DateTimeOffset(DateTime.SpecifyKind(link.CreatedUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(link.ExpiresUtc, DateTimeKind.Utc)));
}
