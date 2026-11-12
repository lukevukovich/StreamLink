using Microsoft.Extensions.Configuration;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Tests;

public class ProviderAccessTests
{
    [Fact]
    public void Policy_DeniesByDefaultAndRequiresExactOrigin()
    {
        var empty = Policy();
        Assert.False(empty.IsAllowed(new Uri("https://provider.example")));

        var policy = Policy("https://provider.example", "http://private.example:8080");
        Assert.True(policy.IsAllowed(new Uri("https://PROVIDER.example:443/live/1")));
        Assert.True(policy.IsAllowed(new Uri("http://private.example:8080/live/1")));
        Assert.False(policy.IsAllowed(new Uri("http://provider.example")));
        Assert.False(policy.IsAllowed(new Uri("https://provider.example:8443")));
        Assert.False(policy.IsAllowed(new Uri("https://provider.example.attacker.invalid")));
        Assert.False(policy.IsAllowed(new Uri("https://provider.example@attacker.invalid")));
        Assert.False(policy.IsAllowed(new Uri("https://provider.example/live/1#fragment")));
        Assert.False(policy.IsAllowed(new Uri("http://127.0.0.1:8080")));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://user:pass@provider.example")]
    [InlineData("https://provider.example/path")]
    [InlineData("https://provider.example?secret=1")]
    [InlineData("ftp://provider.example")]
    public void Policy_RejectsInvalidConfiguration(string origin) =>
        Assert.Throws<InvalidOperationException>(() => Policy(origin));

    [Fact]
    public async Task LoginAndExistingSession_DenyUnapprovedDestinationsBeforeRequest()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{\"user_info\":{\"auth\":1}}"));
        var service = new XtreamApiService(new StubClients(handler), Policy("https://provider.example"));
        await Assert.ThrowsAsync<ProviderAccessException>(() => service.LoginAsync("http://127.0.0.1:8080", "user", "pass"));
        await Assert.ThrowsAsync<ProviderAccessException>(() => service.GetCategoriesAsync(TestSupport.Session()));
        await Assert.ThrowsAsync<ProviderAccessException>(() => service.LoginAsync("https://provider.example?secret=1", "user", "pass"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void StreamUrl_DoesNotSendCredentialsToUnapprovedAdvertisedServer()
    {
        var existing = TestSupport.Session();
        var session = new XtreamSession
        {
            ServerUrl = existing.ServerUrl, Username = existing.Username, Password = existing.Password,
            UserInfo = existing.UserInfo,
            ServerInfo = new() { Url = "127.0.0.1", Protocol = "http", Port = "8080" }
        };
        Assert.Throws<ProviderAccessException>(() => new StreamUrlService(TestSupport.ProviderPolicy()).BuildLinks(session, 1));
    }

    private static ProviderAccessPolicy Policy(params string[] origins)
    {
        var values = origins.Select((origin, index) =>
            new KeyValuePair<string, string?>($"ProviderSecurity:AllowedOrigins:{index}", origin));
        return new ProviderAccessPolicy(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}
