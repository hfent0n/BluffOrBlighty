using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// In-memory store of live games. When accounts and history arrive, finished
/// games can be written to Postgres from here; live state can stay in memory.
/// </summary>
public sealed class GameManager(TimeProvider time)
{
    // No 0/O or 1/I/L, so codes are easy to read out loud.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int CodeLength = 4;

    private readonly ConcurrentDictionary<string, GameSession> _games = new();

    public GameSession Create()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = new string(Enumerable.Range(0, CodeLength)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
                .ToArray());
            var game = new GameSession(code, Random.Shared, time);
            if (_games.TryAdd(code, game)) return game;
        }

        throw new GameException("Couldn't create a game right now. Try again.");
    }

    public GameSession Get(string code) =>
        _games.TryGetValue(Normalise(code), out var game)
            ? game
            : throw new GameException("No game with that code.");

    public GameSession? TryGet(string code) => _games.GetValueOrDefault(Normalise(code));

    public int RemoveIdle(TimeSpan maxIdle)
    {
        var cutoff = time.GetUtcNow() - maxIdle;
        var removed = 0;
        foreach (var (code, game) in _games)
        {
            if (game.LastActivity < cutoff && _games.TryRemove(code, out _)) removed++;
        }

        return removed;
    }

    private static string Normalise(string code) => (code ?? "").Trim().ToUpperInvariant();
}

public sealed class GameCleanupService(GameManager games, ILogger<GameCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxIdle = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = games.RemoveIdle(MaxIdle);
            if (removed > 0) logger.LogInformation("Removed {Count} idle games", removed);
        }
    }
}
