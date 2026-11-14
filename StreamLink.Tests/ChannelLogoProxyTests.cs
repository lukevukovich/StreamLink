using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using StreamLink.Services;

namespace StreamLink.Tests;

public class ChannelLogoProxyTests
{
    private static ProviderAccessPolicy Policy(params string[] origins) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(origins.Select((origin, index) =>
            new KeyValuePair<string, string?>($"ProviderSecurity:AllowedOrigins:{index}", origin)))
        .Build());

    [Fact]
    public async Task ApprovedHttpLogo_UsesProtectedGrantAndDetectsImageFormat()
    {
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2 };
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(png)
        });
        var proxy = new ChannelLogoProxy(TestSupport.Protection(), Policy("http://provider.example"), new StubClients(handler));
        var source = proxy.GetSource("http://provider.example/icons/logo.png?token=secret")!;

        Assert.StartsWith("/channel-logo/", source);
        Assert.DoesNotContain("secret", source);
        var result = await proxy.FetchAsync(source["/channel-logo/".Length..], CancellationToken.None);
        Assert.Equal("image/png", result!.ContentType);
        Assert.Equal(png, result.Content);
        Assert.Equal("http://provider.example/icons/logo.png?token=secret", handler.Requests.Single().AbsoluteUri);
        Assert.Equal("https://provider.example/logo.png", proxy.GetSource("https://provider.example/logo.png"));
        Assert.Equal("http://elsewhere.example/logo.png", proxy.GetSource("http://elsewhere.example/logo.png"));
    }

    [Fact]
    public async Task InvalidOrNoLongerApprovedGrant_DoesNotContactProvider()
    {
        var protection = TestSupport.Protection();
        var handler = new StubHandler(_ => throw new InvalidOperationException("Unexpected request"));
        var allowed = new ChannelLogoProxy(protection, Policy("http://provider.example"), new StubClients(handler));
        var denied = new ChannelLogoProxy(protection, Policy(), new StubClients(handler));
        var grant = allowed.GetSource("http://provider.example/logo.png")!["/channel-logo/".Length..];

        Assert.Null(await denied.FetchAsync(grant, CancellationToken.None));
        Assert.Null(await allowed.FetchAsync("invalid", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SensitiveShareLogo_RequiresAnApprovedOriginAndHidesItsUrl()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        });
        var protection = TestSupport.Protection();
        const string url = "https://cdn.example/logo.png?token=secret";
        var allowed = new ChannelLogoProxy(protection, Policy("https://cdn.example"), new StubClients(handler));
        var denied = new ChannelLogoProxy(protection, Policy(), new StubClients(handler));
        var source = allowed.GetSource(url, requireProxy: true)!;

        Assert.StartsWith("/channel-logo/", source);
        Assert.DoesNotContain("secret", source);
        Assert.Null(denied.GetSource(url, requireProxy: true));
        Assert.NotNull(await allowed.FetchAsync(source["/channel-logo/".Length..], CancellationToken.None));
        Assert.Equal(url, handler.Requests.Single().AbsoluteUri);
    }

    [Fact]
    public async Task ProviderLogo_OnDifferentHost_RetriesHttpAfterTlsFailure()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.Scheme == "https")
                throw new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS handshake failed");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            };
        });
        var clients = new StubClients(handler);
        var proxy = new ChannelLogoProxy(TestSupport.Protection(), Policy("https://provider.example"), clients);
        var source = proxy.GetSource("https://images.example:2086/logo.png", providerUrl: "https://provider.example",
            allowHttpFallback: true)!;

        Assert.StartsWith("/channel-logo/", source);
        Assert.NotNull(await proxy.FetchAsync(source["/channel-logo/".Length..], CancellationToken.None));
        Assert.Equal(new[] { "https://images.example:2086/logo.png", "http://images.example:2086/logo.png" },
            handler.Requests.Select(uri => uri.AbsoluteUri));
        Assert.Equal("PublicChannelLogo", clients.LastName);
    }

    [Theory]
    [InlineData("https://images.example/logo.png?token=secret", true)]
    [InlineData("https://images.example/logo.png", false)]
    public async Task ProviderLogo_DoesNotDowngradeSensitiveOrDisallowedUrls(string url, bool fallback)
    {
        var handler = new StubHandler(_ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS failed"));
        var proxy = new ChannelLogoProxy(TestSupport.Protection(), Policy("https://provider.example"), new StubClients(handler));
        var source = proxy.GetSource(url, providerUrl: "https://provider.example", allowHttpFallback: fallback)!;

        await Assert.ThrowsAsync<ChannelLogoFetchException>(() => proxy.FetchAsync(source["/channel-logo/".Length..], CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProviderLogo_StopsWorkingWhenProviderIsRemoved()
    {
        var protection = TestSupport.Protection();
        var handler = new StubHandler(_ => throw new InvalidOperationException("Unexpected request"));
        var allowed = new ChannelLogoProxy(protection, Policy("https://provider.example"), new StubClients(handler));
        var denied = new ChannelLogoProxy(protection, Policy(), new StubClients(handler));
        var source = allowed.GetSource("https://images.example/logo.png", providerUrl: "https://provider.example")!;
        Assert.Null(await denied.FetchAsync(source["/channel-logo/".Length..], CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.31.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002:0808:0808::1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    public void PublicImageDestinations_ExcludeNonPublicAddresses(string address, bool expected) =>
        Assert.Equal(expected, PublicImageConnection.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task PublicImageClient_RejectsLoopbackBeforeConnecting()
    {
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectCallback = PublicImageConnection.ConnectAsync };
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://127.0.0.1:1/logo.png"));
        Assert.Contains("No permitted public image destination", exception.ToString());
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("oversized")]
    [InlineData("oversized-stream")]
    [InlineData("not-image")]
    public async Task Fetch_RejectsRedirectsOversizedAndNonImages(string failure)
    {
        var handler = new StubHandler(_ =>
        {
            if (failure == "redirect")
                return new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://elsewhere.example/") } };
            if (failure == "oversized-stream")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new NonSeekableStream(new byte[2 * 1024 * 1024 + 1]))
                };
            var bytes = failure == "oversized" ? new byte[2 * 1024 * 1024 + 1] : "<html>not an image</html>"u8.ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        var proxy = new ChannelLogoProxy(TestSupport.Protection(), Policy("http://provider.example"), new StubClients(handler));
        var grant = proxy.GetSource("http://provider.example/logo")!["/channel-logo/".Length..];

        await Assert.ThrowsAsync<ChannelLogoFetchException>(() => proxy.FetchAsync(grant, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Svg_IsAcceptedWhenProviderIdentifiesIt()
    {
        var handler = new StubHandler(_ =>
        {
            var content = new ByteArrayContent("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var proxy = new ChannelLogoProxy(TestSupport.Protection(), Policy("http://provider.example"), new StubClients(handler));
        var grant = proxy.GetSource("http://provider.example/logo")!["/channel-logo/".Length..];

        Assert.Equal("image/svg+xml", (await proxy.FetchAsync(grant, CancellationToken.None))!.ContentType);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
