namespace BluffOrBlighty.Api.Game;

public enum GamePhase
{
    Lobby,
    Submitting,
    Guessing,
    Finished,
}

/// <summary>
/// A label the player clicked on the map. Lat/Lng are the label's own point, not the click position.
/// <see cref="Layer"/> is the map style layer the label came from (e.g. "label_village"), so the
/// guessing map can draw every option, fake included, in the same style as its neighbours.
/// </summary>
public sealed record PlacePick(string Name, double Lat, double Lng, string? Layer = null);

/// <summary>Two real places, plus one real place whose label gets replaced by <see cref="FakeName"/>.</summary>
public sealed record SubmissionRequest(PlacePick[] Real, PlacePick Renamed, string FakeName);

public sealed record JoinResult(string Code, string PlayerId, string Token);

public sealed class GameException(string message) : Exception(message);

public sealed class Player
{
    public required string Id { get; init; }

    /// <summary>Secret used to reconnect. Never included in views sent to other players.</summary>
    public required string Token { get; init; }

    public required string Name { get; init; }
    public string? ConnectionId { get; set; }
    public bool IsConnected => ConnectionId is not null;
    public int Score { get; set; }
}

internal sealed record RoundOption(string Label, double Lat, double Lng, string? Layer);

internal sealed class Round
{
    public required string AuthorId { get; init; }
    public required RoundOption[] Options { get; init; }
    public required int FakeIndex { get; init; }
    public required string OriginalName { get; init; }
    public Dictionary<string, int> Votes { get; } = [];
    public bool Revealed { get; set; }
}

// ---- Views sent to clients. Each player gets their own view, so the server
// ---- never tells anyone which option is fake until the round is revealed.

public sealed record PlayerView(string Id, string Name, int Score, bool IsHost, bool IsConnected, bool HasSubmitted, bool HasVoted);

/// <summary>Only coordinates and labels. The renamed place's original name is not sent until reveal.</summary>
public sealed record OptionView(string Label, double Lat, double Lng, string? Layer);

public sealed record VoteView(string PlayerId, int OptionIndex);

public sealed record RevealView(int FakeIndex, string OriginalName, VoteView[] Votes);

public sealed record RoundView(
    int Index,
    int Total,
    string AuthorId,
    OptionView[] Options,
    int? MyVote,
    RevealView? Reveal);

public sealed record GameView(
    string Code,
    GamePhase Phase,
    string MeId,
    string HostId,
    double MaxSpreadKm,
    MapPreset Map,
    PlayerView[] Players,
    RoundView? Round);
