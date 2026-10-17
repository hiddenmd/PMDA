namespace PMDA.SampleGen;

/// <summary>Great-circle helpers on a spherical Earth; plenty for synthetic data.</summary>
internal static class Geo
{
    private const double EarthRadiusM = 6_371_000;

    public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>Initial true bearing from point 1 to point 2, degrees 0–360.</summary>
    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        var y = Math.Sin(Rad(lon2 - lon1)) * Math.Cos(Rad(lat2));
        var x = Math.Cos(Rad(lat1)) * Math.Sin(Rad(lat2)) - Math.Sin(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Cos(Rad(lon2 - lon1));
        return Normalize(Deg(Math.Atan2(y, x)));
    }

    public static (double Lat, double Lon) Move(double lat, double lon, double bearingDeg, double distanceM)
    {
        var d = distanceM / EarthRadiusM;
        var b = Rad(bearingDeg);
        var lat2 = Math.Asin(Math.Sin(Rad(lat)) * Math.Cos(d) + Math.Cos(Rad(lat)) * Math.Sin(d) * Math.Cos(b));
        var lon2 = Rad(lon) + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(Rad(lat)), Math.Cos(d) - Math.Sin(Rad(lat)) * Math.Sin(lat2));
        return (Deg(lat2), Deg(lon2));
    }

    public static double Normalize(double deg) => ((deg % 360) + 360) % 360;

    /// <summary>Signed smallest difference to - from, in (-180, 180].</summary>
    public static double Delta(double from, double to)
    {
        var d = Normalize(to - from);
        return d > 180 ? d - 360 : d;
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
    private static double Deg(double rad) => rad * 180 / Math.PI;
}
