using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using StreamLink.Components;
using StreamLink.Data;
using StreamLink.Services;

var builder = WebApplication.CreateBuilder(args);

// Xtream credentials may appear in provider URLs. Never emit those URLs to logs.
builder.Logging.AddFilter("System.Net.Http.HttpClient.Xtream", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ExternalLinkRedirect", LogLevel.Warning);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
var keyDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).SetApplicationName("StreamLink");
builder.Services.AddHttpClient("Xtream").ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("ExternalLinkRedirect", client => client.Timeout = TimeSpan.FromSeconds(12))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddDbContextFactory<StreamLinkDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("StreamLink") ?? "Data Source=streamlink.db"));
builder.Services.AddScoped<SessionState>();
builder.Services.AddScoped<SessionPersistence>();
builder.Services.AddScoped<IXtreamApiService, XtreamApiService>();
builder.Services.AddScoped<IStreamUrlService, StreamUrlService>();
builder.Services.AddScoped<IShareLinkService, ShareLinkService>();
builder.Services.AddScoped<ExternalLinkService>();

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
// No media is served through StreamLink.
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.MapPost("/api/external-link", async (HttpContext context, ExternalLinkService links, ExternalLinkRequest request) =>
{
    if (request.Grant is null || request.Grant.Length > 8192 || !context.Request.Headers.TryGetValue("Origin", out var origin) ||
        !Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var source) ||
        source.GetLeftPart(UriPartial.Authority) != $"{context.Request.Scheme}://{context.Request.Host}")
        return Results.BadRequest();
    var url = await links.ResolveAsync(request.Grant, context.RequestAborted);
    return url is null ? Results.UnprocessableEntity() : Results.Json(new { url });
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StreamLinkDbContext>();
    db.Database.EnsureCreated();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

internal sealed record ExternalLinkRequest(string? Grant);
