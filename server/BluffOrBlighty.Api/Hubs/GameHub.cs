using BluffOrBlighty.Api.Accounts;
using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Game;
using Microsoft.AspNetCore.SignalR;

namespace BluffOrBlighty.Api.Hubs;

/// <summary>
/// Thin real-time layer over <see cref="GameSession"/>. Every change goes through
/// <see cref="GameManager"/> (load, change, save), then pushes a "state" message to each player
/// holding only what that player is allowed to see.
/// </summary>
public sealed class GameHub(
    GameManager games,
    IGameBroadcaster broadcaster,
    BotDriver bots,
    IResultRecorder results,
    IHostApplicationLifetime lifetime) : Hub
{
    private const string CodeKey = "code";
    private const string PlayerKey = "playerId";

    /// <summary>The signed-in account on this connection, if any (from the session cookie).</summary>
    private string? UserId => Context.User?.CurrentUserId();

    /// <summary>Starts a solo match against a campaign level's opponent.</summary>
    public async Task<JoinResult> StartCampaign(string playerName, string levelId)
    {
        var level = Run(() => CampaignLevels.Get(levelId));
        var (game, result) = await Run(() => games.CreateAsync(g =>
        {
            var joined = g.AddPlayer(playerName, Context.ConnectionId, UserId);
            g.StartCampaign(joined.PlayerId, level);
            return joined;
        }));
        await Bind(result);
        await Broadcast(game);
        return result;
    }

    public async Task<JoinResult> CreateGame(string playerName)
    {
        var (game, result) = await Run(() => games.CreateAsync(g => g.AddPlayer(playerName, Context.ConnectionId, UserId)));
        await Bind(result);
        await Broadcast(game);
        return result;
    }

    public async Task<JoinResult> JoinGame(string code, string playerName)
    {
        var (game, result) = await Run(() => games.UpdateAsync(code, g => g.AddPlayer(playerName, Context.ConnectionId, UserId)));
        await Bind(result);
        await Broadcast(game);
        return result;
    }

    /// <summary>
    /// Reattach after a refresh, a dropped connection or a server deploy, using the secret token
    /// from join. The game is loaded from the store, so this works on any server instance.
    /// </summary>
    public async Task<JoinResult> Rejoin(string code, string token)
    {
        var (game, result) = await Run(() => games.UpdateAsync(code, g => g.Reconnect(token, Context.ConnectionId, UserId)));
        await Bind(result);
        await Broadcast(game);
        return result;
    }

    public async Task<string> AddBot(BotLevel level)
    {
        var (code, me) = Current();
        var (game, id) = await Run(() => games.UpdateAsync(code, g => g.AddBot(me, level)));
        await Broadcast(game);
        return id;
    }

    public Task RemoveBot(string botId) => Act((g, me) => g.RemoveBot(me, botId));

    public Task SetMaxSpread(double? km) => Act((g, me) => g.SetMaxSpread(me, km));

    public Task SetMap(string mapId) => Act((g, me) => g.SetMap(me, mapId));

    public Task StartGame() => Act((g, me) => g.Start(me));

    public Task Submit(SubmissionRequest submission) => Act((g, me) => g.Submit(me, submission));

    public Task Vote(int optionIndex) => Act((g, me) => g.Vote(me, optionIndex));

    public Task Advance() => Act((g, me) => g.Advance(me));

    public Task BackToLobby() => Act((g, me) => g.BackToLobby(me));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // When this server is shutting down (e.g. a deploy), every connection drops at once.
        // Those players haven't left: they'll reconnect to the new server and rejoin. Marking
        // them away now could start rounds without them, so leave the game as it is.
        if (!lifetime.ApplicationStopping.IsCancellationRequested &&
            Context.Items.TryGetValue(CodeKey, out var code) && Context.Items.TryGetValue(PlayerKey, out var me))
        {
            try
            {
                var game = await games.UpdateAsync((string)code!, g => g.Disconnect((string)me!, Context.ConnectionId));
                await Broadcast(game);
            }
            catch (GameException)
            {
                // The game has expired; nothing to update.
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task Act(Action<GameSession, string> action)
    {
        var (code, me) = Current();
        var game = await Run(() => games.UpdateAsync(code, g => action(g, me)));
        await Broadcast(game);
        await SaveResultsIfFinished(game);
    }

    /// <summary>
    /// When a game has just finished, save it to signed-in players' accounts. The claim is a
    /// versioned save, so only one server records it even if two notice at once.
    /// </summary>
    private async Task SaveResultsIfFinished(GameSession game)
    {
        if (!results.Enabled || game.Phase != GamePhase.Finished || game.State.ResultsRecorded) return;
        try
        {
            var (_, claimed) = await games.UpdateAsync(game.Code, g => g.ClaimResults());
            if (claimed is { Count: > 0 }) await results.RecordAsync(claimed);
        }
        catch (GameException)
        {
            // The game expired in between; nothing to save.
        }
    }

    private (string Code, string Me) Current()
    {
        if (!Context.Items.TryGetValue(CodeKey, out var code) || !Context.Items.TryGetValue(PlayerKey, out var me))
            throw new HubException("Join a game first.");
        return ((string)code!, (string)me!);
    }

    /// <summary>Points this connection at a game, leaving whatever game it was in before.</summary>
    private async Task Bind(JoinResult result)
    {
        if (Context.Items.TryGetValue(CodeKey, out var oldCode) && Context.Items.TryGetValue(PlayerKey, out var oldMe) &&
            (string)oldCode! != result.Code)
        {
            try
            {
                var old = await games.UpdateAsync((string)oldCode!, g => g.Disconnect((string)oldMe!, Context.ConnectionId));
                await broadcaster.BroadcastAsync(old);
            }
            catch (GameException)
            {
                // The old game has gone; nothing to leave.
            }
        }

        Context.Items[CodeKey] = result.Code;
        Context.Items[PlayerKey] = result.PlayerId;
    }

    /// <summary>Sends everyone the new state, then lets any bots react to it.</summary>
    private async Task Broadcast(GameSession game)
    {
        await broadcaster.BroadcastAsync(game);
        bots.Kick(game);
    }

    /// <summary>Turns rule violations into HubExceptions, whose message reaches the client.</summary>
    private static T Run<T>(Func<T> f)
    {
        try { return f(); }
        catch (GameException e) { throw new HubException(e.Message); }
    }

    private static async Task<T> Run<T>(Func<Task<T>> f)
    {
        try { return await f(); }
        catch (GameException e) { throw new HubException(e.Message); }
    }

    private static async Task<GameSession> Run(Func<Task<GameSession>> f)
    {
        try { return await f(); }
        catch (GameException e) { throw new HubException(e.Message); }
    }
}
