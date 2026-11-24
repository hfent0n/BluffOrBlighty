using System.Text.Json;
using BluffOrBlighty.Api.Accounts;
using BluffOrBlighty.Api.Data;
using BluffOrBlighty.Api.Game;

namespace BluffOrBlighty.Tests;

public class AccountTests
{
    private static readonly PlacePick GreatSnoring = new("Great Snoring", 52.8706, 0.8836);
    private static readonly PlacePick LittleSnoring = new("Little Snoring", 52.8613, 0.9093);
    private static readonly PlacePick Thursford = new("Thursford", 52.8598, 0.9449);
    private static SubmissionRequest Norfolk(string fake) => new([GreatSnoring, LittleSnoring], Thursford, fake);

    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Importing_progress_keeps_the_best_of_each_and_twice_changes_nothing()
    {
        var row = new CampaignProgress { LevelId = "newsletter", Stars = 2, Wins = 1, Plays = 4, BestMargin = 1, LastPlayedAt = T0 };
        var fromBrowser = new LevelProgressDto(Stars: 3, Wins: 1, Plays: 2, BestMargin: 4, LastPlayedAt: T0.AddDays(1));

        ProgressMerge.Into(row, fromBrowser);
        ProgressMerge.Into(row, fromBrowser);

        Assert.Equal(3, row.Stars);
        Assert.Equal(1, row.Wins);
        Assert.Equal(4, row.Plays);
        Assert.Equal(4, row.BestMargin);
        Assert.Equal(T0.AddDays(1), row.LastPlayedAt);
    }

    [Fact]
    public void Imported_values_are_kept_in_range()
    {
        var row = new CampaignProgress { LevelId = "newsletter" };
        ProgressMerge.Into(row, new LevelProgressDto(Stars: 99, Wins: -5, Plays: -1, BestMargin: 0, LastPlayedAt: T0));
        Assert.Equal(3, row.Stars);
        Assert.Equal(0, row.Wins);
        Assert.Equal(0, row.Plays);
    }

    [Fact]
    public void Recording_a_match_counts_a_play_and_keeps_best_stars()
    {
        var row = new CampaignProgress { LevelId = "newsletter", Stars = 3, Wins = 1, Plays = 1, BestMargin = 4 };
        ProgressMerge.Record(row, new CampaignResult(Won: true, Stars: 1, YourScore: 3, OpponentScore: 2,
            FakesSpotted: 2, TimesFooled: 1), T0);
        Assert.Equal((3, 2, 2, 4), (row.Stars, row.Wins, row.Plays, row.BestMargin));

        ProgressMerge.Record(row, new CampaignResult(false, 0, 1, 4, 1, 0), T0);
        Assert.Equal((3, 2, 3), (row.Stars, row.Wins, row.Plays));
    }

    /// <summary>Alice (signed in) and Bob (anonymous) play; Bob spots Alice's fake, Alice misses Bob's.</summary>
    private static GameSession FinishedGame(bool aliceSignedIn = true)
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c1", aliceSignedIn ? "user-alice" : null);
        var bob = game.AddPlayer("Bob", "c2");
        game.SetMap(alice.PlayerId, "north-norfolk");
        game.Start(alice.PlayerId);
        game.Submit(alice.PlayerId, Norfolk("Muddle Snoring"));
        game.Submit(bob.PlayerId, Norfolk("Snorebury"));
        for (var r = 0; r < 2; r++)
        {
            var round = game.ViewFor(alice.PlayerId).Round!;
            var fake = Array.FindIndex(round.Options, o => o.Label is "Muddle Snoring" or "Snorebury");
            if (round.AuthorId == alice.PlayerId) game.Vote(bob.PlayerId, fake);
            else game.Vote(alice.PlayerId, (fake + 1) % 3);
            game.Advance(alice.PlayerId);
        }

        Assert.Equal(GamePhase.Finished, game.Phase);
        return game;
    }

    [Fact]
    public void Results_are_claimed_once_and_only_for_signed_in_players()
    {
        var game = FinishedGame();
        var results = game.ClaimResults();

        var alice = Assert.Single(results!);
        Assert.Equal("user-alice", alice.UserId);
        Assert.Equal("north-norfolk", alice.MapId);
        Assert.Equal(2, alice.Players);
        Assert.Equal(0, alice.Score);   // missed Bob's fake, didn't fool Bob
        Assert.Equal(2, alice.Rank);    // Bob scored 2 (spotted + fooled)
        Assert.False(alice.Won);

        Assert.Null(game.ClaimResults()); // already claimed
    }

    [Fact]
    public void Nothing_to_claim_before_the_end_or_with_no_signed_in_players()
    {
        var unfinished = new GameSession("TEST", new Random(1), TimeProvider.System);
        unfinished.AddPlayer("Alice", "c1", "user-alice");
        Assert.Null(unfinished.ClaimResults());

        Assert.Empty(FinishedGame(aliceSignedIn: false).ClaimResults()!);
    }

    [Fact]
    public void Playing_again_allows_the_next_game_to_be_saved()
    {
        var game = FinishedGame();
        Assert.NotNull(game.ClaimResults());
        game.BackToLobby(game.State.HostId);
        Assert.False(game.State.ResultsRecorded);
    }

    [Fact]
    public void Signing_in_mid_game_links_the_player_on_rejoin()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c1");
        game.Reconnect(alice.Token, "c2", "user-alice");
        Assert.Equal("user-alice", game.State.Players.Single().UserId);

        game.Reconnect(alice.Token, "c3"); // reconnecting signed out doesn't unlink
        Assert.Equal("user-alice", game.State.Players.Single().UserId);
    }

    [Fact]
    public void Account_ids_and_tokens_never_reach_other_players()
    {
        var game = FinishedGame();
        var json = JsonSerializer.Serialize(game.ViewFor(game.State.Players[1].Id), GameState.Json);
        Assert.DoesNotContain("user-alice", json);
        Assert.DoesNotContain(game.State.Players[0].Token, json);
    }

    [Fact]
    public void Campaign_results_carry_the_match_result()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var me = game.AddPlayer("Alice", "c1", "user-alice");
        game.StartCampaign(me.PlayerId, CampaignLevels.Get("newsletter"));
        var bot = game.State.Players.Single(p => p.IsBot).Id;
        for (var leg = 0; leg < 3; leg++)
        {
            game.Submit(me.PlayerId, Norfolk("Muddle Snoring"));
            game.Submit(bot, Norfolk("Snorebury"));
            for (var r = 0; r < 2; r++)
            {
                var round = game.ViewFor(me.PlayerId).Round!;
                var fake = Array.FindIndex(round.Options, o => o.Label is "Muddle Snoring" or "Snorebury");
                if (round.AuthorId == me.PlayerId) game.Vote(bot, (fake + 1) % 3);
                else game.Vote(me.PlayerId, fake);
                game.Advance(me.PlayerId);
            }
        }

        var result = Assert.Single(game.ClaimResults()!);
        Assert.Equal("newsletter", result.CampaignLevelId);
        Assert.True(result.Won);
        Assert.Equal(3, result.Campaign!.Stars);
    }
}
