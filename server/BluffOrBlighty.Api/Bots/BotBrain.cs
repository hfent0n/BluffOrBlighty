using BluffOrBlighty.Api.Game;

namespace BluffOrBlighty.Api.Bots;

/// <summary>The bot's decisions, as pure functions so they're easy to test.</summary>
public static class BotBrain
{
    private static readonly HashSet<string> PickableClasses = ["town", "village", "hamlet"];

    /// <summary>
    /// Map style layer for a place class, so the guess map draws bot picks like the
    /// surrounding labels. These are the OpenFreeMap "liberty" layer ids; other styles fall back.
    /// </summary>
    public static string LayerFor(string placeClass) => placeClass switch
    {
        "town" => "label_town",
        "village" => "label_village",
        _ => "label_other",
    };

    /// <summary>Picks 3 nearby real places and renames one. Returns null if this area has too few places.</summary>
    public static SubmissionRequest? ChooseSubmission(
        IReadOnlyList<TilePlace> places, MapPreset map, Random rng,
        double? maxSpreadKm = null,
        FakeStyle style = FakeStyle.Local)
    {
        var candidates = places
            .Where(p => PickableClasses.Contains(p.Class) && MapPresets.Contains(map, p.Lat, p.Lng))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(_ => rng.Next())
            .ToList();

        // The bot keeps its three within a few km of each other so its round fits on one
        // screen, or tighter if the host has set a smaller limit.
        var spread = Math.Min(maxSpreadKm ?? 8, 8);
        static bool Ok(TilePlace a, TilePlace b, double max)
        {
            var d = Geo.DistanceKm(a.Lat, a.Lng, b.Lat, b.Lng);
            return d >= 0.5 && d <= max;
        }

        foreach (var a in candidates)
        {
            var near = candidates.Where(b => b != a && Ok(a, b, spread)).ToList();
            for (var i = 0; i < near.Count; i++)
            for (var j = i + 1; j < near.Count; j++)
            {
                if (!Ok(near[i], near[j], spread)) continue;

                var three = new[] { a, near[i], near[j] };
                // Prefer renaming a village or hamlet: a fake town would stand out.
                var renameable = three.Where(p => p.Class != "town").ToArray();
                var renamed = renameable.Length > 0 ? renameable[rng.Next(renameable.Length)] : three[rng.Next(3)];
                var real = three.Where(p => p != renamed).ToArray();

                var fake = FakeNames.Generate(
                    nearby: places.Select(p => p.Name),
                    forbidden: places.Select(p => p.Name),
                    rng,
                    style);

                return new SubmissionRequest(
                    [ToPick(real[0]), ToPick(real[1])],
                    ToPick(renamed),
                    fake);
            }
        }

        return null;
    }

    /// <summary>
    /// Picks which option is fake. <paramref name="foundOnMap"/>[i] says whether option i was
    /// found among the map's place labels at its location. With probability
    /// <paramref name="knowledge"/> the bot uses that; otherwise it guesses, so accuracy is
    /// roughly k + (1 - k) / 3.
    /// </summary>
    public static int ChooseVote(IReadOnlyList<bool> foundOnMap, double knowledge, Random rng)
    {
        var count = foundOnMap.Count;
        if (rng.NextDouble() < knowledge)
        {
            var unknown = Enumerable.Range(0, count).Where(i => !foundOnMap[i]).ToArray();
            if (unknown.Length > 0) return unknown[rng.Next(unknown.Length)];
        }

        return rng.Next(count);
    }

    /// <summary>True if a place with this name sits within half a kilometre of the point.</summary>
    public static bool IsOnMap(string label, double lat, double lng, IEnumerable<TilePlace> places) =>
        places.Any(p => string.Equals(p.Name, label, StringComparison.OrdinalIgnoreCase) &&
                        Geo.DistanceKm(p.Lat, p.Lng, lat, lng) <= 0.5);

    private static PlacePick ToPick(TilePlace p) => new(p.Name, p.Lat, p.Lng, LayerFor(p.Class));
}
