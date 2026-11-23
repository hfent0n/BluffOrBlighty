namespace BluffOrBlighty.Api.Game;

public static class Geo
{
    private const double EarthRadiusKm = 6371.0;

    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double deg) => deg * Math.PI / 180.0;

        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static double DistanceKm(PlacePick a, PlacePick b) => DistanceKm(a.Lat, a.Lng, b.Lat, b.Lng);
}
