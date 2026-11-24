using System.Text.Json.Serialization;
using System.Net;
using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Game;
using BluffOrBlighty.Api.Hubs;
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
    var options = RedisSettings.Parse(redisConnection);
    options.AbortOnConnectFail = false; // retry in the background after a later drop, rather than throwing

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

    // Refuse to start without Redis. Every player connection needs it (the SignalR backplane
    // subscribes per connection), so starting anyway would break every game. Failing here makes
    // Cloud Run reject the new revision and keep the previous one serving.
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (!redis.IsConnected && DateTime.UtcNow < deadline) await Task.Delay(250);
    if (!redis.IsConnected)
        throw new InvalidOperationException(
            $"Can't connect to Redis at {RedisSettings.Describe(options)}. Check the Redis:ConnectionString " +
            "setting (in production, the Secret Manager secret named by the REDIS_SECRET variable).");
    Console.WriteLine($"Connected to Redis at {RedisSettings.Describe(options)}");

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

// Health reports the store in use and, for Redis, whether it's actually reachable right now
// (503 if not), so the post-deploy smoke test catches a broken connection, not just a setting.
app.MapGet("/api/health", (GameManager games, IServiceProvider services) =>
{
    var redis = services.GetService<IConnectionMultiplexer>();
    var healthy = redis is null || redis.IsConnected;
    var body = new { status = healthy ? "ok" : "redis unreachable", store = games.StoreKind };
    return healthy ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/api/maps", () => MapPresets.All);
app.MapGet("/api/campaign", () => CampaignLevels.Views());
app.MapHub<GameHub>("/hub/game");

// In production the built React app is copied into wwwroot and served from here.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
