using BluffOrBlighty.Api.Bots;
using BluffOrBlighty.Api.Game;

namespace BluffOrBlighty.Tests;

public class WorldMapTests
{
    // Real Kerala backwater villages, as labelled in the zoom-12 tiles.
    private static readonly TilePlace[] Kerala =
    [
        new("Kumarakom", "village", 9.6170, 76.4300),
        new("Kainakary", "village", 9.4980, 76.3780),
        new("Muhamma", "village", 9.6070, 76.3600),
        new("Chennamkary", "hamlet", 9.5090, 76.4070),
        new("Mannancherry", "village", 9.5750, 76.3650),
        new("Komalapuram", "village", 9.5500, 76.3500),
        new("Aryad", "village", 9.5300, 76.3500),
    ];

    [Fact]
    public void Asia_and_africa_maps_are_grouped_by_continent_and_uk_maps_by_country()
    {
        var groups = MapPresets.All.Select(m => m.FilterGroup).Distinct().ToList();
        Assert.Contains("Asia", groups);
        Assert.Contains("Africa", groups);
        Assert.Contains("England", groups);
        Assert.All(MapPresets.All.Where(m => m.Group is not null), m => Assert.False(m.IsUk));
        Assert.Equal(4, MapPresets.All.Count(m => m.Group == "Asia"));
        Assert.Equal(4, MapPresets.All.Count(m => m.Group == "Africa"));
    }

    [Fact]
    public void Spliced_fakes_are_made_of_local_parts_and_are_not_real()
    {
        var real = Kerala.Select(p => p.Name).ToArray();
        for (var seed = 0; seed < 200; seed++)
        {
            var name = FakeNames.Generate(real, real, new Random(seed), FakeStyle.Splice);
            Assert.DoesNotContain(real, r => r.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.InRange(name.Length, 4, 30);
            // No English endings or prefixes bolted on.
            Assert.DoesNotContain("-on-", name);
            Assert.False(name.StartsWith("Great ") || name.StartsWith("Little "), name);
        }
    }

    [Fact]
    public void Bot_can_submit_on_a_non_uk_map()
    {
        var map = MapPresets.Get("kerala-backwaters");
        var s = BotBrain.ChooseSubmission(Kerala, map, new Random(3), style: FakeStyle.Splice);
        Assert.NotNull(s);

        var game = new GameSession("TEST", new Random(1), TimeProvider.System);
        var host = game.AddPlayer("Alice", "c1");
        game.SetMap(host.PlayerId, map.Id);
        var bot = game.AddBot(host.PlayerId, BotLevel.Normal);
        game.Start(host.PlayerId);
        game.Submit(bot, s!); // passes the game's rules
    }

    [Fact]
    public void Map_labels_use_the_latin_name_for_other_scripts()
    {
        var japanese = new NetTopologySuite.Features.AttributesTable
        {
            { "name", "大滝村" }, { "name:latin", "Otaki" }, { "name:nonlatin", "大滝村" },
        };
        var english = new NetTopologySuite.Features.AttributesTable { { "name", "Great Snoring" } };
        var withEn = new NetTopologySuite.Features.AttributesTable { { "name", "Llanfair" }, { "name_en", "Llanfair PG" } };

        Assert.Equal("Otaki", VectorTilePlaceSource.DisplayName(japanese));
        Assert.Equal("Great Snoring", VectorTilePlaceSource.DisplayName(english));
        Assert.Equal("Llanfair PG", VectorTilePlaceSource.DisplayName(withEn));
    }
}
