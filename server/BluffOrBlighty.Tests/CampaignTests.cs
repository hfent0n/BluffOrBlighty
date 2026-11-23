using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Game;

namespace BluffOrBlighty.Tests;

public class CampaignTests
{
    private static readonly TilePlace[] Norfolk =
    [
        new("Great Snoring", "village", 52.87356, 0.89238),
        new("Little Snoring", "village", 52.85309, 0.90680),
        new("Thursford Green", "hamlet", 52.87089, 0.93723),
        new("Thursford", "village", 52.86504, 0.94847),
        new("Barney", "village", 52.85348, 0.96203),
        new("Kettlestone", "village", 52.84400, 0.90400),
        new("Hindringham", "village", 52.88900, 0.94100),
        new("Gunthorpe", "village", 52.87164, 0.99160),
        new("Saxthorpe", "village", 52.82953, 1.13966),
        new("Hempton", "village", 52.82675, 0.83865),
    ];

    private static (GameSession Game, string Me, string Bot) Match(string levelId = "newsletter")
    {
        var game = new GameSession("TEST", new Random(3), TimeProvider.System);
        var me = game.AddPlayer("Alice", "c1").PlayerId;
        game.StartCampaign(me, CampaignLevels.Get(levelId));
        var bot = game.ViewFor(me).Players.Single(p => p.IsBot).Id;
        return (game, me, bot);
    }

    private static SubmissionRequest Picks(int seed) =>
        BotBrain.ChooseSubmission(Norfolk, MapPresets.Get("north-norfolk"), new Random(seed))!;

    /// <summary>Plays one leg. The human always spots the fake; the bot votes for option 0 (right or wrong).</summary>
    private static void PlayLeg(GameSession game, string me, string bot, int seed, bool botFindsFake)
    {
        game.Submit(me, Picks(seed));
        game.Submit(bot, Picks(seed + 100));
        Assert.Equal(GamePhase.Guessing, game.Phase);

        for (var r = 0; r < 2; r++)
        {
            var round = game.ViewFor(me).Round!;
            var fake = FakeIndex(round);
            if (round.AuthorId == me)
                game.Vote(bot, botFindsFake ? fake : (fake + 1) % 3);
            else
                game.Vote(me, fake);
            game.Advance(me);
        }
    }

    // Test helper: find the fake from the generator's rule (the fake is the only name not in Norfolk).
    private static int FakeIndex(RoundView round) =>
        Array.FindIndex(round.Options, o => !Norfolk.Any(p => p.Name == o.Label));

    [Fact]
    public void Starting_a_level_adds_its_opponent_and_map_and_starts()
    {
        var (game, me, _) = Match();
        var view = game.ViewFor(me);
        Assert.Equal(GamePhase.Submitting, view.Phase);
        Assert.Equal("north-norfolk", view.Map.Id);
        Assert.Equal(1, view.Leg);
        Assert.Equal(3, view.Legs);
        Assert.Equal("Doris", view.Players.Single(p => p.IsBot).Name);
        Assert.Equal("newsletter", view.Campaign!.LevelId);
        Assert.Null(view.Campaign.Result);
    }

    [Fact]
    public void Nobody_else_can_join_and_map_is_fixed()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var me = game.AddPlayer("Alice", "c1").PlayerId;
        game.StartCampaign(me, CampaignLevels.Get("newsletter"));
        Assert.Throws<GameException>(() => game.AddPlayer("Bob", "c2"));
        Assert.Throws<GameException>(() => game.SetMap(me, "anywhere"));
        Assert.Throws<GameException>(() => game.BackToLobby(me));
    }

    [Fact]
    public void Match_runs_all_legs_then_finishes_with_a_result()
    {
        var (game, me, bot) = Match();
        PlayLeg(game, me, bot, 1, botFindsFake: false);
        Assert.Equal(GamePhase.Submitting, game.Phase);
        Assert.Equal(2, game.ViewFor(me).Leg);
        Assert.Contains(game.PendingBotWork(), w => w is BotSubmitWork { Leg: 2 });

        PlayLeg(game, me, bot, 2, botFindsFake: false);
        PlayLeg(game, me, bot, 3, botFindsFake: false);
        Assert.Equal(GamePhase.Finished, game.Phase);

        var result = game.ViewFor(me).Campaign!.Result!;
        Assert.True(result.Won);
        Assert.Equal(6, result.YourScore); // spotted 3 + fooled 3
        Assert.Equal(0, result.OpponentScore);
        Assert.Equal(3, result.FakesSpotted);
        Assert.Equal(3, result.TimesFooled);
        Assert.Equal(3, result.Stars);
    }

    [Fact]
    public void Losing_gives_no_stars()
    {
        var (game, me, bot) = Match();
        // Human never votes correctly: vote for a real option on the bot's rounds.
        for (var leg = 0; leg < 3; leg++)
        {
            game.Submit(me, Picks(leg));
            game.Submit(bot, Picks(leg + 100));
            for (var r = 0; r < 2; r++)
            {
                var round = game.ViewFor(me).Round!;
                var fake = FakeIndex(round);
                if (round.AuthorId == me) game.Vote(bot, fake);
                else game.Vote(me, (fake + 1) % 3);
                game.Advance(me);
            }
        }

        var result = game.ViewFor(me).Campaign!.Result!;
        Assert.False(result.Won);
        Assert.Equal(0, result.Stars);
    }

    [Theory]
    [InlineData(true, 1, 2, 3, 1)]  // narrow win, missed one fake
    [InlineData(true, 2, 2, 3, 2)]  // comfortable win
    [InlineData(true, 1, 3, 3, 2)]  // narrow win, spotted every fake
    [InlineData(true, 4, 3, 3, 3)]
    [InlineData(false, 0, 3, 3, 0)] // a draw is not a win
    public void Stars_follow_the_rules(bool won, int margin, int spotted, int legs, int expected) =>
        Assert.Equal(expected, CampaignLevels.Stars(won, margin, spotted, legs));

    [Fact]
    public void Levels_get_harder_and_reference_real_maps()
    {
        var levels = CampaignLevels.All;
        Assert.Equal(Enumerable.Range(1, levels.Count), levels.Select(l => l.Number));
        Assert.All(levels, l => Assert.NotNull(MapPresets.Get(l.MapId)));
        for (var i = 1; i < levels.Count; i++)
            Assert.True(levels[i].Bot.Knowledge > levels[i - 1].Bot.Knowledge);
    }

    [Theory]
    [InlineData(FakeStyle.Obvious)]
    [InlineData(FakeStyle.Local)]
    [InlineData(FakeStyle.Crafty)]
    public void Every_fake_style_avoids_real_names(FakeStyle style)
    {
        var real = Norfolk.Select(p => p.Name).ToArray();
        for (var seed = 0; seed < 200; seed++)
        {
            var name = FakeNames.Generate(real, real, new Random(seed), style);
            Assert.DoesNotContain(real, r => r.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.InRange(name.Length, 4, 30);
        }
    }

    [Fact]
    public void Crafty_fakes_reuse_local_endings()
    {
        // Norfolk names here end in -ing, -ford, -ney, -tone, -ham, -thorpe, -ton.
        var real = Norfolk.Select(p => p.Name).ToArray();
        var localEndings = new[] { "ing", "ford", "ham", "thorpe", "ton", "ey" };
        for (var seed = 0; seed < 50; seed++)
        {
            // The made-up word may be followed by a local suffix word, e.g. "Snorford Green".
            var name = FakeNames.Generate(real, real, new Random(seed), FakeStyle.Crafty);
            Assert.Contains(name.Split(' '), w => localEndings.Any(e => w.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Campaign_endpoint_views_include_map_names()
    {
        var views = CampaignLevels.Views().ToList();
        Assert.Equal("The Snorings", views[0].MapName);
        Assert.InRange(views[0].Difficulty, 1, 2);
        Assert.Equal(5, views[^1].Difficulty);
    }
}
