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
    public void Places_must_be_within_15_km_by_default()
    {
        var (game, alice, _, _) = ThreePlayerGame();
        Assert.Equal(15, game.ViewFor(alice.PlayerId).MaxSpreadKm);
        var penzance = new PlacePick("Penzance", 50.1186, -5.5371);
        var ex = Assert.Throws<GameException>(() =>
            game.Submit(alice.PlayerId, new SubmissionRequest([GreatSnoring, penzance], Thursford, "Fakeville")));
        Assert.Contains("15 km", ex.Message);
    }

    [Fact]
    public void Host_can_turn_the_distance_limit_off()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c1");
        game.AddPlayer("Bob", "c2");
        game.SetMaxSpread(alice.PlayerId, null);
        game.Start(alice.PlayerId);

        var penzance = new PlacePick("Penzance", 50.1186, -5.5371);
        game.Submit(alice.PlayerId, new SubmissionRequest([GreatSnoring, penzance], Thursford, "Fakeville"));
        Assert.Null(game.ViewFor(alice.PlayerId).MaxSpreadKm);
    }

    [Fact]
    public void Host_can_tighten_the_distance_limit()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c1");
        game.AddPlayer("Bob", "c2");
        game.SetMaxSpread(alice.PlayerId, 3); // Great Snoring to Thursford is about 4 km
        game.Start(alice.PlayerId);

        var ex = Assert.Throws<GameException>(() => game.Submit(alice.PlayerId, NorfolkSubmission()));
        Assert.Contains("3 km", ex.Message);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(51.0)]
    [InlineData(double.NaN)]
    public void Distance_limit_must_be_in_range(double km)
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var alice = game.AddPlayer("Alice", "c1");
        Assert.Throws<GameException>(() => game.SetMaxSpread(alice.PlayerId, km));
    }

    [Fact]
    public void Only_host_sets_the_limit_and_only_in_the_lobby()
    {
        var (game, alice, bob, _) = ThreePlayerGame(); // already started
        Assert.Throws<GameException>(() => game.SetMaxSpread(bob.PlayerId, 10));
        Assert.Throws<GameException>(() => game.SetMaxSpread(alice.PlayerId, 10));
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

public class MapPresetTests
{
    private static readonly PlacePick GreatSnoring = new("Great Snoring", 52.8706, 0.8836);
    private static readonly PlacePick LittleSnoring = new("Little Snoring", 52.8613, 0.9093);
    private static readonly PlacePick Thursford = new("Thursford", 52.8598, 0.9449);

    private static (GameSession Game, JoinResult Host, JoinResult Guest) Lobby()
    {
        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        return (game, game.AddPlayer("Alice", "c1"), game.AddPlayer("Bob", "c2"));
    }

    [Fact]
    public void Defaults_to_anywhere_and_host_can_change_map()
    {
        var (game, host, guest) = Lobby();
        Assert.Equal(MapPresets.AnywhereId, game.ViewFor(host.PlayerId).Map.Id);

        Assert.Throws<GameException>(() => game.SetMap(guest.PlayerId, "north-norfolk"));
        Assert.Throws<GameException>(() => game.SetMap(host.PlayerId, "atlantis"));
        game.SetMap(host.PlayerId, "north-norfolk");
        Assert.Equal("north-norfolk", game.ViewFor(guest.PlayerId).Map.Id);
    }

    [Fact]
    public void Picks_inside_the_map_are_accepted()
    {
        var (game, host, _) = Lobby();
        game.SetMap(host.PlayerId, "north-norfolk");
        game.Start(host.PlayerId);
        game.Submit(host.PlayerId, new SubmissionRequest([GreatSnoring, LittleSnoring], Thursford, "Muddle Snoring"));
    }

    [Fact]
    public void Picks_outside_the_map_are_rejected()
    {
        var (game, host, _) = Lobby();
        game.SetMap(host.PlayerId, "the-wallops");
        game.Start(host.PlayerId);
        var ex = Assert.Throws<GameException>(() =>
            game.Submit(host.PlayerId, new SubmissionRequest([GreatSnoring, LittleSnoring], Thursford, "Muddle Snoring")));
        Assert.Contains("Wallop Country", ex.Message);
    }

    [Fact]
    public void Map_cannot_change_after_start()
    {
        var (game, host, _) = Lobby();
        game.Start(host.PlayerId);
        Assert.Throws<GameException>(() => game.SetMap(host.PlayerId, "north-norfolk"));
    }

    [Fact]
    public void Every_area_has_a_decoy_that_isnt_one_of_its_real_examples()
    {
        Assert.All(MapPresets.All.Where(m => m.RadiusKm is not null), m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Decoy), m.Id);
            Assert.DoesNotContain(m.Examples, e => e.Equals(m.Decoy, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Preset_ids_are_unique_and_examples_sit_inside_radius_sanity()
    {
        Assert.Equal(MapPresets.All.Count, MapPresets.All.Select(m => m.Id).Distinct().Count());
        Assert.All(MapPresets.All.Where(m => m.RadiusKm is not null), m => Assert.InRange(m.RadiusKm!.Value, 5, 40));
    }
}
