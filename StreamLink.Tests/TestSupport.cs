using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StreamLink.Data;
using StreamLink.Models;
using StreamLink.Services;

namespace StreamLink.Tests;

internal static class TestSupport
{
    public static XtreamSession Session(string formats = "m3u8,ts") => new()
    {
        ServerUrl = "https://provider.example:8443",
        Username = "user name",
        Password = "p@ss word",
        UserInfo = new() { Auth = 1, AllowedOutputFormats = formats.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() }
    };

    public static IDataProtectionProvider Protection() => new EphemeralDataProtectionProvider();

    public static IConfiguration ShareConfiguration() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
        .Build();

    public static (ShareLinkService Service, IDbContextFactory<StreamLinkDbContext> Factory, SessionPersistence Persistence) Shares(string database)
    {
        var options = new DbContextOptionsBuilder<StreamLinkDbContext>().UseSqlite($"Data Source={database}").Options;
        var factory = new SimpleDbFactory(options);
        using (var db = factory.CreateDbContext()) db.Database.EnsureCreated();
        var persistence = new SessionPersistence(Protection());
        return (new ShareLinkService(factory, new StreamUrlService(), persistence, ShareConfiguration()), factory, persistence);
    }
}

internal sealed class SimpleDbFactory(DbContextOptions<StreamLinkDbContext> options) : IDbContextFactory<StreamLinkDbContext>
{
    public StreamLinkDbContext CreateDbContext() => new(options);
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json) };
}

internal sealed class StubClients(HttpMessageHandler handler) : IHttpClientFactory
{
    public string? LastName { get; private set; }
    public HttpClient CreateClient(string name)
    {
        LastName = name;
        return new HttpClient(handler, disposeHandler: false);
    }
}

internal sealed class StubShares : IShareLinkService
{
    public SharePlayback? Playback { get; set; }
    public int Lookups { get; private set; }
    public Task<SharePlayback?> GetPlaybackAsync(string token, CancellationToken cancellationToken = default)
    {
        Lookups++;
        return Task.FromResult(Playback);
    }

    public Task<ShareLink> CreateAsync(XtreamSession session, string ownerKey, XtreamChannel channel, string categoryName, int expirationHours, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ShareLink>> GetActiveAsync(string ownerKey, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ShareLink?> GetAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task RevokeAsync(string token, string ownerKey, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}