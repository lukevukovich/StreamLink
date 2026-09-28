using System.Net;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Tests;

public class StreamAndSessionTests
{
    [Fact]
    public void StreamUrls_EscapeCredentialsAndHonorFormats()
    {
        var session = TestSupport.Session("TS");
        var links = new StreamUrlService().BuildLinks(session, 42);
        Assert.Equal("https://provider.example:8443/live/user%20name/p%40ss%20word/42.ts", links.TsUrl);
        Assert.False(links.HlsAllowed);
        Assert.True(links.TsAllowed);
        Assert.True(new StreamUrlService().BuildLinks(TestSupport.Session(""), 42).HlsAllowed);
    }

    [Fact]
    public void StreamUrls_UseValidProviderOverrideAndPort()
    {
        var session = TestSupport.Session();
        session = new XtreamSession { ServerUrl = session.ServerUrl, Username = session.Username, Password = session.Password,
            UserInfo = session.UserInfo, ServerInfo = new() { Url = "cdn.example", Protocol = "https", HttpsPort = "9443" } };
        Assert.StartsWith("https://cdn.example:9443/live/", new StreamUrlService().BuildLinks(session, 1).HlsUrl);
        session.ServerInfo.Url = "cdn.example/unsafe";
        Assert.StartsWith("https://provider.example:8443/live/", new StreamUrlService().BuildLinks(session, 1).HlsUrl);
    }

    [Theory]
    [InlineData("ftp://provider.example")]
    [InlineData("https://evil@provider.example")]
    [InlineData("https://provider.example?token=secret")]
    public void StreamUrls_RejectUnsafeOrigin(string origin)
    {
        var session = TestSupport.Session();
        Assert.Throws<InvalidOperationException>(() => new StreamUrlService().BuildLinks(new XtreamSession
        { ServerUrl = origin, Username = session.Username, Password = session.Password, UserInfo = session.UserInfo }, 1));
    }

    [Fact]
    public void State_NotifiesAndKeepsStableOwnerAcrossLogout()
    {
        var state = new SessionState();
        var events = 0;
        state.Changed += () => events++;
        Assert.False(state.IsAuthenticated);
        state.MarkHydrated();
        state.SetSession(TestSupport.Session());
        var owner = state.OwnerKey;
        state.Clear();
        Assert.True(state.IsHydrated);
        Assert.False(state.IsAuthenticated);
        state.SetSession(TestSupport.Session());
        Assert.Equal(owner, state.OwnerKey);
        Assert.Equal(4, events);
        Assert.Equal(64, owner.Length);
    }

    [Fact]
    public void Persistence_RoundTripsAndRejectsTamperingAndOtherKeys()
    {
        var persistence = new SessionPersistence(TestSupport.Protection());
        Assert.Equal("streamlink.session", persistence.StorageKeyName);
        var stored = persistence.Protect(TestSupport.Session());
        Assert.DoesNotContain("p@ss word", stored);
        Assert.Equal("user name", persistence.Unprotect(stored)?.Username);
        Assert.Null(persistence.Unprotect(stored + "invalid"));
        Assert.Null(new SessionPersistence(TestSupport.Protection()).Unprotect(stored));
        Assert.Null(persistence.Unprotect(null));
        Assert.Equal("value", persistence.UnprotectString(persistence.ProtectString("value")));
    }

    [Fact]
    public void UserInfo_ParsesNumericStringsAndExpiration()
    {
        var user = System.Text.Json.JsonSerializer.Deserialize<XtreamUserInfo>("{\"auth\":\"1\",\"active_cons\":\"2\",\"exp_date\":\"1700000000\"}")!;
        Assert.Equal(2, user.ActiveConnections);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), user.Expiration);
        user.ExpirationDate = "not a timestamp";
        Assert.Null(user.Expiration);
    }
}

public class XtreamApiTests
{
    [Fact]
    public async Task Login_EncodesCredentialsAndRejectsInvalidAuthentication()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{\"user_info\":{\"auth\":\"1\",\"active_cons\":\"2\"}}"));
        var clients = new StubClients(handler);
        var api = new XtreamApiService(clients);
        Assert.Null(await api.LoginAsync("ftp://provider.example", "u", "p"));
        var session = await api.LoginAsync(" https://provider.example/ ", "a+b", "p &" );
        Assert.NotNull(session);
        Assert.Equal(2, session.UserInfo.ActiveConnections);
        Assert.Equal("Xtream", clients.LastName);
        Assert.Equal("https://provider.example/player_api.php?username=a%2Bb&password=p%20%26", handler.Requests.Single().AbsoluteUri);
    }

    [Fact]
    public async Task Api_RefreshAndParsesListsAcrossSupportedShapes()
    {
        var handler = new StubHandler(request =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("get_live_categories")) return StubHandler.Json("[{\"category_id\":\"7\",\"category_name\":\"News\"}]");
            if (query.Contains("get_live_streams")) return StubHandler.Json("{\"first\":[{\"stream_id\":\"12\",\"name\":\"One\"}],\"second\":[{\"stream_id\":13,\"name\":\"Two\"}]}");
            return StubHandler.Json("{\"user_info\":{\"auth\":1}}");
        });
        var api = new XtreamApiService(new StubClients(handler));
        var session = TestSupport.Session();
        Assert.NotNull(await api.RefreshAccountAsync(session));
        Assert.Equal(2, (await api.GetChannelsAsync(session, "a & b")).Count);
        Assert.Single(await api.GetCategoriesAsync(session));
        Assert.Contains("category_id=a%20%26%20b", handler.Requests[1].Query);
    }

    [Theory]
    [InlineData("not json", HttpStatusCode.OK)]
    [InlineData("[]", HttpStatusCode.ServiceUnavailable)]
    [InlineData("{\"user_info\":{\"auth\":0}}", HttpStatusCode.OK)]
    public async Task Api_FailsClosedOnBadLogin(string body, HttpStatusCode status)
    {
        var api = new XtreamApiService(new StubClients(new StubHandler(_ => StubHandler.Json(body, status))));
        Assert.Null(await api.LoginAsync("https://provider.example", "u", "p"));
    }

    [Fact]
    public async Task Api_ReturnsEmptyListsOnMalformedOrUnavailableResponses()
    {
        var api = new XtreamApiService(new StubClients(new StubHandler(_ => StubHandler.Json("invalid"))));
        Assert.Empty(await api.GetCategoriesAsync(TestSupport.Session()));
        Assert.Empty(await api.GetChannelsAsync(TestSupport.Session(), "1"));
    }
}