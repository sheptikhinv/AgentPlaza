using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPlaza.Api;
using AgentPlaza.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlazaSecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection("Bootstrap"));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddDbContext<PlazaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PresenceHub>();
builder.Services.AddScoped<PresenceService>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddHostedService<PresenceExpirationWorker>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "agent_plaza_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/v1/auth/token", async (
    TokenRequest request,
    HttpContext context,
    IOptions<PlazaSecurityOptions> security) =>
{
    if (string.IsNullOrWhiteSpace(request.Token)
        || string.IsNullOrWhiteSpace(security.Value.ViewerToken)
        || !TokenHash.EqualsToken(request.Token, security.Value.ViewerToken))
    {
        return Results.Unauthorized();
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "plaza-viewer")],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(new ClaimsPrincipal(identity));
    return Results.NoContent();
});

app.MapPost("/v1/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/v1/events", async (
    AgentEventRequest request,
    HttpContext context,
    PresenceService presenceService,
    CancellationToken cancellationToken) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var result = await presenceService.IngestAsync(request, authorization[7..].Trim(), cancellationToken);
    return result.Status switch
    {
        IngestStatus.Accepted => Results.Accepted(),
        IngestStatus.Duplicate => Results.Ok(new { duplicate = true }),
        IngestStatus.Invalid => Results.BadRequest(new { error = result.Error }),
        _ => Results.Unauthorized(),
    };
});

app.MapGet("/v1/plaza", async (PresenceService presenceService, CancellationToken cancellationToken) =>
    Results.Ok(await presenceService.GetPlazaAsync(cancellationToken)))
    .RequireAuthorization();

app.MapGet("/v1/plaza/stream", async (
    HttpContext context,
    PresenceService presenceService,
    PresenceHub hub,
    IOptions<JsonOptions> jsonOptions,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.ContentType = "text/event-stream";
    await context.Response.StartAsync(cancellationToken);

    using var subscription = hub.Subscribe();
    var snapshot = await presenceService.GetPlazaAsync(cancellationToken);
    await WriteSseAsync(context.Response, "plaza.snapshot", snapshot, jsonOptions.Value.SerializerOptions, cancellationToken);

    while (!cancellationToken.IsCancellationRequested)
    {
        var updateAvailable = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
        var keepAlive = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        if (await Task.WhenAny(updateAvailable, keepAlive) == keepAlive)
        {
            await context.Response.WriteAsync(": keepalive\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            continue;
        }

        if (!await updateAvailable)
        {
            break;
        }

        while (subscription.Reader.TryRead(out var presence))
        {
            await WriteSseAsync(context.Response, "presence.updated", presence, jsonOptions.Value.SerializerOptions, cancellationToken);
        }
    }
}).RequireAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (PlazaDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("Database is unavailable", statusCode: StatusCodes.Status503ServiceUnavailable));

if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();

static async Task WriteSseAsync<T>(
    HttpResponse response,
    string eventName,
    T payload,
    JsonSerializerOptions options,
    CancellationToken cancellationToken)
{
    await response.WriteAsync($"event: {eventName}\n", cancellationToken);
    await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, options)}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}
