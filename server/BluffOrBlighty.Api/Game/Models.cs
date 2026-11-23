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

public enum BotLevel
{
    Easy,
    Normal,
    Hard,
}

/// <summary>How a bot invents fake names.</summary>
public enum FakeStyle
{
    /// <summary>Silly made-up stems ("Wobblecombe-on-Sea"). Easy to spot.</summary>
    Obvious,

    /// <summary>Grafts nearby real names onto common endings, with the usual prefixes and suffixes.</summary>
    Local,

    /// <summary>Copies only the patterns the area itself uses, so the fake blends in.</summary>
    Crafty,

    /// <summary>
    /// Splices the start of one local name onto the end of another. Works for any language,
    /// so it's what bots use outside the UK, where English endings would give the fake away.
    /// </summary>
    Splice,
}

/// <summary>
/// How a bot plays. <see cref="Knowledge"/> is the chance it checks the map to find the fake
/// (otherwise it guesses).
/// </summary>
public sealed record BotProfile(double Knowledge, FakeStyle Style)
{
    public static BotProfile For(BotLevel level) => level switch
    {
        BotLevel.Easy => new(0.2, FakeStyle.Obvious),
        BotLevel.Hard => new(0.85, FakeStyle.Crafty),
        _ => new(0.55, FakeStyle.Local),
    };
}

public sealed class Player
{
    public required string Id { get; init; }

    /// <summary>Secret used to reconnect. Never included in views sent to other players.</summary>
    public required string Token { get; init; }

    public required string Name { get; init; }
    public string? ConnectionId { get; set; }

    /// <summary>Bots are played by the server and always count as present.</summary>
    public bool IsBot { get; init; }

    public BotLevel? BotLevel { get; init; }
    public BotProfile? BotProfile { get; init; }
    public bool IsConnected => IsBot || ConnectionId is not null;
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

public sealed record PlayerView(
    string Id,
    string Name,
    int Score,
    bool IsHost,
    bool IsConnected,
    bool HasSubmitted,
    bool HasVoted,
    bool IsBot,
    BotLevel? BotLevel);

/// <summary>Something a bot needs to do. The bot driver polls for these after every change.</summary>
public abstract record BotWork(string BotId, BotProfile Profile, int Leg)
{
    /// <summary>Identifies this piece of work, so it's only started once.</summary>
    public abstract string Key { get; }
}

public sealed record BotSubmitWork(string BotId, BotProfile Profile, int Leg, MapPreset Map, double? MaxSpreadKm)
    : BotWork(BotId, Profile, Leg)
{
    public override string Key => $"{BotId}:submit:{Leg}";
}

public sealed record BotVoteWork(string BotId, BotProfile Profile, int Leg, int RoundIndex, OptionView[] Options)
    : BotWork(BotId, Profile, Leg)
{
    public override string Key => $"{BotId}:vote:{Leg}:{RoundIndex}";
}

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

/// <summary>How a campaign match went for the human player. Only present once the match has finished.</summary>
public sealed record CampaignResult(bool Won, int Stars, int YourScore, int OpponentScore, int FakesSpotted, int TimesFooled);

public sealed record CampaignView(
    string LevelId,
    int Number,
    string Title,
    string Opponent,
    CampaignResult? Result);

public sealed record GameView(
    string Code,
    GamePhase Phase,
    string MeId,
    string HostId,
    MapPreset Map,
    // Max distance between a player's 3 picks; null = no limit.
    double? MaxSpreadKm,
    PlayerView[] Players,
    RoundView? Round,
    // 1-based. A leg is one round for each player; most games are a single leg.
    int Leg,
    int Legs,
    CampaignView? Campaign);
