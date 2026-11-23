using System.Security.Cryptography;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// Loads, changes and saves games through an <see cref="IGameStore"/>. Each change runs
/// against a fresh copy of the game and is saved only if nobody else saved in the meantime;
/// otherwise it's retried. That keeps games correct when more than one server instance
/// is running, for example while a deploy hands over from the old version to the new one.
/// </summary>
public sealed class GameManager(IGameStore store, TimeProvider time)
{
    // No 0/O or 1/I/L, so codes are easy to read out loud.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int CodeLength = 4;
    private const int MaxAttempts = 20;

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
            if (await store.TryCreateAsync(game.State)) return (game, result);
        }

        throw new GameException("Couldn't create a game right now. Try again.");
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the latest copy of the game and saves it, retrying if
    /// another change got there first. A <see cref="GameException"/> from the change is passed on
    /// and nothing is saved.
    /// </summary>
    public async Task<(GameSession Game, T Result)> UpdateAsync<T>(string code, Func<GameSession, T> change)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var state = await store.GetAsync(Normalise(code)) ?? throw new GameException("No game with that code.");
            var expected = state.Version;
            var game = new GameSession(state, Random.Shared, time);
            var result = change(game);

            state.Version = expected + 1;
            if (await store.TrySaveAsync(state, expected)) return (game, result);

            // Someone else saved first. Back off briefly and try again on their version.
            await Task.Delay(Random.Shared.Next(5, 25 + attempt * 10));
        }

        throw new GameException("That game is busy. Try again.");
    }

    public async Task<GameSession> UpdateAsync(string code, Action<GameSession> change) =>
        (await UpdateAsync(code, g => { change(g); return 0; })).Game;

    /// <summary>A read-only snapshot, or null if there's no such game.</summary>
    public async Task<GameSession?> TryGetAsync(string code) =>
        await store.GetAsync(Normalise(code)) is { } state ? new GameSession(state, Random.Shared, time) : null;

    private static string Normalise(string code) => (code ?? "").Trim().ToUpperInvariant();
}

/// <summary>Clears out idle games from the in-memory store. (Redis expires them by itself.)</summary>
public sealed class GameCleanupService(InMemoryGameStore store, ILogger<GameCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxIdle = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = store.RemoveIdle(MaxIdle);
            if (removed > 0) logger.LogInformation("Removed {Count} idle games", removed);
        }
    }
}
