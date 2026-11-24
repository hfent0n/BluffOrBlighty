using System.Text.Json;
using System.Text.Json.Serialization;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// Everything about one game, as plain data, so it can be stored outside the server process
/// (Redis in production) and survive deploys. <see cref="GameSession"/> holds the rules that act on it.
/// </summary>
public sealed class GameState
{
    public required string Code { get; init; }

    /// <summary>Bumped on every save; the store rejects a save made from an out-of-date copy.</summary>
    public long Version { get; set; }

    public GamePhase Phase { get; set; } = GamePhase.Lobby;
    public DateTimeOffset LastActivity { get; set; }
    public List<Player> Players { get; set; } = [];
    public string HostId { get; set; } = "";

    // Settings
    public string MapId { get; set; } = MapPresets.AnywhereId;

    /// <summary>Max distance between a player's 3 picks. Null means no limit.</summary>
    public double? MaxSpreadKm { get; set; } = GameSession.DefaultMaxSpreadKm;

    public string? CampaignLevelId { get; set; }

    // Progress
    public Dictionary<string, SubmissionRequest> Submissions { get; set; } = [];
    public List<Round> Rounds { get; set; } = [];
    public int RoundIndex { get; set; }
    public int Leg { get; set; } = 1;
    public int Legs { get; set; } = 1;

    /// <summary>Set once a finished game's results have been saved to players' accounts.</summary>
    public bool ResultsRecorded { get; set; }

    // Per-player tallies across all legs, for campaign stars.
    public Dictionary<string, int> Spotted { get; set; } = [];
    public Dictionary<string, int> Fooled { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static GameState FromJson(string json) =>
        JsonSerializer.Deserialize<GameState>(json, Json) ?? throw new InvalidOperationException("Empty game state.");
}
