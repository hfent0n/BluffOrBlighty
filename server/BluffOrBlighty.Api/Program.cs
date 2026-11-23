using System.Text.Json.Serialization;
using BluffOrBlighty.Api.Game;
using BluffOrBlighty.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameManager>();
builder.Services.AddHostedService<GameCleanupService>();
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
