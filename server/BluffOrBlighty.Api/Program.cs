using System.Text.Json.Serialization;
using System.Net;
using BluffOrBlighty.Api.Accounts;
using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Data;
using BluffOrBlighty.Api.Game;
using BluffOrBlighty.Api.Hubs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameManager>();
builder.Services.AddSingleton<IGameBroadcaster, GameBroadcaster>();

var signalR = builder.Services
    .AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Game state: Redis when configured (production), so games survive deploys and can be shared
// by more than one server instance; otherwise in memory (local development, tests).
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    var options = ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = false; // keep retrying in the background rather than failing startup

    // Hosted Redis bills per command, so keep the client's background chatter down:
    // ping idle connections every 2 minutes (default 1), re-check server config every
    // 10 minutes (default 1), skip CLIENT SETINFO and the tie-breaker GET on connect (there's
    // only one Redis), and never send CONFIG/CLUSTER (often blocked on hosted Redis anyway).
    options.KeepAlive = 120;
    options.ConfigCheckSeconds = 600;
    options.SetClientLibrary = false;
    options.TieBreaker = "";
    options.CommandMap = CommandMap.Create(["CONFIG", "CLUSTER"], available: false);

    var redis = await ConnectionMultiplexer.ConnectAsync(options);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddSingleton<IGameStore, RedisGameStore>();

    // SignalR backplane: a message for a player connected to another instance gets there via Redis.
    signalR.AddStackExchangeRedis(o =>
    {
        o.ConnectionFactory = _ => Task.FromResult<IConnectionMultiplexer>(redis);
        o.Configuration.ChannelPrefix = RedisChannel.Literal("bluff-or-blighty");
    });
}
else
{
    builder.Services.AddSingleton<InMemoryGameStore>();
    builder.Services.AddSingleton<IGameStore>(sp => sp.GetRequiredService<InMemoryGameStore>());
}

builder.Services.AddHostedService<GameCleanupService>();

// Accounts: Google sign-in and a Postgres database, when one is configured. Without it the game
// runs as before (anonymous, campaign progress in the browser) and the client hides sign-in.
var databaseConnection = builder.Configuration.GetConnectionString("Database");
var accountsEnabled = !string.IsNullOrWhiteSpace(databaseConnection);
builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection("Auth"));
if (accountsEnabled)
{
    builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(databaseConnection));
    builder.Services.AddScoped<AccountService>();
    builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
    builder.Services.AddSingleton<IResultRecorder, DbResultRecorder>();

    // Cookie encryption keys live in the database, so sign-ins survive deploys.
    builder.Services.AddDataProtection().SetApplicationName("bluff-or-blighty").PersistKeysToDbContext<AppDbContext>();

    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.Cookie.Name = "bob_session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Secure on HTTPS (see forwarded headers)
            o.ExpireTimeSpan = TimeSpan.FromDays(90);
            o.SlidingExpiration = true;
            // An API, not pages: answer 401/403 instead of redirecting to a login page.
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
        });
    builder.Services.AddAuthorization();
}
else
{
    builder.Services.AddSingleton<IResultRecorder, NoResultRecorder>();
}

// Cloud Run terminates HTTPS and forwards plain HTTP; trust its X-Forwarded-Proto so the app
// knows requests were HTTPS (and marks cookies Secure).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Bots read place labels from the same vector tiles the players' map uses.
builder.Services.AddMemoryCache();
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Bots"));
builder.Services.AddSingleton(new BotTiming());
builder.Services.AddSingleton<BotDriver>();
builder.Services
    .AddHttpClient<IPlaceSource, VectorTilePlaceSource>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("BluffOrBlighty/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
var app = builder.Build();

app.UseForwardedHeaders();

if (accountsEnabled)
{
    // Bring the database schema up to date. (EF Core takes a lock, so overlapping instances
    // during a deploy don't both migrate.)
    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapAccountEndpoints();
}
else
{
    app.MapAccountsDisabled();
}

app.MapGet("/api/health", (GameManager games) =>
    Results.Ok(new { status = "ok", store = games.StoreKind, accounts = accountsEnabled }));
app.MapGet("/api/maps", () => MapPresets.All);
app.MapGet("/api/campaign", () => CampaignLevels.Views());
app.MapHub<GameHub>("/hub/game");

// In production the built React app is copied into wwwroot and served from here.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
