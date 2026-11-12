using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Tests;

public class ExternalLinkTests
{
    private const string Source = "https://provider.example/live/user/pass/1.m3u8";

    [Theory]
    [InlineData("/watch/1.m3u8", "https://provider.example/watch/1.m3u8")]
    [InlineData("https://cdn.example/video?id=1", "https://cdn.example/video?id=1")]
    public async Task Redirect_AcceptsSafeRelativeAndAbsoluteLocations(string location, string expected)
    {
        var handler = new StubHandler(_ => Redirect(location));
        var clients = new StubClients(handler);
        var service = new ExternalLinkService(TestSupport.Protection(), clients, new StubShares(), TestSupport.ProviderPolicy(), NullLogger<ExternalLinkService>.Instance);
        Assert.Equal(expected, await service.ResolveAsync(service.CreateChannelGrant(Source), default));
        Assert.Equal("ExternalLinkRedirect", clients.LastName);
        Assert.Equal(Source, handler.Requests.Single().AbsoluteUri);
    }

    [Theory]
    [InlineData("https://cdn.example/user/pass/1.ts")]
    [InlineData("https://cdn.example/user%2520/pass/1.ts")]
    [InlineData("https://user:pass@cdn.example/video")]
    [InlineData("https://cdn.example/video#fragment")]
    [InlineData("javascript:alert(1)")]
    [InlineData(Source)]
    public async Task Redirect_RejectsUnsafeLocations(string location)
    {
        var service = new ExternalLinkService(TestSupport.Protection(), new StubClients(new StubHandler(_ => Redirect(location))), new StubShares(), TestSupport.ProviderPolicy(), NullLogger<ExternalLinkService>.Instance);
        Assert.Null(await service.ResolveAsync(service.CreateChannelGrant(Source), default));
    }

    [Fact]
    public async Task Redirect_RejectsMissingNonRedirectOrInvalidGrant()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new ExternalLinkService(TestSupport.Protection(), new StubClients(handler), new StubShares(), TestSupport.ProviderPolicy(), NullLogger<ExternalLinkService>.Instance);
        Assert.Null(await service.ResolveAsync("invalid", default));
        Assert.Null(await service.ResolveAsync(service.CreateChannelGrant("file:///video"), default));
        Assert.Null(await service.ResolveAsync(service.CreateChannelGrant(Source), default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ShareGrant_RechecksShareOnEveryResolution()
    {
        var shares = new StubShares { Playback = new(new ShareLink(), Source) };
        var service = new ExternalLinkService(TestSupport.Protection(), new StubClients(new StubHandler(_ => Redirect("https://cdn.example/ok"))), shares, TestSupport.ProviderPolicy(), NullLogger<ExternalLinkService>.Instance);
        var grant = service.CreateShareGrant("share");
        Assert.Equal("https://cdn.example/ok", await service.ResolveAsync(grant, default));
        shares.Playback = null;
        Assert.Null(await service.ResolveAsync(grant, default));
        Assert.Equal(2, shares.Lookups);
    }

    [Fact]
    public async Task Redirect_BlocksUnapprovedSourceFromExistingShareOrGrant()
    {
        const string unapproved = "http://127.0.0.1:8080/live/user/pass/1.m3u8";
        var handler = new StubHandler(_ => Redirect("https://cdn.example/ok"));
        var shares = new StubShares { Playback = new(new ShareLink(), unapproved) };
        var service = new ExternalLinkService(TestSupport.Protection(), new StubClients(handler), shares, TestSupport.ProviderPolicy(), NullLogger<ExternalLinkService>.Instance);
        Assert.Null(await service.ResolveAsync(service.CreateChannelGrant(unapproved), default));
        Assert.Null(await service.ResolveAsync(service.CreateShareGrant("share"), default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Redirect_AllowsHttpDestinationOnlyWhenExplicitlyApproved()
    {
        var clients = new StubClients(new StubHandler(_ => Redirect("http://cdn.example/video")));
        var blocked = new ExternalLinkService(TestSupport.Protection(), clients, new StubShares(), TestSupport.ProviderPolicy(), NullLogger<ExternalLinkService>.Instance);
        Assert.Null(await blocked.ResolveAsync(blocked.CreateChannelGrant(Source), default));

        var policy = new ProviderAccessPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderSecurity:AllowedOrigins:0"] = "https://provider.example",
            ["ProviderSecurity:AllowedOrigins:1"] = "http://cdn.example"
        }).Build());
        var allowed = new ExternalLinkService(TestSupport.Protection(), clients, new StubShares(), policy, NullLogger<ExternalLinkService>.Instance);
        Assert.Equal("http://cdn.example/video", await allowed.ResolveAsync(allowed.CreateChannelGrant(Source), default));
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }
}