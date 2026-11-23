using System.Text.Json.Serialization;
using System.Net;
using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Game;
using BluffOrBlighty.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameManager>();
builder.Services.AddHostedService<GameCleanupService>();
builder.Services.AddSingleton<IGameBroadcaster, GameBroadcaster>();

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
builder.Services
    .AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/maps", () => MapPresets.All);
app.MapHub<GameHub>("/hub/game");

// In production the built React app is copied into wwwroot and served from here.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
