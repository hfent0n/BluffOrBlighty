using System.Collections.Concurrent;
using StackExchange.Redis;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// Where game state lives between requests. Saves are optimistic: a save made from an
/// out-of-date copy is rejected, and <see cref="GameManager"/> retries with fresh state.
/// </summary>
public interface IGameStore
{
    /// <summary>"memory" or "redis", for the health check.</summary>
    string Kind { get; }

    /// <summary>Stores a new game. False if the code is already taken.</summary>
    Task<bool> TryCreateAsync(GameState state);

    Task<GameState?> GetAsync(string code);

    /// <summary>Saves <paramref name="state"/> only if the stored copy is still at <paramref name="expectedVersion"/>.</summary>
    Task<bool> TrySaveAsync(GameState state, long expectedVersion);
}

/// <summary>Games kept in this process. For local development and tests; lost on restart.</summary>
public sealed class InMemoryGameStore(TimeProvider time) : IGameStore
{
    // Stored as JSON, like Redis, so code that works here works there too.
    private readonly ConcurrentDictionary<string, (long Version, string Json, DateTimeOffset Saved)> _games = new();
    private readonly object _lock = new();

    public string Kind => "memory";

    public Task<bool> TryCreateAsync(GameState state) =>
        Task.FromResult(_games.TryAdd(state.Code, (state.Version, state.ToJson(), time.GetUtcNow())));

    public Task<GameState?> GetAsync(string code) =>
        Task.FromResult(_games.TryGetValue(code, out var g) ? GameState.FromJson(g.Json) : null);

    public Task<bool> TrySaveAsync(GameState state, long expectedVersion)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(state.Code, out var current) || current.Version != expectedVersion)
                return Task.FromResult(false);
            _games[state.Code] = (state.Version, state.ToJson(), time.GetUtcNow());
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
/// Games in Redis, so they outlive any one server instance (deploys, restarts, scaling).
/// Each game is a hash at <c>game:{code}</c> with its version and JSON, expiring after
/// <see cref="Idle"/> without a save.
/// </summary>
public sealed class RedisGameStore(IConnectionMultiplexer redis) : IGameStore
{
    public static readonly TimeSpan Idle = TimeSpan.FromHours(6);

    // Lua runs atomically in Redis, so check-then-write can't interleave with another save.
    private const string CreateScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
        redis.call('HSET', KEYS[1], 'v', ARGV[1], 's', ARGV[2])
        redis.call('EXPIRE', KEYS[1], ARGV[3])
        return 1
        """;

    private const string SaveScript = """
        if redis.call('HGET', KEYS[1], 'v') ~= ARGV[1] then return 0 end
        redis.call('HSET', KEYS[1], 'v', ARGV[2], 's', ARGV[3])
        redis.call('EXPIRE', KEYS[1], ARGV[4])
        return 1
        """;

    public string Kind => "redis";

    private static RedisKey Key(string code) => $"game:{code}";

    private static long Ttl => (long)Idle.TotalSeconds;

    public async Task<bool> TryCreateAsync(GameState state)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(CreateScript,
            [Key(state.Code)], [state.Version, state.ToJson(), Ttl]);
        return (long)result == 1;
    }

    public async Task<GameState?> GetAsync(string code)
    {
        var json = await redis.GetDatabase().HashGetAsync(Key(code), "s");
        return json.IsNullOrEmpty ? null : GameState.FromJson(json!);
    }

    public async Task<bool> TrySaveAsync(GameState state, long expectedVersion)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(SaveScript,
            [Key(state.Code)], [expectedVersion.ToString(), state.Version, state.ToJson(), Ttl]);
        return (long)result == 1;
    }
}
