using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Game;
using BluffOrBlighty.Api.Hubs;
using Microsoft.Extensions.Logging.Abstractions;

namespace BluffOrBlighty.Tests;

public class BotTests
{
    // Real North Norfolk labels (as they appear in the zoom-12 tiles).
    private static readonly TilePlace[] Norfolk =
    [
        new("Great Snoring", "village", 52.87356, 0.89238),
        new("Little Snoring", "village", 52.85309, 0.90680),
        new("Thursford Green", "hamlet", 52.87089, 0.93723),
        new("Thursford", "village", 52.86504, 0.94847),
        new("Barney", "village", 52.85348, 0.96203),
        new("Hindringham", "village", 52.88900, 0.94100),
        new("Fakenham", "town", 52.82992, 0.84775),
        new("Kettlestone", "village", 52.84400, 0.90400),
    ];

    private static readonly MapPreset NorthNorfolk = MapPresets.Get("north-norfolk");

    [Fact]
    public void Bot_submission_passes_the_game_rules()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var rng = new Random(seed);
            var s = BotBrain.ChooseSubmission(Norfolk, NorthNorfolk, GameSession.MaxSpreadKm, rng);
            Assert.NotNull(s);

            var game = new GameSession("TEST", new Random(seed), TimeProvider.System);
            var host = game.AddPlayer("Alice", "c1");
            game.SetMap(host.PlayerId, NorthNorfolk.Id);
            var bot = game.AddBot(host.PlayerId, BotLevel.Normal);
            game.Start(host.PlayerId);
            game.Submit(bot, s!); // throws if it breaks any rule

            Assert.NotEqual("town", Norfolk.Single(p => p.Name == s!.Renamed.Name).Class);
            Assert.DoesNotContain(Norfolk, p => p.Name.Equals(s!.FakeName, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Bot_returns_null_when_area_has_too_few_places()
    {
        Assert.Null(BotBrain.ChooseSubmission(Norfolk.Take(2).ToArray(), NorthNorfolk, 15, new Random(1)));
    }

    [Fact]
    public void Fake_names_avoid_real_names_and_look_like_names()
    {
        var real = Norfolk.Select(p => p.Name).ToArray();
        for (var seed = 0; seed < 200; seed++)
        {
            var name = FakeNames.Generate(real, real, new Random(seed));
            Assert.DoesNotContain(real, r => r.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.InRange(name.Length, 4, 30);
            Assert.True(char.IsUpper(name[0]), name);
        }
    }

    [Theory]
    [InlineData(BotLevel.Easy, 0.30, 0.60)]
    [InlineData(BotLevel.Normal, 0.60, 0.80)]
    [InlineData(BotLevel.Hard, 0.85, 0.97)]
    public void Harder_bots_spot_the_fake_more_often(BotLevel level, double min, double max)
    {
        var rng = new Random(42);
        bool[] found = [true, false, true]; // option 1 is the fake
        var knowledge = BotProfile.For(level).Knowledge;
        var correct = Enumerable.Range(0, 4000).Count(_ => BotBrain.ChooseVote(found, knowledge, rng) == 1);
        Assert.InRange(correct / 4000.0, min, max);
    }

    [Fact]
    public void Is_on_map_matches_name_near_point_only()
    {
        Assert.True(BotBrain.IsOnMap("great snoring", 52.8736, 0.8924, Norfolk));
        Assert.False(BotBrain.IsOnMap("Great Snoring", 52.80, 0.80, Norfolk)); // right name, wrong place
        Assert.False(BotBrain.IsOnMap("Muddle Snoring", 52.8531, 0.9068, Norfolk));
    }

    [Fact]
    public void Only_host_adds_bots_and_only_in_lobby()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var host = game.AddPlayer("Alice", "c1");
        var guest = game.AddPlayer("Bob", "c2");
        Assert.Throws<GameException>(() => game.AddBot(guest.PlayerId, BotLevel.Easy));
        var bot = game.AddBot(host.PlayerId, BotLevel.Easy);
        Assert.Contains(game.ViewFor(host.PlayerId).Players, p => p.Id == bot && p.IsBot && p.BotLevel == BotLevel.Easy);

        game.Start(host.PlayerId);
        Assert.Throws<GameException>(() => game.AddBot(host.PlayerId, BotLevel.Easy));
        Assert.Throws<GameException>(() => game.RemoveBot(host.PlayerId, bot));
    }

    [Fact]
    public void Pending_work_asks_bot_to_submit_then_vote_but_not_on_its_own_round()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var host = game.AddPlayer("Alice", "c1");
        game.SetMap(host.PlayerId, NorthNorfolk.Id);
        var bot = game.AddBot(host.PlayerId, BotLevel.Hard);
        Assert.Empty(game.PendingBotWork());

        game.Start(host.PlayerId);
        Assert.IsType<BotSubmitWork>(Assert.Single(game.PendingBotWork()));

        game.Submit(host.PlayerId, BotBrain.ChooseSubmission(Norfolk, NorthNorfolk, 15, new Random(1))!);
        game.Submit(bot, BotBrain.ChooseSubmission(Norfolk, NorthNorfolk, 15, new Random(2))!);
        Assert.Equal(GamePhase.Guessing, game.Phase);

        // Round 1 is Alice's: the bot should vote.
        var vote = Assert.IsType<BotVoteWork>(Assert.Single(game.PendingBotWork()));
        game.Vote(bot, 0, vote.RoundIndex);
        game.Advance(host.PlayerId); // next round: the bot's own
        Assert.Empty(game.PendingBotWork());
    }

    [Fact]
    public async Task Driver_plays_a_full_game_against_a_human()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var host = game.AddPlayer("Alice", "c1");
        game.SetMap(host.PlayerId, NorthNorfolk.Id);
        game.AddBot(host.PlayerId, BotLevel.Hard);
        game.Start(host.PlayerId);

        var driver = new BotDriver(new FakePlaces(Norfolk), new NoBroadcast(),
            new BotTiming { MinThink = TimeSpan.Zero, MaxThink = TimeSpan.Zero }, NullLogger<BotDriver>.Instance);

        await Task.WhenAll(driver.Kick(game)); // bot submits
        game.Submit(host.PlayerId, BotBrain.ChooseSubmission(Norfolk, NorthNorfolk, 15, new Random(7))!);
        Assert.Equal(GamePhase.Guessing, game.Phase);

        await Task.WhenAll(driver.Kick(game)); // bot votes on Alice's round
        Assert.NotNull(game.ViewFor(host.PlayerId).Round!.Reveal); // all voters done -> revealed
    }

    private sealed class FakePlaces(IReadOnlyList<TilePlace> all) : IPlaceSource
    {
        // Every tile "contains" the whole test set; fine for the bot's purposes.
        public Task<IReadOnlyList<TilePlace>> PlacesInTileAsync(TileId tile, CancellationToken ct = default) =>
            Task.FromResult(all);
    }

    private sealed class NoBroadcast : IGameBroadcaster
    {
        public Task BroadcastAsync(GameSession game) => Task.CompletedTask;
    }
}
