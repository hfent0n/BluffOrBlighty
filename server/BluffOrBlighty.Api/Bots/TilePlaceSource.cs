using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NetTopologySuite.IO.VectorTiles.Mapbox;

namespace BluffOrBlighty.Api.Bots;

/// <summary>A settlement label from the map tiles. Lat/Lng is the label point the players' map uses.</summary>
public sealed record TilePlace(string Name, string Class, double Lat, double Lng);

public readonly record struct TileId(int X, int Y);

/// <summary>Where bots learn which places exist. Implemented over the same vector tiles players see.</summary>
public interface IPlaceSource
{
    /// <summary>Zoom level of the tiles. At 12 a tile is about 6 km across in the UK, and hamlets are included.</summary>
    const int Zoom = 12;

    Task<IReadOnlyList<TilePlace>> PlacesInTileAsync(TileId tile, CancellationToken ct = default);
}

public sealed class BotOptions
{
    /// <summary>TileJSON for an OpenMapTiles-schema source. Should match the client's map style.</summary>
    public string TileJsonUrl { get; set; } = "https://tiles.openfreemap.org/planet";
}

public static class TileMath
{
    /// <summary>The tile containing a point, plus where in that tile it falls (0..1 on each axis).</summary>
    public static (TileId Tile, double Fx, double Fy) Locate(double lat, double lng, int zoom = IPlaceSource.Zoom)
    {
        var n = 1 << zoom;
        var x = (lng + 180.0) / 360.0 * n;
        var latRad = lat * Math.PI / 180.0;
        var y = (1 - Math.Log(Math.Tan(latRad) + 1 / Math.Cos(latRad)) / Math.PI) / 2 * n;
        return (new TileId((int)x, (int)y), x - Math.Floor(x), y - Math.Floor(y));
    }

    /// <summary>The tile containing the point and its 3 neighbours nearest the point (a 2×2 block).</summary>
    public static TileId[] BlockAround(double lat, double lng)
    {
        var (t, fx, fy) = Locate(lat, lng);
        var dx = fx < 0.5 ? -1 : 1;
        var dy = fy < 0.5 ? -1 : 1;
        return [t, t with { X = t.X + dx }, t with { Y = t.Y + dy }, new TileId(t.X + dx, t.Y + dy)];
    }
}

/// <summary>Reads the "place" layer from OpenMapTiles vector tiles, with caching.</summary>
public sealed class VectorTilePlaceSource(HttpClient http, IMemoryCache cache, IOptions<BotOptions> options)
    : IPlaceSource
{
    private static readonly HashSet<string> SettlementClasses =
        ["city", "town", "village", "hamlet", "isolated_dwelling", "locality", "suburb", "quarter", "neighbourhood"];

    public async Task<IReadOnlyList<TilePlace>> PlacesInTileAsync(TileId tile, CancellationToken ct = default)
    {
        var key = $"tile:{IPlaceSource.Zoom}/{tile.X}/{tile.Y}";
        if (cache.TryGetValue(key, out IReadOnlyList<TilePlace>? cached) && cached is not null) return cached;

        var template = await TileUrlTemplateAsync(ct);
        var url = template.Replace("{z}", IPlaceSource.Zoom.ToString())
            .Replace("{x}", tile.X.ToString())
            .Replace("{y}", tile.Y.ToString());
        var bytes = await http.GetByteArrayAsync(url, ct);
        var places = Parse(bytes, tile);
        cache.Set(key, places, TimeSpan.FromHours(6));
        return places;
    }

    private async Task<string> TileUrlTemplateAsync(CancellationToken ct)
    {
        return await cache.GetOrCreateAsync("tilejson-template", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            using var doc = JsonDocument.Parse(await http.GetStringAsync(options.Value.TileJsonUrl, ct));
            return doc.RootElement.GetProperty("tiles")[0].GetString()
                   ?? throw new InvalidOperationException("TileJSON has no tile URL.");
        }) ?? throw new InvalidOperationException("TileJSON has no tile URL.");
    }

    internal static IReadOnlyList<TilePlace> Parse(byte[] bytes, TileId tile)
    {
        // Some servers send gzipped tiles without a Content-Encoding header.
        Stream stream = new MemoryStream(bytes);
        if (bytes is [0x1f, 0x8b, ..]) stream = new GZipStream(stream, CompressionMode.Decompress);

        using (stream)
        {
            var vt = new MapboxTileReader().Read(stream,
                new NetTopologySuite.IO.VectorTiles.Tiles.Tile(tile.X, tile.Y, IPlaceSource.Zoom));
            var layer = vt.Layers.FirstOrDefault(l => l.Name == "place");
            if (layer is null) return [];

            var result = new List<TilePlace>();
            foreach (var f in layer.Features)
            {
                if (f.Geometry is not NetTopologySuite.Geometries.Point p) continue;
                var cls = f.Attributes.Exists("class") ? f.Attributes["class"] as string : null;
                if (cls is null || !SettlementClasses.Contains(cls)) continue;
                // Same name the client's picker uses (name_en, falling back to name).
                var name = (f.Attributes.Exists("name_en") ? f.Attributes["name_en"] as string : null) is { Length: > 0 } en
                    ? en
                    : f.Attributes.Exists("name") ? f.Attributes["name"] as string : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new TilePlace(name, cls, p.Y, p.X));
            }

            return result;
        }
    }
}
