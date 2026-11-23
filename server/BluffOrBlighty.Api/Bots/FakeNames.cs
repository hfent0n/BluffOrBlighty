using System.Globalization;
using BluffOrBlighty.Api.Game;

namespace BluffOrBlighty.Api.Bots;

/// <summary>
/// Makes up plausible British place names, in three styles (see <see cref="FakeStyle"/>):
/// <list type="bullet">
/// <item><b>Obvious</b>: silly stems with loud suffixes. Fun, and easy to spot.</item>
/// <item><b>Local</b>: grafts the stems of nearby real names onto common English endings.</item>
/// <item><b>Crafty</b>: uses only the endings, prefixes and suffixes the area itself uses.</item>
/// </list>
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

    private static readonly string[] SuffixWords = ["Green", "End", "Common", "Bottom", "Tye", "Magna", "Parva", "Street", "Heath"];

    private static readonly string[] SillyStems =
    [
        "Muddle", "Wobble", "Snick", "Puddle", "Grumble", "Tadding", "Fiddle", "Dribble", "Whisk", "Bumble",
        "Crumple", "Nibble", "Shuffle", "Waddle", "Huffle", "Plonk", "Snorkel", "Giggle", "Trundle", "Pickle",
    ];

    private static readonly string[] LoudSuffixes = ["-on-Sea", "-in-the-Marsh", "-under-Edge", " Magna", " Parva", "-cum-Soggington"];

    /// <param name="nearby">Real place names from the area, used as raw material.</param>
    /// <param name="forbidden">Names that must not be produced (every real name the bot knows of).</param>
    public static string Generate(
        IEnumerable<string> nearby, IEnumerable<string> forbidden, Random rng, FakeStyle style = FakeStyle.Local)
    {
        var banned = new HashSet<string>(forbidden.Select(Normalise));
        var names = nearby.ToList();

        for (var attempt = 0; attempt < 60; attempt++)
        {
            var (name, core) = style switch
            {
                FakeStyle.Obvious => Obvious(rng),
                FakeStyle.Crafty => Crafty(names, rng) ?? Local(names, rng),
                _ => Local(names, rng),
            };

            if (name.Length <= 30 && !banned.Contains(Normalise(name)) && !banned.Contains(Normalise(core)))
                return name;
        }

        return "Muddlecombe St Giles";
    }

    private static (string Name, string Core) Obvious(Random rng)
    {
        var core = Join(Pick(SillyStems, rng), Pick(Endings, rng));
        return (rng.NextDouble() < 0.6 ? core + Pick(LoudSuffixes, rng) : core, core);
    }

    private static (string Name, string Core) Local(IReadOnlyList<string> nearby, Random rng)
    {
        var stems = Stems(nearby);
        if (stems.Count < 3) stems.AddRange(SillyStems);

        var core = Join(Pick(stems, rng), Pick(Endings, rng));
        var name = rng.NextDouble() switch
        {
            < 0.45 => core,
            < 0.70 => $"{Pick(Prefixes, rng)} {core}",
            _ => core + Pick(Suffixes, rng),
        };
        return (name, core);
    }

    /// <summary>Only uses patterns the area's real names use. Returns null if there's too little to go on.</summary>
    private static (string Name, string Core)? Crafty(IReadOnlyList<string> nearby, Random rng)
    {
        var stems = Stems(nearby);
        var words = nearby.Select(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();

        // Endings as often as they occur locally, so a "-thorpe" area gets "-thorpe" fakes.
        var localEndings = words
            .Select(w => w[^1].ToLowerInvariant())
            .Select(last => Endings.FirstOrDefault(e => last.EndsWith(e, StringComparison.Ordinal) && last.Length > e.Length + 2))
            .OfType<string>()
            .ToList();
        if (stems.Count < 3 || localEndings.Count < 3) return null;

        var localPrefixes = words.Where(w => w.Length > 1 && Prefixes.Contains(w[0])).Select(w => w[0]).ToList();
        var localSuffixes = words.Where(w => w.Length > 1 && SuffixWords.Contains(w[^1])).Select(w => w[^1]).ToList();

        var core = Join(Pick(stems, rng), Pick(localEndings, rng));
        var roll = rng.NextDouble();
        var name = roll < 0.25 && localPrefixes.Count > 0 ? $"{Pick(localPrefixes, rng)} {core}"
            : roll < 0.40 && localSuffixes.Count > 0 ? $"{core} {Pick(localSuffixes, rng)}"
            : core;
        return (name, core);
    }

    private static List<string> Stems(IEnumerable<string> nearby) =>
        nearby
            .Where(n => !n.Contains(' ') && n.All(char.IsLetter) && n.Length is >= 5 and <= 13)
            .Select(Stem)
            .Where(s => s is { Length: >= 3 and <= 9 })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
