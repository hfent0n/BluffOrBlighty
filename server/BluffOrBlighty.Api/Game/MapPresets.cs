namespace BluffOrBlighty.Api.Game;

/// <summary>
/// A starting area for a game. Players can only pick places within <see cref="RadiusKm"/> of the
/// centre. A null radius means anywhere.
/// <see cref="Examples"/> are real places in the area. <see cref="Decoy"/> is a made-up one that the
/// maps screen slips in among them, as a taste of the game.
/// </summary>
public sealed record MapPreset(
    string Id,
    string Name,
    string Region,
    string Country,
    string Description,
    double Lat,
    double Lng,
    double Zoom,
    double? RadiusKm,
    string[] Examples,
    string? Decoy = null);

public static class MapPresets
{
    public const string AnywhereId = "anywhere";

    /// <summary>Extra allowance at the edge, so a label straddling the boundary still counts.</summary>
    public const double EdgeToleranceKm = 1.0;

    public static readonly MapPreset Anywhere = new(
        AnywhereId, "Anywhere", "Great Britain & Northern Ireland", "UK",
        "The whole map. Everyone roams wherever they like.",
        54.3, -2.5, 5.3, null, []);

    public static readonly IReadOnlyList<MapPreset> All =
    [
        Anywhere,
        new("north-norfolk", "The Snorings", "North Norfolk", "England",
            "Flat fields, flint churches and some very sleepy village names.",
            52.87, 0.93, 11, 15, ["Great Snoring", "Little Snoring", "Thursford", "Hindringham"], "Much Snoozing-in-the-Marsh"),
        new("the-wallops", "Wallop Country", "Test Valley, Hampshire", "England",
            "Three Wallops and a Palestine, all a short drive apart.",
            51.14, -1.58, 11.5, 12, ["Nether Wallop", "Middle Wallop", "Over Wallop", "Palestine"], "Wallop-under-Codswallop"),
        new("hardy-dorset", "Hardy's Dorset", "Central Dorset", "England",
            "Piddles, Puddles and Plush. Rich ground for a convincing fake.",
            50.78, -2.38, 10.5, 22, ["Piddletrenthide", "Ryme Intrinseca", "Plush", "Tolpuddle"], "Puddlewhistle Magna"),
        new("cotswolds", "The Cotswolds", "Gloucestershire", "England",
            "Honey-coloured stone and Slaughters, Upper and Lower.",
            51.9, -1.78, 10.5, 20, ["Upper Slaughter", "Lower Slaughter", "Stow-on-the-Wold", "Temple Guiting"], "Lower Slaughterhouse-on-the-Wold"),
        new("lincs-wolds", "Lincolnshire Wolds", "Near Spilsby, Lincolnshire", "England",
            "Quiet lanes, big skies and names that sound made up already.",
            53.17, 0.0, 11, 15, ["Mavis Enderby", "Old Bolingbroke", "Ashby Puerorum", "Hagworthingham"], "Nether Grumbleby"),
        new("essex-ends", "Ugley & the Ends", "West Essex", "England",
            "Greens, Ends and Tyes. Ugley is real, honest.",
            51.82, 0.28, 10.5, 20, ["Ugley", "Good Easter", "Matching Tye", "Pleshey"], "Ugley-cum-Ghastly"),
        new("swaledale", "Swaledale", "Yorkshire Dales", "England",
            "Remote dales where Crackpot and Booze are genuine addresses.",
            54.4, -2.0, 10.5, 18, ["Crackpot", "Booze", "Muker", "Keld"], "Booze-on-the-Rocks"),
        new("yorkshire-wolds", "Yorkshire Wolds", "East Riding of Yorkshire", "England",
            "Rolling chalk hills, home of Wetwang and Fridaythorpe.",
            54.02, -0.6, 11, 15, ["Wetwang", "Fridaythorpe", "Sledmere", "Fimber"], "Wetwang-under-Dribble"),
        new("chew-valley", "Chew Valley", "North Somerset", "England",
            "Lakes, hills and the gloriously named Nempnett Thrubwell.",
            51.35, -2.65, 11, 12, ["Nempnett Thrubwell", "Chew Magna", "Compton Dando", "Ubley"], "Nempnett Thrubwobble"),
        new("west-penwith", "West Penwith", "Far west Cornwall", "England",
            "Granite coast, Cornish names and Mousehole (say 'Mowzel').",
            50.12, -5.55, 10.5, 18, ["Mousehole", "Zennor", "Lamorna", "Praze-an-Beeble"], "Praze-an-Pasty"),
        new("anglesey", "Anglesey", "Ynys Môn", "Wales",
            "Home of the longest place name in Europe. Your fake needs to be good.",
            53.25, -4.35, 10, 22, ["Llanfairpwllgwyngyll", "Pentraeth", "Brynsiencyn", "Llangefni"], "Llanfairpwllgwyngyll-on-Sea"),
        new("highland-perthshire", "Highland Perthshire", "Near Aberfeldy", "Scotland",
            "Lochs and glens, plus Dull, which is twinned with Boring, Oregon.",
            56.62, -3.9, 10.5, 18, ["Dull", "Aberfeldy", "Kenmore", "Weem"], "Even Duller"),
    ];

    public static MapPreset Get(string? id) =>
        All.FirstOrDefault(m => m.Id == id) ?? throw new GameException("Unknown map.");

    /// <summary>True when the point is inside the map's area (always true for Anywhere).</summary>
    public static bool Contains(MapPreset map, double lat, double lng) =>
        map.RadiusKm is not { } r || Geo.DistanceKm(map.Lat, map.Lng, lat, lng) <= r + EdgeToleranceKm;
}
