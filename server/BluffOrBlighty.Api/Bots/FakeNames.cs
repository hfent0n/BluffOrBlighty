using System.Globalization;

namespace BluffOrBlighty.Api.Bots;

/// <summary>
/// Makes up plausible British place names. It grafts the stems of nearby real names onto
/// common English and Welsh endings, so fakes take on the area's local flavour.
/// </summary>
public static class FakeNames
{
    // Longest first, so "thorpe" is matched before "pe".
    private static readonly string[] Endings =
    [
        "borough", "chester", "minster", "thwaite", "thorpe", "worth", "combe", "hurst", "stead", "field",
        "holme", "marsh", "bury", "ford", "wich", "wick", "well", "wold", "mere", "cott", "stow", "sted",
        "ham", "ton", "ley", "den", "ing", "by", "ey",
    ];

    private static readonly string[] Prefixes =
    [
        "Great", "Little", "Upper", "Lower", "Nether", "Long", "Old", "East", "West", "North", "South",
        "Market", "Chipping", "King's", "Bishop's", "Steeple", "Castle", "Church", "Abbots",
    ];

    private static readonly string[] Suffixes =
    [
        "-on-the-Hill", "-on-the-Wold", "-on-Sea", "-in-the-Marsh", "-le-Moors", "-under-Edge",
        " Magna", " Parva", " St Mary", " St Giles", " End", " Green", " Common", " Bottom", " Tye",
    ];

    private static readonly string[] FallbackStems =
    [
        "Muddle", "Wobble", "Snick", "Puddle", "Grumble", "Tadding", "Fiddle", "Dribble", "Whisk", "Bumble",
        "Crumple", "Nibble", "Shuffle", "Waddle", "Huffle",
    ];

    /// <param name="nearby">Real place names from the area, used as raw material.</param>
    /// <param name="forbidden">Names that must not be produced (every real name the bot knows of).</param>
    public static string Generate(IEnumerable<string> nearby, IEnumerable<string> forbidden, Random rng)
    {
        var banned = new HashSet<string>(forbidden.Select(Normalise));
        var stems = nearby
            .Where(n => !n.Contains(' ') && n.All(char.IsLetter) && n.Length is >= 5 and <= 13)
            .Select(Stem)
            .Where(s => s is { Length: >= 3 and <= 9 })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (stems.Count < 3) stems.AddRange(FallbackStems);

        for (var attempt = 0; attempt < 60; attempt++)
        {
            var core = Join(Pick(stems, rng), Pick(Endings, rng));
            var roll = rng.NextDouble();
            var name = roll switch
            {
                < 0.45 => core,
                < 0.70 => $"{Pick(Prefixes, rng)} {core}",
                _ => core + Pick(Suffixes, rng),
            };

            if (name.Length <= 30 && !banned.Contains(Normalise(name)) && !banned.Contains(Normalise(core)))
                return name;
        }

        return "Muddlecombe St Giles";
    }

    /// <summary>Removes a known ending: "Fakenham" → "Faken", "Wetwang" → "Wetwang".</summary>
    internal static string Stem(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var e in Endings)
        {
            if (lower.EndsWith(e, StringComparison.Ordinal) && name.Length - e.Length >= 3)
                return name[..^e.Length];
        }

        return name.Length > 7 ? name[..6] : name;
    }

    private static string Join(string stem, string ending)
    {
        stem = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant());
        // Avoid triple letters such as "Wellll".
        if (stem.Length >= 2 && stem[^1] == ending[0] && stem[^2] == ending[0]) stem = stem[..^1];
        return stem + ending;
    }

    private static T Pick<T>(IReadOnlyList<T> items, Random rng) => items[rng.Next(items.Count)];

    private static string Normalise(string s) =>
        string.Join(' ', s.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
