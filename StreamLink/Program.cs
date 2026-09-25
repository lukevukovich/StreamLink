using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using System.Text;
using StreamLink.Components;
using StreamLink.Data;
using StreamLink.Services;

var builder = WebApplication.CreateBuilder(args);

// Xtream credentials are sent in the provider query string. Never emit those URLs to logs.
builder.Logging.AddFilter("System.Net.Http.HttpClient.Xtream", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.StreamProxy", LogLevel.Warning);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
var keyDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).SetApplicationName("StreamLink");
builder.Services.AddHttpClient("Xtream").ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));
// IPTV providers commonly redirect live manifests and segments to a CDN or edge server.
builder.Services.AddHttpClient("StreamProxy", client => client.Timeout = Timeout.InfiniteTimeSpan).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 10
});
builder.Services.AddDbContextFactory<StreamLinkDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("StreamLink") ?? "Data Source=streamlink.db"));
builder.Services.AddScoped<SessionState>();
builder.Services.AddScoped<SessionPersistence>();
builder.Services.AddScoped<IXtreamApiService, XtreamApiService>();
builder.Services.AddScoped<IStreamUrlService, StreamUrlService>();
builder.Services.AddScoped<IShareLinkService, ShareLinkService>();
builder.Services.AddScoped<IStreamProxyService, StreamProxyService>();
builder.Services.AddScoped<LiveTranscodeService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/stream/{token}/manifest.m3u8", async (string token, IStreamProxyService proxy, CancellationToken cancellationToken) =>
{
    var result = await proxy.GetManifestAsync(token, cancellationToken);
    return result is null ? Results.NotFound() : Results.Bytes(result.Content, result.ContentType);
});


app.MapGet("/stream/{token}/resource/{resourceToken}", async (HttpContext context, string token, string resourceToken, IStreamProxyService proxy) =>
{
    using var result = await proxy.GetResourceAsync(token, resourceToken, context.RequestAborted);
    if (result is null) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    context.Response.ContentType = result.ContentType;
    context.Response.Headers.CacheControl = "no-store";
    if (result.Content is not null) await context.Response.Body.WriteAsync(result.Content, context.RequestAborted);
    else if (result.Upstream is not null)
    {
        await using var upstream = await result.Upstream.Content.ReadAsStreamAsync(context.RequestAborted);
        await upstream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
});

app.MapGet("/stream/{token}/live.ts", async (HttpContext context, string token, IStreamProxyService proxy) =>
{
    await using var live = await proxy.GetLiveTsAsync(token, context.RequestAborted);
    if (live is null) { context.Response.StatusCode = StatusCodes.Status502BadGateway; return; }
    context.Response.ContentType = "video/mp2t";
    context.Response.Headers.CacheControl = "no-store";
    await using var upstream = await live.Response.Content.ReadAsStreamAsync(context.RequestAborted);
    await upstream.CopyToAsync(context.Response.Body, context.RequestAborted);
});

app.MapGet("/stream/{token}/compatible.ts", (HttpContext context, string token, LiveTranscodeService transcoder) => transcoder.StreamAsync(context, token));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StreamLinkDbContext>();
    db.Database.EnsureCreated();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
