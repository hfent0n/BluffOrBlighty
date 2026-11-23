namespace BluffOrBlighty.Api.Game;

/// <summary>
/// One rung of the single-player ladder: a named opponent on a fixed map. Each level gets
/// better at spotting fakes and at writing them.
/// </summary>
public sealed record CampaignLevel(
    string Id,
    int Number,
    string Title,
    string Opponent,
    string Blurb,
    string MapId,
    BotProfile Bot,
    int Legs)
{
    /// <summary>1–5, for the difficulty pips on the ladder.</summary>
    public int Difficulty => Math.Clamp((int)Math.Ceiling(Bot.Knowledge * 5.5), 1, 5);
}

/// <summary>Public description of a level, for the ladder screen.</summary>
public sealed record CampaignLevelView(
    string Id,
    int Number,
    string Title,
    string Opponent,
    string Blurb,
    string MapId,
    string MapName,
    int Difficulty,
    int Legs);

public static class CampaignLevels
{
    public static readonly IReadOnlyList<CampaignLevel> All =
    [
        new("newsletter", 1, "Parish Newsletter Editor", "Doris",
            "Doris has lived in Little Snoring for sixty years, but she's never looked at a map.",
            "north-norfolk", new BotProfile(0.15, FakeStyle.Obvious), 3),
        new("fete", 2, "Village Fete Organiser", "Reg",
            "Reg runs the tombola in all three Wallops. His fakes are about as subtle as his jumpers.",
            "the-wallops", new BotProfile(0.3, FakeStyle.Obvious), 3),
        new("rambler", 3, "Weekend Rambler", "Bryony",
            "Bryony has walked every footpath in the Cotswolds and remembers about half of them.",
            "cotswolds", new BotProfile(0.4, FakeStyle.Local), 3),
        new("pub-quiz", 4, "Pub Quiz Captain", "Clive",
            "Clive's team has won the Tolpuddle quiz four years running. He knows his Piddles from his Puddles.",
            "hardy-dorset", new BotProfile(0.5, FakeStyle.Local), 3),
        new("historian", 5, "Local Historian", "Marjorie",
            "Marjorie wrote the book on Mavis Enderby. Literally. It's in the church.",
            "lincs-wolds", new BotProfile(0.6, FakeStyle.Local), 3),
        new("postie", 6, "Rural Postie", "Dafydd",
            "Dafydd delivers to every farm on Anglesey and spells all of them correctly.",
            "anglesey", new BotProfile(0.7, FakeStyle.Crafty), 3),
        new("surveyor", 7, "County Surveyor", "Agnes",
            "Agnes measured Swaledale with a chain and a theodolite. Crackpot doesn't fool her.",
            "swaledale", new BotProfile(0.8, FakeStyle.Crafty), 3),
        new("cartographer", 8, "Keeper of the Maps", "The Cartographer",
            "Nobody knows how long the Cartographer has been in West Penwith. Best of five.",
            "west-penwith", new BotProfile(0.9, FakeStyle.Crafty), 5),
    ];

    public static CampaignLevel Get(string? id) =>
        All.FirstOrDefault(l => l.Id == id) ?? throw new GameException("Unknown campaign level.");

    public static IEnumerable<CampaignLevelView> Views() =>
        All.Select(l => new CampaignLevelView(
            l.Id, l.Number, l.Title, l.Opponent, l.Blurb, l.MapId, MapPresets.Get(l.MapId).Name, l.Difficulty, l.Legs));

    /// <summary>
    /// Stars for a finished match: 1 for winning, +1 for winning by 2 or more,
    /// +1 for spotting every one of the opponent's fakes.
    /// </summary>
    public static int Stars(bool won, int margin, int spotted, int legs) =>
        !won ? 0 : 1 + (margin >= 2 ? 1 : 0) + (spotted >= legs ? 1 : 0);
}
