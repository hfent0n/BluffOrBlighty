using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// Loads, changes and saves games through an <see cref="IGameStore"/>.
/// </summary>
/// <remarks>
/// To keep store traffic (and the Redis bill) low, each instance keeps the games it's serving
/// in memory and only reads the store when it doesn't have one: after a restart or deploy, or
/// when another instance has saved since. Changes to one game run one at a time within an
/// instance, so they never conflict with each other. Every change is still saved with an
/// optimistic version check, so if another instance got there first (old and new versions
/// overlapping during a deploy), it reloads and retries instead of overwriting.
/// </remarks>
public sealed class GameManager(IGameStore store, TimeProvider time)
{
    // No 0/O or 1/I/L, so codes are easy to read out loud.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int CodeLength = 4;
    private const int MaxAttempts = 20;

    /// <summary>This instance's copy of a game, plus a gate so its changes run one at a time.</summary>
    private sealed class Cached
    {
        public readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>The latest JSON this instance knows of, or null if it must reload.</summary>
        public StoredGame? Game;

        public DateTimeOffset LastUsed;
    }

    private readonly ConcurrentDictionary<string, Cached> _cache = new();

    public string StoreKind => store.Kind;

    /// <summary>Creates a game, runs <paramref name="setup"/> on it (e.g. adding the host), and stores it.</summary>
    public async Task<(GameSession Game, T Result)> CreateAsync<T>(Func<GameSession, T> setup)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = new string(Enumerable.Range(0, CodeLength)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
                .ToArray());
            var game = new GameSession(code, Random.Shared, time);
            var result = setup(game);
            var json = game.State.ToJson();
            if (!await store.TryCreateAsync(code, game.State.Version, json)) continue;

            Remember(code, new StoredGame(game.State.Version, json));
            return (game, result);
        }

        throw new GameException("Couldn't create a game right now. Try again.");
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the latest copy of the game and saves it. A
    /// <see cref="GameException"/> from the change is passed on and nothing is saved.
    /// </summary>
    public async Task<(GameSession Game, T Result)> UpdateAsync<T>(string code, Func<GameSession, T> change)
    {
        code = Normalise(code);
        var cached = _cache.GetOrAdd(code, _ => new Cached());
        await cached.Gate.WaitAsync();
        try
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                cached.LastUsed = time.GetUtcNow();
                if (cached.Game is null)
                {
                    cached.Game = await store.GetAsync(code);
                    if (cached.Game is null)
                    {
                        _cache.TryRemove(code, out _);
                        throw new GameException("No game with that code.");
                    }
                }

                // Work on a fresh copy, so a change that's rejected part-way leaves the cache untouched.
                var state = GameState.FromJson(cached.Game.Json);
                var expected = state.Version;
                var game = new GameSession(state, Random.Shared, time);
                var result = change(game);

                state.Version = expected + 1;
                var json = state.ToJson();
                if (await store.TrySaveAsync(code, expected, state.Version, json))
                {
                    cached.Game = new StoredGame(state.Version, json);
                    return (game, result);
                }

                // Another instance saved first. Reload its version and apply the change to that.
                cached.Game = null;
                await Task.Delay(Random.Shared.Next(5, 25 + attempt * 10));
            }

            throw new GameException("That game is busy. Try again.");
        }
        finally
        {
            cached.Gate.Release();
        }
    }

    public async Task<GameSession> UpdateAsync(string code, Action<GameSession> change) =>
        (await UpdateAsync(code, g => { change(g); return 0; })).Game;

    /// <summary>A read-only snapshot, or null if there's no such game. Uses this instance's copy when it has one.</summary>
    public async Task<GameSession?> TryGetAsync(string code)
    {
        code = Normalise(code);
        var game = _cache.TryGetValue(code, out var cached) && cached.Game is { } known
            ? known
            : await store.GetAsync(code);
        if (game is null) return null;

        Remember(code, game);
        return new GameSession(GameState.FromJson(game.Json), Random.Shared, time);
    }

    /// <summary>Forgets games this instance hasn't touched for a while. They reload from the store if needed.</summary>
    public int EvictIdle(TimeSpan maxIdle)
    {
        var cutoff = time.GetUtcNow() - maxIdle;
        var removed = 0;
        foreach (var (code, cached) in _cache)
        {
            if (cached.LastUsed < cutoff && cached.Gate.CurrentCount == 1 && _cache.TryRemove(code, out _)) removed++;
        }

        return removed;
    }

    private void Remember(string code, StoredGame game)
    {
        var cached = _cache.GetOrAdd(code, _ => new Cached());
        // Only fill in or move forward, never back to an older version.
        if (cached.Game is null || cached.Game.Version < game.Version) cached.Game = game;
        cached.LastUsed = time.GetUtcNow();
    }

    private static string Normalise(string code) => (code ?? "").Trim().ToUpperInvariant();
}

/// <summary>
/// Forgets idle games from this instance's memory, and clears them from the in-memory store
/// when that's in use. (Redis expires games by itself.)
/// </summary>
public sealed class GameCleanupService(GameManager games, IGameStore store, ILogger<GameCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan MaxIdle = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var evicted = games.EvictIdle(MaxIdle);
            var removed = store is InMemoryGameStore memory ? memory.RemoveIdle(MaxIdle) : 0;
            if (evicted + removed > 0) logger.LogInformation("Cleared {Count} idle games", evicted + removed);
        }
    }
}
