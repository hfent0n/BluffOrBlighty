using System.Collections.Concurrent;
using StackExchange.Redis;

namespace BluffOrBlighty.Api.Game;

/// <summary>A game as stored: its version and its JSON (<see cref="GameState.ToJson"/>).</summary>
public sealed record StoredGame(long Version, string Json);

/// <summary>
/// Where game state lives between server restarts. Saves are optimistic: a save made from an
/// out-of-date copy is rejected, and <see cref="GameManager"/> reloads and retries.
/// </summary>
/// <remarks>
/// <see cref="GameManager"/> keeps games in memory and only reads the store when it doesn't have
/// a game (after a restart, or when another instance has saved since), so most moves cost
/// a single save.
/// </remarks>
public interface IGameStore
{
    /// <summary>"memory" or "redis", for the health check.</summary>
    string Kind { get; }

    /// <summary>Stores a new game. False if the code is already taken.</summary>
    Task<bool> TryCreateAsync(string code, long version, string json);

    Task<StoredGame?> GetAsync(string code);

    /// <summary>Saves only if the stored copy is still at <paramref name="expectedVersion"/>.</summary>
    Task<bool> TrySaveAsync(string code, long expectedVersion, long newVersion, string json);
}

/// <summary>Games kept in this process. For local development and tests; lost on restart.</summary>
public sealed class InMemoryGameStore(TimeProvider time) : IGameStore
{
    private readonly ConcurrentDictionary<string, (StoredGame Game, DateTimeOffset Saved)> _games = new();
    private readonly object _lock = new();

    public string Kind => "memory";

    /// <summary>Calls made, so tests can check how often the store is touched.</summary>
    public int Reads { get; private set; }
    public int Writes { get; private set; }

    public Task<bool> TryCreateAsync(string code, long version, string json)
    {
        lock (_lock) Writes++;
        return Task.FromResult(_games.TryAdd(code, (new StoredGame(version, json), time.GetUtcNow())));
    }

    public Task<StoredGame?> GetAsync(string code)
    {
        lock (_lock) Reads++;
        return Task.FromResult(_games.TryGetValue(code, out var g) ? g.Game : null);
    }

    public Task<bool> TrySaveAsync(string code, long expectedVersion, long newVersion, string json)
    {
        lock (_lock)
        {
            Writes++;
            if (!_games.TryGetValue(code, out var current) || current.Game.Version != expectedVersion)
                return Task.FromResult(false);
            _games[code] = (new StoredGame(newVersion, json), time.GetUtcNow());
            return Task.FromResult(true);
        }
    }

    public int RemoveIdle(TimeSpan maxIdle)
    {
        var cutoff = time.GetUtcNow() - maxIdle;
        var removed = 0;
        foreach (var (code, g) in _games)
        {
            if (g.Saved < cutoff && _games.TryRemove(code, out _)) removed++;
        }

        return removed;
    }
}

/// <summary>
/// Games in Redis, so they outlive any one server instance (deploys, restarts).
/// </summary>
/// <remarks>
/// Built to use as few Redis commands as possible, since hosted Redis (e.g. Upstash) bills
/// per command, counting each call inside a script too. Each game is one string at
/// <c>bluff:game:{code}</c> holding <c>{version}|{json}</c>, expiring after <see cref="Idle"/>
/// without a save:
/// <list type="bullet">
/// <item>Create: <c>SET … NX EX</c>, 1 command.</item>
/// <item>Load: <c>GET</c>, 1 command (only when this instance doesn't have the game in memory).</item>
/// <item>Save: a script that reads just the version prefix (<c>GETRANGE</c>) and writes with
/// <c>SET … EX</c>, 3 commands (the call plus two inside it).</item>
/// </list>
/// </remarks>
public sealed class RedisGameStore(IConnectionMultiplexer redis) : IGameStore
{
    public static readonly TimeSpan Idle = TimeSpan.FromHours(6);

    // Runs atomically in Redis, so the version check and the write can't interleave with
    // another save. GETRANGE reads only the start of the value, not the whole game.
    private const string SaveScript = """
        local head = redis.call('GETRANGE', KEYS[1], 0, 24)
        if string.match(head, '^(%d+)|') ~= ARGV[1] then return 0 end
        redis.call('SET', KEYS[1], ARGV[2], 'EX', ARGV[3])
        return 1
        """;

    public string Kind => "redis";

    private static RedisKey Key(string code) => $"bluff:game:{code}";

    private static string Value(long version, string json) => $"{version}|{json}";

    public Task<bool> TryCreateAsync(string code, long version, string json) =>
        redis.GetDatabase().StringSetAsync(Key(code), Value(version, json), Idle, When.NotExists);

    public async Task<StoredGame?> GetAsync(string code)
    {
        string? value = await redis.GetDatabase().StringGetAsync(Key(code));
        if (value is null) return null;
        var bar = value.IndexOf('|');
        return new StoredGame(long.Parse(value.AsSpan(0, bar)), value[(bar + 1)..]);
    }

    public async Task<bool> TrySaveAsync(string code, long expectedVersion, long newVersion, string json)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(SaveScript,
            [Key(code)], [expectedVersion.ToString(), Value(newVersion, json), (long)Idle.TotalSeconds]);
        return (long)result == 1;
    }
}
