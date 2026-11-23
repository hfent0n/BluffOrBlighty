using System.Collections.Concurrent;
using BluffOrBlighty.Api.Game;
using BluffOrBlighty.Api.Hubs;

namespace BluffOrBlighty.Api.Bots;

public sealed class BotTiming
{
    /// <summary>Pauses before acting, so bots feel like players rather than instant machines.</summary>
    public TimeSpan MinThink { get; set; } = TimeSpan.FromSeconds(1.5);

    public TimeSpan MaxThink { get; set; } = TimeSpan.FromSeconds(4);
}

/// <summary>
/// Plays the bots. After any change to a game, <see cref="Kick"/> asks the game what its bots
/// need to do and runs each piece of work once, in the background.
/// </summary>
public sealed class BotDriver(
    GameManager games,
    IPlaceSource places,
    IGameBroadcaster broadcaster,
    BotTiming timing,
    ILogger<BotDriver> logger)
{
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    /// <summary>
    /// Starts any bot work the game (as of this snapshot) is waiting on. Bot moves are saved
    /// through <see cref="GameManager"/>, so they apply to the latest state even if it has moved
    /// on. Returns the tasks (for tests); callers needn't await.
    /// </summary>
    /// <remarks>
    /// Work is de-duplicated per server instance. If two instances briefly overlap (a deploy),
    /// both might act for a bot; that's harmless, as a second submit replaces the first and
    /// stale votes are rejected.
    /// </remarks>
    public IReadOnlyList<Task> Kick(GameSession game)
    {
        var started = new List<Task>();
        foreach (var work in game.PendingBotWork())
        {
            var key = $"{game.Code}:{work.Key}";
            if (!_inFlight.TryAdd(key, 0)) continue;
            started.Add(Task.Run(async () =>
            {
                GameSession? latest = null;
                try
                {
                    latest = await RunAsync(game.Code, work);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Bot work {Key} failed", key);
                }
                finally
                {
                    _inFlight.TryRemove(key, out _);
                }

                latest ??= await games.TryGetAsync(game.Code);
                if (latest is null) return; // game has expired

                // Changes made by this bot may give other bots something to do.
                await broadcaster.BroadcastAsync(latest);
                await Task.WhenAll(Kick(latest));
            }));
        }

        return started;
    }

    /// <summary>Does the work and returns the game as saved, or null if nothing was saved.</summary>
    private async Task<GameSession?> RunAsync(string code, BotWork work)
    {
        var rng = new Random();
        await Think(rng);
        return work switch
        {
            BotSubmitWork s => await SubmitAsync(code, s, rng),
            BotVoteWork v => await VoteAsync(code, v, rng),
            _ => null,
        };
    }

    private async Task<GameSession?> SubmitAsync(string code, BotSubmitWork work, Random rng)
    {
        // "Anywhere" has no area, so the bot picks somewhere it knows has good names.
        // ("Anywhere" means anywhere in the UK, so stick to UK areas.)
        var presets = MapPresets.All.Where(m => m.RadiusKm is not null && m.IsUk).ToList();
        var area = work.Map.RadiusKm is null ? presets[rng.Next(presets.Count)] : work.Map;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (lat, lng) = RandomPointIn(area, rng);
            var found = new List<TilePlace>();
            foreach (var tile in TileMath.BlockAround(lat, lng))
                found.AddRange(await places.PlacesInTileAsync(tile));

            if (BotBrain.ChooseSubmission(found, work.Map, rng, work.MaxSpreadKm,
                    // English-style fakes would stand out abroad, so splice local names there.
                    area.IsUk ? work.Profile.Style : FakeStyle.Splice) is { } submission)
            {
                try
                {
                    return await games.UpdateAsync(code, g => g.Submit(work.BotId, submission));
                }
                catch (GameException e)
                {
                    logger.LogWarning("Bot submission rejected ({Message}); retrying", e.Message);
                }
            }
        }

        logger.LogWarning("Bot {Bot} couldn't find places in {Map}; removing it from game {Code}",
            work.BotId, work.Map.Id, code);
        return await games.UpdateAsync(code, g => g.DropBot(work.BotId));
    }

    private async Task<GameSession?> VoteAsync(string code, BotVoteWork work, Random rng)
    {
        bool[] found;
        try
        {
            found = new bool[work.Options.Length];
            for (var i = 0; i < work.Options.Length; i++)
            {
                var o = work.Options[i];
                var tile = TileMath.Locate(o.Lat, o.Lng).Tile;
                found[i] = BotBrain.IsOnMap(o.Label, o.Lat, o.Lng, await places.PlacesInTileAsync(tile));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Can't check the map: guess, rather than hold up the round.
            logger.LogWarning(e, "Bot couldn't load tiles to check options; guessing");
            found = new bool[work.Options.Length];
            Array.Fill(found, true);
        }

        var choice = BotBrain.ChooseVote(found, work.Profile.Knowledge, rng);
        try
        {
            return await games.UpdateAsync(code, g => g.Vote(work.BotId, choice, work.RoundIndex, work.Leg));
        }
        catch (GameException)
        {
            // The host revealed or moved on while the bot was thinking. Nothing to do.
            return null;
        }
    }

    private Task Think(Random rng)
    {
        var min = timing.MinThink.TotalMilliseconds;
        var max = Math.Max(min, timing.MaxThink.TotalMilliseconds);
        var ms = min + rng.NextDouble() * (max - min);
        return ms <= 0 ? Task.CompletedTask : Task.Delay(TimeSpan.FromMilliseconds(ms));
    }

    private static (double Lat, double Lng) RandomPointIn(MapPreset area, Random rng)
    {
        // Stay away from the edge, so there's room for 3 places inside the area.
        var r = (area.RadiusKm ?? 0) * 0.6 * Math.Sqrt(rng.NextDouble());
        var theta = rng.NextDouble() * 2 * Math.PI;
        var lat = area.Lat + r / 110.574 * Math.Sin(theta);
        var lng = area.Lng + r / (111.32 * Math.Cos(area.Lat * Math.PI / 180)) * Math.Cos(theta);
        return (lat, lng);
    }
}
