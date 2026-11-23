using BluffOrBlighty.Api.Game;
using Microsoft.AspNetCore.SignalR;

namespace BluffOrBlighty.Api.Hubs;

/// <summary>Sends each connected player their own view of a game. Usable from outside the hub (e.g. by bots).</summary>
public interface IGameBroadcaster
{
    Task BroadcastAsync(GameSession game);
}

public sealed class GameBroadcaster(IHubContext<GameHub> hub) : IGameBroadcaster
{
    public Task BroadcastAsync(GameSession game) =>
        Task.WhenAll(game.ViewsForConnectedPlayers()
            .Select(x => hub.Clients.Client(x.ConnectionId).SendAsync("state", x.View)));
}
