using BluffOrBlighty.Api.Game;

namespace BluffOrBlighty.Tests;

/// <summary>
/// Game state now lives in a store (Redis in production), so a game must survive being
/// saved and loaded at any point, and concurrent changes must not overwrite each other.
/// These run against the in-memory store, which stores JSON just like Redis.
/// </summary>
public class GameStoreTests
{
    private static readonly PlacePick GreatSnoring = new("Great Snoring", 52.8706, 0.8836, "label_village");
    private static readonly PlacePick LittleSnoring = new("Little Snoring", 52.8613, 0.9093, "label_village");
    private static readonly PlacePick Thursford = new("Thursford", 52.8598, 0.9449, "label_village");

    private static SubmissionRequest Norfolk(string fake) => new([GreatSnoring, LittleSnoring], Thursford, fake);

    private static GameManager NewManager() =>
        new(new InMemoryGameStore(TimeProvider.System), TimeProvider.System);

    [Fact]
    public async Task A_whole_game_survives_being_saved_and_reloaded_between_every_action()
    {
        var games = NewManager();
        var (game, alice) = await games.CreateAsync(g => g.AddPlayer("Alice", "c-alice"));
        var code = game.Code;

        var (_, bob) = await games.UpdateAsync(code, g => g.AddPlayer("Bob", "c-bob"));
        await games.UpdateAsync(code, g => g.SetMap(alice.PlayerId, "north-norfolk"));
        await games.UpdateAsync(code, g => g.SetMaxSpread(alice.PlayerId, 10));
        await games.UpdateAsync(code, g => g.Start(alice.PlayerId));
        await games.UpdateAsync(code, g => g.Submit(alice.PlayerId, Norfolk("Muddle Snoring")));
        game = await games.UpdateAsync(code, g => g.Submit(bob.PlayerId, Norfolk("Snorebury")));

        Assert.Equal(GamePhase.Guessing, game.Phase);
        var view = game.ViewFor(bob.PlayerId);
        Assert.Equal("north-norfolk", view.Map.Id);
        Assert.Equal(10, view.MaxSpreadKm);

        // Bob spots Alice's fake; the answer (and the original name) came back from the store intact.
        var fake = Array.FindIndex(view.Round!.Options, o => o.Label == "Muddle Snoring");
        game = await games.UpdateAsync(code, g => g.Vote(bob.PlayerId, fake));
        var reveal = game.ViewFor(alice.PlayerId).Round!.Reveal!;
        Assert.Equal(fake, reveal.FakeIndex);
        Assert.Equal("Thursford", reveal.OriginalName);
        Assert.Equal(1, game.ViewFor(alice.PlayerId).Players.Single(p => p.Id == bob.PlayerId).Score);
    }

    [Fact]
    public async Task Reconnecting_after_a_restart_finds_the_player_by_token()
    {
        var store = new InMemoryGameStore(TimeProvider.System);
        var before = new GameManager(store, TimeProvider.System);
        var (game, alice) = await before.CreateAsync(g => g.AddPlayer("Alice", "old-connection"));

        // A new server instance with the same store, like after a deploy.
        var after = new GameManager(store, TimeProvider.System);
        var (_, again) = await after.UpdateAsync(game.Code, g => g.Reconnect(alice.Token, "new-connection"));
        Assert.Equal(alice.PlayerId, again.PlayerId);
        var reloaded = (await after.TryGetAsync(game.Code))!;
        Assert.Contains(reloaded.ViewsForConnectedPlayers(), v => v.ConnectionId == "new-connection");
    }

    [Fact]
    public async Task Concurrent_changes_are_all_kept()
    {
        var games = NewManager();
        var (game, _) = await games.CreateAsync(g => g.AddPlayer("Host", "c0"));

        // Eleven players join at once, as if from different server instances.
        await Task.WhenAll(Enumerable.Range(1, 11).Select(i =>
            Task.Run(() => games.UpdateAsync(game.Code, g => g.AddPlayer($"Player {i}", $"c{i}")))));

        var final = (await games.TryGetAsync(game.Code))!;
        Assert.Equal(12, final.ViewFor(final.State.HostId).Players.Length);
    }

    [Fact]
    public async Task A_save_from_an_out_of_date_copy_is_rejected()
    {
        var store = new InMemoryGameStore(TimeProvider.System);
        var games = new GameManager(store, TimeProvider.System);
        var (game, _) = await games.CreateAsync(g => g.AddPlayer("Alice", "c1"));

        var stale = (await store.GetAsync(game.Code))!;
        await games.UpdateAsync(game.Code, g => g.AddPlayer("Bob", "c2")); // moves the version on

        Assert.False(await store.TrySaveAsync(game.Code, stale.Version, stale.Version + 1, stale.Json));
        Assert.Equal(2, (await games.TryGetAsync(game.Code))!.State.Players.Count);
    }

    [Fact]
    public async Task Moves_on_a_game_this_instance_has_cost_one_save_and_no_reads()
    {
        var store = new InMemoryGameStore(TimeProvider.System);
        var games = new GameManager(store, TimeProvider.System);
        var (game, alice) = await games.CreateAsync(g => g.AddPlayer("Alice", "c1"));

        await games.UpdateAsync(game.Code, g => g.AddPlayer("Bob", "c2"));
        await games.UpdateAsync(game.Code, g => g.SetMap(alice.PlayerId, "north-norfolk"));
        await games.UpdateAsync(game.Code, g => g.Start(alice.PlayerId));
        await games.TryGetAsync(game.Code);

        Assert.Equal(0, store.Reads);
        Assert.Equal(4, store.Writes); // create + 3 moves
    }

    [Fact]
    public async Task Two_instances_on_one_store_keep_every_change()
    {
        // Like the old and new versions overlapping during a deploy: each has its own memory.
        var store = new InMemoryGameStore(TimeProvider.System);
        var a = new GameManager(store, TimeProvider.System);
        var b = new GameManager(store, TimeProvider.System);
        var (game, _) = await a.CreateAsync(g => g.AddPlayer("Host", "c0"));

        await b.UpdateAsync(game.Code, g => g.AddPlayer("Bob", "c1"));   // B loads, saves v2
        await a.UpdateAsync(game.Code, g => g.AddPlayer("Cat", "c2"));   // A's copy is stale: reload, retry
        await b.UpdateAsync(game.Code, g => g.AddPlayer("Dee", "c3"));   // and B's is now stale

        await Task.WhenAll(Enumerable.Range(4, 8).Select(i =>
            Task.Run(() => (i % 2 == 0 ? a : b).UpdateAsync(game.Code, g => g.AddPlayer($"P{i}", $"c{i}")))));

        var names = (await new GameManager(store, TimeProvider.System).TryGetAsync(game.Code))!
            .State.Players.Select(p => p.Name).ToList();
        Assert.Equal(12, names.Count);
        Assert.Contains("Bob", names);
        Assert.Contains("Cat", names);
        Assert.Contains("Dee", names);
    }

    [Fact]
    public async Task A_move_rejected_part_way_through_leaves_the_game_unchanged()
    {
        var games = NewManager();
        var (game, me) = await games.CreateAsync(g =>
        {
            var joined = g.AddPlayer("Alice", "c1");
            g.StartCampaign(joined.PlayerId, CampaignLevels.Get("newsletter"));
            return joined;
        });
        await games.UpdateAsync(game.Code, g => g.Disconnect(me.PlayerId, "c1")); // Alice is away

        // BackToLobby drops away players before refusing campaign games. The cached copy
        // must not keep that half-made change.
        await Assert.ThrowsAsync<GameException>(() => games.UpdateAsync(game.Code, g => g.BackToLobby(me.PlayerId)));
        Assert.Equal(2, (await games.TryGetAsync(game.Code))!.State.Players.Count);
    }

    [Fact]
    public async Task Rule_errors_save_nothing()
    {
        var games = NewManager();
        var (game, _) = await games.CreateAsync(g => g.AddPlayer("Alice", "c1"));
        var version = game.State.Version;

        await Assert.ThrowsAsync<GameException>(() => games.UpdateAsync(game.Code, g => g.AddPlayer("alice", "c2")));
        Assert.Equal(version, (await games.TryGetAsync(game.Code))!.State.Version);
    }

    [Fact]
    public async Task Unknown_codes_say_so()
    {
        var ex = await Assert.ThrowsAsync<GameException>(() => NewManager().UpdateAsync("ZZZZ", g => g.Start("x")));
        Assert.Contains("No game", ex.Message);
    }

    [Fact]
    public async Task Campaign_games_round_trip_too()
    {
        var games = NewManager();
        var (game, me) = await games.CreateAsync(g =>
        {
            var joined = g.AddPlayer("Alice", "c1");
            g.StartCampaign(joined.PlayerId, CampaignLevels.Get("newsletter"));
            return joined;
        });

        var reloaded = (await games.TryGetAsync(game.Code))!;
        var view = reloaded.ViewFor(me.PlayerId);
        Assert.Equal("newsletter", view.Campaign!.LevelId);
        Assert.Equal(3, view.Legs);
        Assert.Contains(reloaded.PendingBotWork(), w => w is BotSubmitWork { Leg: 1 });
    }
}
