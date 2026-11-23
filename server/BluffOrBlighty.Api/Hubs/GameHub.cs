using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Game;
using Microsoft.AspNetCore.SignalR;

namespace BluffOrBlighty.Api.Hubs;

/// <summary>
/// Thin real-time layer over <see cref="GameSession"/>. Every change pushes a
/// "state" message to each player, holding only what that player is allowed to see.
/// </summary>
public sealed class GameHub(GameManager games, IGameBroadcaster broadcaster, BotDriver bots) : Hub
{
    private const string CodeKey = "code";
    private const string PlayerKey = "playerId";

    public async Task<JoinResult> CreateGame(string playerName)
    {
        var game = games.Create();
        var result = Run(() => game.AddPlayer(playerName, Context.ConnectionId));
        Bind(result);
        await Broadcast(game);
        return result;
    }

    public async Task<JoinResult> JoinGame(string code, string playerName)
    {
        var game = Run(() => games.Get(code));
        var result = Run(() => game.AddPlayer(playerName, Context.ConnectionId));
        Bind(result);
        await Broadcast(game);
        return result;
    }

    /// <summary>Reattach after a refresh or dropped connection, using the secret token from join.</summary>
    public async Task<JoinResult> Rejoin(string code, string token)
    {
        var game = Run(() => games.Get(code));
        var result = Run(() => game.Reconnect(token, Context.ConnectionId));
        Bind(result);
        await Broadcast(game);
        return result;
    }

    public async Task<string> AddBot(BotLevel level)
    {
        var (game, me) = Current();
        var id = Run(() => game.AddBot(me, level));
        await Broadcast(game);
        return id;
    }

    public Task RemoveBot(string botId) => Act((g, me) => g.RemoveBot(me, botId));

    public Task SetMap(string mapId) => Act((g, me) => g.SetMap(me, mapId));

    public Task StartGame() => Act((g, me) => g.Start(me));

    public Task Submit(SubmissionRequest submission) => Act((g, me) => g.Submit(me, submission));

    public Task Vote(int optionIndex) => Act((g, me) => g.Vote(me, optionIndex));

    public Task Advance() => Act((g, me) => g.Advance(me));

    public Task BackToLobby() => Act((g, me) => g.BackToLobby(me));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(CodeKey, out var code) && Context.Items.TryGetValue(PlayerKey, out var me) &&
            games.TryGet((string)code!) is { } game)
        {
            game.Disconnect((string)me!, Context.ConnectionId);
            await Broadcast(game);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task Act(Action<GameSession, string> action)
    {
        var (game, me) = Current();
        Run(() => action(game, me));
        await Broadcast(game);
    }

    private (GameSession Game, string Me) Current()
    {
        if (!Context.Items.TryGetValue(CodeKey, out var code) || !Context.Items.TryGetValue(PlayerKey, out var me))
            throw new HubException("Join a game first.");
        return (Run(() => games.Get((string)code!)), (string)me!);
    }

    private void Bind(JoinResult result)
    {
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

    private static void Run(Action f) => Run(() => { f(); return 0; });
}
