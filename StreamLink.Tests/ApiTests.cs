using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StreamLink.Api;
using StreamLink.Services;

namespace StreamLink.Tests;

public class ApiTests
{
    [Fact]
    public async Task Api_AuthenticatesBrowsesAndManagesOwnedShares()
    {
        using var root = new EndpointFactory();
        using var factory = WithProvider(root);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/shares")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(client, "https://not-approved.invalid", "alice", "password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "https://provider.example", "wrong", "password")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await Login(client, "https://provider.example", "unavailable", "password")).StatusCode);

        using var login = await Login(client, "https://provider.example", "alice", "password");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var json = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var session = JsonSerializer.Deserialize<SessionResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.InRange(session.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(58), DateTimeOffset.UtcNow.AddMinutes(61));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var account = await client.GetFromJsonAsync<AccountResponse>("/api/account");
        Assert.Equal("alice", account!.Username);
        var categories = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories?sort=name");
        Assert.Equal(new[] { "Alpha", "Zebra" }, categories!.Select(c => c.Name));
        var channels = await client.GetFromJsonAsync<ChannelResponse[]>("/api/channels?categoryId=7&search=NEWS&sort=name-desc");
        Assert.Equal(new[] { "News Z", "News A" }, channels!.Select(c => c.Name));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/channels?categoryId=7&sort=bad")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/channels/42?categoryId=7")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/channels/42?categoryId=999")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/shares", new CreateShareRequest(999, "7", 4))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/shares", new CreateShareRequest(42, "7", 0))).StatusCode);
        using var created = await client.PostAsJsonAsync("/api/shares", new CreateShareRequest(42, "7", 4));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var share = (await created.Content.ReadFromJsonAsync<ShareResponse>())!;
        Assert.Equal($"/api/shares/{share.Code}", created.Headers.Location!.OriginalString);
        Assert.Equal("/watch/" + share.Code, share.WatchPath);
        Assert.Equal("Alpha", share.CategoryName);
        using var reused = await client.PostAsJsonAsync("/api/shares", new CreateShareRequest(42, "7", 8));
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Equal(share.ExpiresAt, (await reused.Content.ReadFromJsonAsync<ShareResponse>())!.ExpiresAt);
        Assert.Single((await client.GetFromJsonAsync<ShareResponse[]>("/api/shares?sort=expiring"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/shares?sort=bad")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/shares/{share.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/shares/{share.Code}/expiration", new UpdateShareRequest(12))).StatusCode);

        using var otherLogin = await Login(client, "https://provider.example", "bob", "password");
        var other = (await otherLogin.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.AccessToken);
        Assert.Empty((await client.GetFromJsonAsync<ShareResponse[]>("/api/shares"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/shares/{share.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/shares/{share.Code}/expiration", new UpdateShareRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/shares/{share.Code}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/shares/{share.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/shares/{share.Code}")).StatusCode);
        using var defaultLifetime = await client.PostAsJsonAsync("/api/shares", new { streamId = 43, categoryId = "7" });
        Assert.Equal(HttpStatusCode.Created, defaultLifetime.StatusCode);
        var defaultShare = (await defaultLifetime.Content.ReadFromJsonAsync<ShareResponse>())!;
        Assert.InRange(defaultShare.ExpiresAt, DateTimeOffset.UtcNow.AddHours(4).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(4).AddMinutes(1));
    }

    [Fact]
    public async Task Swagger_DocumentsRoutesAndBearerAuthentication()
    {
        using var root = new EndpointFactory();
        using var factory = WithProvider(root);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/streamlink/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/sessions", out var login));
        Assert.False(login.GetProperty("post").TryGetProperty("security", out _));
        Assert.Equal("Account", login.GetProperty("post").GetProperty("tags")[0].GetString());
        Assert.Equal("Verify provider credentials and create an API session.", login.GetProperty("post").GetProperty("summary").GetString());
        Assert.True(paths.GetProperty("/api/account").GetProperty("get").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/api/categories").GetProperty("get").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/api/shares").GetProperty("post").TryGetProperty("security", out _));
        Assert.Equal("Shares", paths.GetProperty("/api/shares").GetProperty("post").GetProperty("tags")[0].GetString());
        Assert.True(paths.TryGetProperty("/api/channels/{streamId}", out _));
        Assert.Equal("Catalog", paths.GetProperty("/api/channels/{streamId}").GetProperty("get").GetProperty("tags")[0].GetString());
        foreach (var route in new[] { "/api/channels", "/api/channels/{streamId}" })
        {
            var category = paths.GetProperty(route).GetProperty("get").GetProperty("parameters").EnumerateArray()
                .Single(parameter => parameter.GetProperty("name").GetString() == "categoryId");
            Assert.True(category.GetProperty("required").GetBoolean());
        }
        Assert.False(paths.TryGetProperty("/api/v1/sessions", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/categories")).StatusCode);
        Assert.True(document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        Assert.True(document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateShareRequest").TryGetProperty("description", out _));
    }

    [Theory]
    [InlineData("/api/channels")]
    [InlineData("/api/channels?categoryId=")]
    [InlineData("/api/channels?categoryId=%20%20")]
    [InlineData("/api/channels/42")]
    [InlineData("/api/channels/42?categoryId=")]
    [InlineData("/api/channels/42?categoryId=%20%20")]
    public async Task Channels_RequireCategoryId(string path)
    {
        using var root = new EndpointFactory();
        using var factory = WithProvider(root);
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IApiSessionTokens>().Issue(TestSupport.Session()).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Production_RequiresHttpsAndCanDisableSwagger()
    {
        using var root = new EndpointFactory();
        using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:SwaggerDevelopmentOnly"] = "true"
            }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(client, "https://provider.example", "alice", "password")).StatusCode);
        using var secureClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await secureClient.GetAsync("/swagger/streamlink/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task Production_ExposesSwaggerWhenConfigured()
    {
        using var root = new EndpointFactory();
        using var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:SwaggerDevelopmentOnly"] = "false"
            }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/streamlink/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_ReportsProviderFailureAndRejectsRemovedOrigin()
    {
        using var root = new EndpointFactory();
        using var factory = WithProvider(root);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<IApiSessionTokens>();
        var existing = TestSupport.Session();
        var unavailable = new StreamLink.Models.XtreamSession
        {
            ServerUrl = "https://provider.example", Username = "unavailable", Password = "password",
            UserInfo = existing.UserInfo, ServerInfo = existing.ServerInfo
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Issue(unavailable).Token);
        using var response = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var unapproved = new StreamLink.Models.XtreamSession
        {
            ServerUrl = "https://unapproved.invalid", Username = existing.Username, Password = existing.Password,
            UserInfo = existing.UserInfo, ServerInfo = existing.ServerInfo
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Issue(unapproved).Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/shares")).StatusCode);
    }

    [Fact]
    public async Task ChannelLookup_FallsBackWhenProviderRequiresCategory()
    {
        var handler = new StubHandler(request =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("get_live_categories"))
                return StubHandler.Json("[{\"category_id\":\"7\",\"category_name\":\"News\"}]");
            if (query.Contains("category_id=7"))
                return StubHandler.Json("[{\"stream_id\":42,\"name\":\"Example\"}]");
            return StubHandler.Json("{}", HttpStatusCode.BadRequest);
        });
        var xtream = new XtreamApiService(new StubClients(handler), TestSupport.ProviderPolicy());
        var catalog = new CatalogService(xtream, NullLogger<CatalogService>.Instance);
        var channel = await catalog.FindChannelAsync(TestSupport.Session(), 42);
        Assert.Equal("News", channel!.CategoryName);
        Assert.Equal("Example", channel.Channel.Name);
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        using var root = new EndpointFactory();
        using var factory = WithProvider(root);
        using var client = factory.CreateClient();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "https://provider.example", "wrong", "password")).StatusCode);
        using var limited = await Login(client, "https://provider.example", "wrong", "password");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task InvalidJson_ReturnsProblemDetails()
    {
        using var root = new EndpointFactory();
        using var factory = WithProvider(root);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/sessions",
            new StringContent("{invalid", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void ApiToken_RejectsTamperedExpiredAndBrowserSessions()
    {
        var protection = TestSupport.Protection();
        var tokens = new ApiSessionTokens(protection);
        var (token, _) = tokens.Issue(TestSupport.Session());
        Assert.NotNull(tokens.Read(token));
        Assert.Null(tokens.Read(token + "bad"));
        Assert.Null(tokens.Read(new SessionPersistence(protection).Protect(TestSupport.Session())));
        var expired = protection.CreateProtector("StreamLink.Api.Session.v1").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(TestSupport.Session()), DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Null(tokens.Read(expired));
        var oversized = TestSupport.Session();
        oversized.UserInfo.AllowedOutputFormats.Add(new string('x', 20000));
        Assert.Throws<ProviderResponseException>(() => tokens.Issue(oversized));
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string server, string username, string password) =>
        client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(server, username, password));

    private static WebApplicationFactory<Program> WithProvider(EndpointFactory factory) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => services.AddHttpClient("Xtream").ConfigurePrimaryHttpMessageHandler(() => new StubHandler(request =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("username=unavailable")) return StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
            if (query.Contains("username=wrong")) return StubHandler.Json("{\"user_info\":{\"auth\":0}}");
            if (query.Contains("action=get_live_categories"))
                return StubHandler.Json("[{\"category_id\":\"8\",\"category_name\":\"Zebra\"},{\"category_id\":\"7\",\"category_name\":\"Alpha\"}]");
            if (query.Contains("action=get_live_streams"))
                return StubHandler.Json("[{\"stream_id\":42,\"category_id\":\"7\",\"name\":\"News A\"},{\"stream_id\":43,\"category_id\":\"7\",\"name\":\"News Z\"}]");
            return StubHandler.Json("{\"user_info\":{\"auth\":1,\"status\":\"Active\",\"allowed_output_formats\":[\"m3u8\"]}}");
        }))));
}
