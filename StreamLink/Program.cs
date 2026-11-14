using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
using StreamLink.Api;
using StreamLink.Components;
using StreamLink.Data;
using StreamLink.Services;

var builder = WebApplication.CreateBuilder(args);

// Xtream credentials may appear in provider URLs. Never emit those URLs to logs.
builder.Logging.AddFilter("System.Net.Http.HttpClient.Xtream", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ExternalLinkRedirect", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ChannelLogo", LogLevel.None);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("streamlink", new OpenApiInfo { Title = "StreamLink API", Version = "current", Description = "Provider account, live catalog, and share management. Use HTTPS and treat bearer tokens and share codes as secrets." });
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "StreamLink.xml"));
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "One-hour token returned by POST /api/sessions."
    });
    options.OperationFilter<ApiSecurityOperationFilter>();
});
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiAuthenticationHandler>(ApiAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("api-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many login attempts").ExecuteAsync(context.HttpContext);
    };
});
var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).SetApplicationName("StreamLink");
builder.Services.AddHttpClient("Xtream").ConfigureHttpClient(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamLink/1.0");
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddHttpClient("ExternalLinkRedirect", client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamLink/1.0");
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddHttpClient("ChannelLogo", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamLink/1.0");
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddDbContextFactory<StreamLinkDbContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString("StreamLink") ?? "Data Source=streamlink.db"));
builder.Services.AddScoped<SessionState>();
builder.Services.AddScoped<SessionPersistence>();
builder.Services.AddScoped<ThemePreferenceService>();
builder.Services.AddSingleton<ProviderAccessPolicy>();
builder.Services.AddSingleton<ChannelLogoProxy>();
builder.Services.AddScoped<IXtreamApiService, XtreamApiService>();
builder.Services.AddScoped<IStreamUrlService, StreamUrlService>();
builder.Services.AddScoped<IShareLinkService, ShareLinkService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddSingleton<IApiSessionTokens, ApiSessionTokens>();
builder.Services.AddScoped<ExternalLinkService>();
builder.Services.AddHostedService<ExpiredShareLinkCleanupService>();

var app = builder.Build();
_ = app.Services.GetRequiredService<ProviderAccessPolicy>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseWhen(context => IsPublicApi(context) || context.Request.Path.StartsWithSegments("/swagger"), branch =>
    branch.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
}));

app.Use(async (context, next) =>
{
    if (!app.Environment.IsDevelopment() && !context.Request.IsHttps &&
        (IsPublicApi(context) || context.Request.Path.StartsWithSegments("/swagger")))
    {
        await Results.Problem(statusCode: 400, title: "HTTPS is required.").ExecuteAsync(context);
        return;
    }
    await next();
});
app.UseWhen(IsPublicApi, branch =>
{
    branch.UseExceptionHandler(errors => errors.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, title) = exception switch
        {
            ProviderAccessException => (403, "The provider origin is not approved."),
            ProviderResponseException => (502, "The provider request failed."),
            BadHttpRequestException bad when bad.StatusCode is >= 400 and < 500 => (bad.StatusCode, "Invalid request."),
            _ => (500, "The request could not be completed.")
        };
        app.Logger.LogWarning("API request failed with status {Status}.", status);
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
    }));
    branch.UseStatusCodePages(async statusContext =>
    {
        var context = statusContext.HttpContext;
        await Results.Problem(statusCode: context.Response.StatusCode, title: "Request failed.").ExecuteAsync(context);
    });
});
app.UseHttpsRedirection();
if (app.Environment.IsDevelopment() || !builder.Configuration.GetValue("Api:SwaggerDevelopmentOnly", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("streamlink/swagger.json", "StreamLink API"));
}
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamLinkDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsPublicApi(HttpContext context) =>
    context.Request.Path.StartsWithSegments("/api") &&
    !context.Request.Path.StartsWithSegments("/api/external-link");

public partial class Program;
