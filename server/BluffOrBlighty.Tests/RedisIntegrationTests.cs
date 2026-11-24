using System.Net;
using System.Text.Json.Serialization;
using BluffOrBlighty.Api.Game;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace BluffOrBlighty.Tests;

/// <summary>
/// Runs only when <c>REDIS_TEST_CONNECTION</c> points at a real Redis (CI starts one). With
/// <c>REDIS_TEST_REQUIRED=true</c> (set in CI) a missing Redis fails the tests rather than skipping them.
/// </summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "REDIS_TEST_CONNECTION";

    public RedisFactAttribute()
    {
        var configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));
        var required = Environment.GetEnvironmentVariable("REDIS_TEST_REQUIRED") == "true";
        if (!configured && !required)
            Skip = $"Set {ConnectionVariable} (e.g. localhost:6379, or run `docker compose up -d redis`) to run.";
    }
}

/// <summary>
/// The real server, in-process, against a real Redis, with players connecting over WebSockets
/// exactly as browsers do. Catches anything that breaks the Redis path: the store, the SignalR
/// backplane, or connections themselves (as a bad Redis setup once did in production).
/// </summary>
[Collection("Redis")] // they share one Redis and an environment variable; run them one at a time
public class RedisIntegrationTests
{
    /// <summary>One server instance. Several can share the same Redis, like Cloud Run revisions during a deploy.</summary>
    private sealed class ServerInstance : WebApplicationFactory<Program>
    {
        static ServerInstance()
        {
            // Program reads its settings while building, so supply Redis as the app would get it in production.
            var redis = Environment.GetEnvironmentVariable(RedisFactAttribute.ConnectionVariable)
                        ?? throw new InvalidOperationException($"{RedisFactAttribute.ConnectionVariable} isn't set.");
            Environment.SetEnvironmentVariable("Redis__ConnectionString", redis);
        }

        public HubConnection Player()
        {
            var server = Server;
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, "hub/game"), o =>
                {
                    // WebSockets only, as in production: that's the path that broke.
                    o.Transports = HttpTransportType.WebSockets;
                    o.SkipNegotiation = true;
                    o.WebSocketFactory = async (context, ct) =>
                        await server.CreateWebSocketClient().ConnectAsync(context.Uri, ct);
                })
                .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                .Build();
            return connection;
        }
    }

    /// <summary>A connected player, remembering the latest game state the server pushed to them.</summary>
    private sealed class Client(HubConnection connection) : IAsyncDisposable
    {
        public HubConnection Connection { get; } = connection;
        public volatile GameView? State;

        public static async Task<Client> ConnectAsync(ServerInstance server)
        {
            var client = new Client(server.Player());
            client.Connection.On<GameView>("state", s => client.State = s);
            await client.Connection.StartAsync();
            return client;
        }

        public async Task UntilAsync(string what, Func<GameView, bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (State is { } s && condition(s)) return;
                await Task.Delay(50);
            }

            Assert.Fail($"Timed out waiting for: {what}");
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    [RedisFact]
    public async Task Health_reports_a_connected_redis()
    {
        await using var server = new ServerInstance();
        var response = await server.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"store\":\"redis\"", await response.Content.ReadAsStringAsync());
    }

    [RedisFact]
    public async Task Players_on_different_instances_see_each_other_through_redis()
    {
        await using var a = new ServerInstance();
        await using var b = new ServerInstance();
        await using var alice = await Client.ConnectAsync(a);
        await using var bob = await Client.ConnectAsync(b);

        var joined = await alice.Connection.InvokeAsync<JoinResult>("CreateGame", "Alice");
        await bob.Connection.InvokeAsync<JoinResult>("JoinGame", joined.Code, "Bob");

        // Bob joined on B; the update reaches Alice on A only through the Redis backplane.
        await alice.UntilAsync("Alice sees Bob", s => s.Players.Length == 2);

        await alice.Connection.InvokeAsync("SetMap", "north-norfolk");
        await bob.UntilAsync("Bob sees Alice's map change", s => s.Map.Id == "north-norfolk");
    }

    [RedisFact]
    public async Task A_game_survives_its_server_going_away()
    {
        JoinResult joined;
        await using (var before = new ServerInstance())
        {
            await using var alice = await Client.ConnectAsync(before);
            joined = await alice.Connection.InvokeAsync<JoinResult>("CreateGame", "Alice");
            await alice.Connection.InvokeAsync("SetMap", "the-wallops");
            await alice.Connection.InvokeAsync("SetMaxSpread", 7.0);
        } // the instance shuts down, like an old revision after a deploy

        await using var after = new ServerInstance();
        await using var alice2 = await Client.ConnectAsync(after);
        var rejoined = await alice2.Connection.InvokeAsync<JoinResult>("Rejoin", joined.Code, joined.Token);

        Assert.Equal(joined.PlayerId, rejoined.PlayerId);
        await alice2.UntilAsync("Alice's game came back", s => s.Map.Id == "the-wallops" && s.MaxSpreadKm == 7);
        Assert.True(alice2.State!.Players.Single().IsConnected);
    }

    [RedisFact]
    public async Task The_redis_store_is_the_one_in_use()
    {
        await using var server = new ServerInstance();
        _ = server.CreateClient(); // starts the app
        Assert.Equal("redis", server.Services.GetRequiredService<GameManager>().StoreKind);
    }
}
