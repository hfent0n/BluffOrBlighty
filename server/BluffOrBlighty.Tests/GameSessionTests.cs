using BluffOrBlighty.Api.Game;

namespace BluffOrBlighty.Tests;

public class GameSessionTests
{
    // Three real villages in north Norfolk, a few km apart.
    private static readonly PlacePick GreatSnoring = new("Great Snoring", 52.8706, 0.8836);
    private static readonly PlacePick LittleSnoring = new("Little Snoring", 52.8613, 0.9093);
    private static readonly PlacePick Thursford = new("Thursford", 52.8598, 0.9449);

    private static SubmissionRequest NorfolkSubmission(string fake = "Muddle Snoring") =>
        new([GreatSnoring, LittleSnoring], Thursford, fake);

    private static (GameSession Game, JoinResult Alice, JoinResult Bob, JoinResult Cat) ThreePlayerGame()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c-alice");
        var bob = game.AddPlayer("Bob", "c-bob");
        var cat = game.AddPlayer("Cat", "c-cat");
        game.Start(alice.PlayerId);
        return (game, alice, bob, cat);
    }

    [Fact]
    public void First_player_is_host_and_only_host_can_start()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c1");
        var bob = game.AddPlayer("Bob", "c2");

        Assert.Throws<GameException>(() => game.Start(bob.PlayerId));
        game.Start(alice.PlayerId);
        Assert.Equal(GamePhase.Submitting, game.Phase);
    }

    [Fact]
    public void Guessing_starts_once_everyone_has_submitted()
    {
        var (game, alice, bob, cat) = ThreePlayerGame();
        game.Submit(alice.PlayerId, NorfolkSubmission());
        game.Submit(bob.PlayerId, NorfolkSubmission());
        Assert.Equal(GamePhase.Submitting, game.Phase);

        game.Submit(cat.PlayerId, NorfolkSubmission());
        Assert.Equal(GamePhase.Guessing, game.Phase);
        Assert.Equal(3, game.ViewFor(alice.PlayerId).Round!.Total);
    }

    [Fact]
    public void Views_hide_the_fake_and_original_name_until_reveal()
    {
        var (game, alice, bob, cat) = ThreePlayerGame();
        foreach (var p in new[] { alice, bob, cat }) game.Submit(p.PlayerId, NorfolkSubmission());

        var round = game.ViewFor(bob.PlayerId).Round!;
        Assert.Null(round.Reveal);
        Assert.Equal(3, round.Options.Length);
        // The renamed place's location is shown, but under the fake label.
        Assert.Contains(round.Options, o => o.Label == "Muddle Snoring" && o.Lat == Thursford.Lat);
        Assert.DoesNotContain(round.Options, o => o.Label == "Thursford");
    }

    [Fact]
    public void Scoring_rewards_correct_guesses_and_fooling_people()
    {
        var (game, alice, bob, cat) = ThreePlayerGame();
        foreach (var p in new[] { alice, bob, cat }) game.Submit(p.PlayerId, NorfolkSubmission());

        // Round 1 belongs to Alice (join order).
        var round = game.ViewFor(bob.PlayerId).Round!;
        Assert.Equal(alice.PlayerId, round.AuthorId);
        var fakeIndex = Array.FindIndex(round.Options, o => o.Label == "Muddle Snoring");
        var wrongIndex = (fakeIndex + 1) % 3;

        game.Vote(bob.PlayerId, fakeIndex); // Bob spots it
        Assert.Null(game.ViewFor(bob.PlayerId).Round!.Reveal);
        game.Vote(cat.PlayerId, wrongIndex); // Cat is fooled -> auto reveal

        var view = game.ViewFor(alice.PlayerId);
        Assert.NotNull(view.Round!.Reveal);
        Assert.Equal(fakeIndex, view.Round.Reveal!.FakeIndex);
        Assert.Equal("Thursford", view.Round.Reveal.OriginalName);
        Assert.Equal(1, view.Players.Single(p => p.Id == alice.PlayerId).Score);
        Assert.Equal(1, view.Players.Single(p => p.Id == bob.PlayerId).Score);
        Assert.Equal(0, view.Players.Single(p => p.Id == cat.PlayerId).Score);
    }

    [Fact]
    public void Author_cannot_vote_on_own_round()
    {
        var (game, alice, bob, cat) = ThreePlayerGame();
        foreach (var p in new[] { alice, bob, cat }) game.Submit(p.PlayerId, NorfolkSubmission());

        Assert.Throws<GameException>(() => game.Vote(alice.PlayerId, 0));
    }

    [Fact]
    public void Host_advances_through_rounds_to_finish()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c1");
        var bob = game.AddPlayer("Bob", "c2");
        game.Start(alice.PlayerId);
        game.Submit(alice.PlayerId, NorfolkSubmission());
        game.Submit(bob.PlayerId, NorfolkSubmission());

        game.Advance(alice.PlayerId); // reveal round 1 early
        game.Advance(alice.PlayerId); // next round
        Assert.Equal(1, game.ViewFor(alice.PlayerId).Round!.Index);
        game.Vote(alice.PlayerId, 0); // auto reveal round 2
        game.Advance(alice.PlayerId);
        Assert.Equal(GamePhase.Finished, game.Phase);
    }

    [Fact]
    public void Disconnected_player_does_not_block_submissions()
    {
        var (game, alice, bob, cat) = ThreePlayerGame();
        game.Submit(alice.PlayerId, NorfolkSubmission());
        game.Submit(bob.PlayerId, NorfolkSubmission());
        game.Disconnect(cat.PlayerId, "c-cat");
        Assert.Equal(GamePhase.Guessing, game.Phase);
        Assert.Equal(2, game.ViewFor(alice.PlayerId).Round!.Total);
    }

    [Fact]
    public void Reconnect_with_token_restores_player()
    {
        var (game, _, bob, _) = ThreePlayerGame();
        game.Disconnect(bob.PlayerId, "c-bob");
        var again = game.Reconnect(bob.Token, "c-bob-2");
        Assert.Equal(bob.PlayerId, again.PlayerId);
        Assert.Throws<GameException>(() => game.Reconnect("not-a-token", "x"));
    }

    [Theory]
    [InlineData("Great Snoring")]
    [InlineData("  great   snoring ")]
    public void Fake_name_cannot_match_a_real_pick(string fake)
    {
        var (game, alice, _, _) = ThreePlayerGame();
        Assert.Throws<GameException>(() => game.Submit(alice.PlayerId, NorfolkSubmission(fake)));
    }

    [Fact]
    public void Places_must_be_close_together()
    {
        var (game, alice, _, _) = ThreePlayerGame();
        var penzance = new PlacePick("Penzance", 50.1186, -5.5371);
        var ex = Assert.Throws<GameException>(() =>
            game.Submit(alice.PlayerId, new SubmissionRequest([GreatSnoring, penzance], Thursford, "Fakeville")));
        Assert.Contains("km", ex.Message);
    }

    [Fact]
    public void Same_place_twice_is_rejected()
    {
        var (game, alice, _, _) = ThreePlayerGame();
        Assert.Throws<GameException>(() =>
            game.Submit(alice.PlayerId, new SubmissionRequest([GreatSnoring, GreatSnoring], Thursford, "Fakeville")));
    }

    [Fact]
    public void Back_to_lobby_resets_scores()
    {
        var (game, alice, bob, cat) = ThreePlayerGame();
        foreach (var p in new[] { alice, bob, cat }) game.Submit(p.PlayerId, NorfolkSubmission());
        game.Advance(alice.PlayerId);
        game.BackToLobby(alice.PlayerId);
        var view = game.ViewFor(alice.PlayerId);
        Assert.Equal(GamePhase.Lobby, view.Phase);
        Assert.All(view.Players, p => Assert.Equal(0, p.Score));
    }
}
