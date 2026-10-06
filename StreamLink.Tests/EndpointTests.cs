using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Tests;

public class EndpointTests : IClassFixture<EndpointFactory>
{
    private readonly EndpointFactory factory;
    public EndpointTests(EndpointFactory factory) => this.factory = factory;

    [Fact]
    public async Task Endpoint_RejectsMissingWrongAndOversizedGrantsOrOrigins()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/external-link", new { grant = "x" })).StatusCode);
        using var wrongOrigin = Request("x", "http://elsewhere.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(wrongOrigin)).StatusCode);
        using var oversized = Request(new string('a', 8193));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(oversized)).StatusCode);
        using var invalid = Request("invalid");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(invalid)).StatusCode);
    }

    [Fact]
    public async Task Endpoint_ReturnsResolvedUrlWithPrivateHeaders()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        using var scope = factory.Services.CreateScope();
        var grant = scope.ServiceProvider.GetRequiredService<ExternalLinkService>()
            .CreateChannelGrant("https://provider.example/live/user/pass/1.m3u8");
        using var request = Request(grant);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("https://cdn.example/video", (await response.Content.ReadFromJsonAsync<Resolved>())?.Url);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/watch")]
    [InlineData("/watch/does-not-exist")]
    public async Task PublicPages_DoNotRenderContentBeforeHydration(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("auth-loading", html);
        Assert.Contains("id=\"streamlink-reconnect\"", html);
        Assert.Contains("js/reconnect.js", html);
        Assert.Contains("autostart=\"false\"", html);
        Assert.DoesNotContain("provider.example", html);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task WatchPage_DoesNotExposeShareOrProviderBeforeHydration()
    {
        using var scope = factory.Services.CreateScope();
        var shares = scope.ServiceProvider.GetRequiredService<IShareLinkService>();
        var share = await shares.CreateAsync(TestSupport.Session(), "owner", new XtreamChannel { StreamId = 99, Name = "Test Channel" }, "News", 1);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        var html = await client.GetStringAsync($"/watch/{share.Token}");
        Assert.Contains("auth-loading", html);
        Assert.DoesNotContain("<h1>Test Channel</h1>", html);
        Assert.DoesNotContain("p@ss word", html);
        Assert.DoesNotContain("provider.example", html);
    }

    [Fact]
    public async Task SharePreview_ContainsSafeActiveMetadataAndAbsoluteImage()
    {
        using var scope = factory.Services.CreateScope();
        var shares = scope.ServiceProvider.GetRequiredService<IShareLinkService>();
        var share = await shares.CreateAsync(TestSupport.Session(), "owner", new XtreamChannel { StreamId = 100, Name = "News & Sports <Live>" }, "News", 1);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        var html = await client.GetStringAsync($"/watch/{share.Token}");
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("property=\"og:title\" content=\"News &amp; Sports &lt;Live&gt; &#xB7; StreamLink\"", html);
        Assert.Contains("property=\"og:description\"", html);
        Assert.Contains($"property=\"og:url\" content=\"https://streamlink.example.com/watch/{share.Token}\"", html);
        Assert.Contains("property=\"og:image\" content=\"https://streamlink.example.com/streamlink-preview.png\"", html);
        Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", html);
        Assert.Contains("name=\"robots\" content=\"noindex, nofollow\"", html);
        Assert.DoesNotContain("provider.example", html);
        Assert.DoesNotContain("p@ss word", html);

        await shares.RevokeAsync(share.Token, "owner");
        html = await client.GetStringAsync($"/watch/{share.Token}");
        Assert.Contains("property=\"og:title\" content=\"Shared stream &#xB7; StreamLink\"", html);
        Assert.DoesNotContain("News &amp; Sports", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/tv")]
    [InlineData("/tv/7")]
    [InlineData("/channel/99")]
    [InlineData("/watch")]
    [InlineData("/watch/missing")]
    public async Task PublicPreview_HasGenericMetadataAndBrandedImage(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        var html = await client.GetStringAsync(path);
        Assert.Contains("property=\"og:site_name\" content=\"StreamLink\"", html);
        Assert.Contains("property=\"og:image\" content=\"https://streamlink.example.com/streamlink-preview.png\"", html);
        Assert.Contains("name=\"twitter:image\" content=\"https://streamlink.example.com/streamlink-preview.png\"", html);
        using var image = await client.GetAsync("/streamlink-preview.png");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/tv/7?name=%F0%9F%93%BA%20News", "https://streamlink.example.com/tv/7")]
    [InlineData("/channel/99?name=%F0%9F%93%BA%20News&category=%F0%9F%8E%AC", "https://streamlink.example.com/channel/99")]
    public async Task LegacyLinks_PreviewCleanCanonicalUrls(string path, string expected)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        var html = await client.GetStringAsync(path);
        Assert.Contains($"property=\"og:url\" content=\"{expected}\"", html);
        Assert.DoesNotContain("%F0%9F", html);
    }

    [Fact]
    public async Task PublicPreview_UsesConfiguredHttpsOriginBehindProxy()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost") });
        var html = await client.GetStringAsync("/tv");
        Assert.Contains("property=\"og:url\" content=\"https://streamlink.example.com/tv\"", html);
        Assert.Contains("property=\"og:image\" content=\"https://streamlink.example.com/streamlink-preview.png\"", html);
    }

    private static HttpRequestMessage Request(string grant, string origin = "http://localhost")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/external-link") { Content = JsonContent.Create(new { grant }) };
        request.Headers.Add("Origin", origin);
        return request;
    }

    private sealed record Resolved(string Url);
}

public sealed class EndpointFactory : WebApplicationFactory<Program>
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"streamlink-web-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(root);
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../StreamLink")));
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:StreamLink"] = $"Data Source={Path.Combine(root, "test.db")}",
                    ["DataProtection:KeyDirectory"] = Path.Combine(root, "keys"),
                    ["PublicBaseUrl"] = "https://streamlink.example.com/" }));
        builder.ConfigureTestServices(services => services.AddHttpClient("ExternalLinkRedirect")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://cdn.example/video");
                return response;
            })));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(root))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}