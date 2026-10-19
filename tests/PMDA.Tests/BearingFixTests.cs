using PMDA.Core.Analysis;

namespace PMDA.Tests;

public sealed class BearingFixTests
{
    private const double R = 6_371_000, Rad = Math.PI / 180;

    private static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double φ1 = lat1 * Rad, φ2 = lat2 * Rad, Δλ = (lon2 - lon1) * Rad;
        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        return (Math.Atan2(y, x) / Rad + 360) % 360;
    }

    private static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        double dφ = (lat2 - lat1) * Rad, dλ = (lon2 - lon1) * Rad;
        var a = Math.Sin(dφ / 2) * Math.Sin(dφ / 2) + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Sin(dλ / 2) * Math.Sin(dλ / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>Observer flying east along 44.3° N from 25.8° E to 26.6° E (about 64 km).</summary>
    private static List<BearingObservation> Observe(double siteLat, double siteLon, int count, double noiseDeg = 0, int seed = 1)
    {
        var random = new Random(seed);
        var list = new List<BearingObservation>();
        for (var i = 0; i < count; i++)
        {
            var lon = 25.8 + 0.8 * i / (count - 1);
            var noise = noiseDeg * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            list.Add(new BearingObservation(44.3, lon, (BearingDeg(44.3, lon, siteLat, siteLon) + noise + 360) % 360));
        }
        return list;
    }

    private static bool InsideEllipse(BearingFixResult f, double lat, double lon)
    {
        var east = (lon - f.Lon!.Value) * Rad * R * Math.Cos(f.Lat!.Value * Rad);
        var north = (lat - f.Lat.Value) * Rad * R;
        var az = f.AzimuthDeg * Rad; // major axis direction, clockwise from north
        var along = east * Math.Sin(az) + north * Math.Cos(az);
        var across = east * Math.Cos(az) - north * Math.Sin(az);
        return along * along / (f.MajorM * f.MajorM) + across * across / (f.MinorM * f.MinorM) <= 1;
    }

    [Fact]
    public void Exact_bearings_fix_the_site_to_within_a_few_metres()
    {
        var fix = BearingFix.Solve(Observe(44.5, 26.2, 60));
        Assert.True(fix.Resolved, fix.Reason);
        Assert.True(DistanceM(fix.Lat!.Value, fix.Lon!.Value, 44.5, 26.2) < 5);
        Assert.Equal(60, fix.Count);
        Assert.True(fix.SpreadDeg > 30);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Noisy_bearings_fix_within_a_kilometre_and_inside_the_ellipse(int seed)
    {
        var fix = BearingFix.Solve(Observe(44.5, 26.2, 200, noiseDeg: 1.5, seed: seed));
        Assert.True(fix.Resolved, fix.Reason);
        Assert.True(DistanceM(fix.Lat!.Value, fix.Lon!.Value, 44.5, 26.2) < 1_000);
        Assert.True(InsideEllipse(fix, 44.5, 26.2));
        Assert.InRange(fix.RmsResidualDeg, 0.8, 2.5);
        Assert.True(fix.MajorM >= fix.MinorM && fix.MinorM > 0);
        Assert.InRange(fix.AzimuthDeg, 0, 180);
    }

    [Fact]
    public void Too_few_bearings_is_unresolved()
    {
        var fix = BearingFix.Solve(Observe(44.5, 26.2, 5));
        Assert.False(fix.Resolved);
        Assert.Equal("Too few bearings", fix.Reason);
        Assert.Null(fix.Lat);
    }

    [Fact]
    public void Flying_straight_at_the_site_gives_no_angle_change()
    {
        // Site due east on the flight line: every bearing is 90°.
        var obs = Enumerable.Range(0, 40).Select(i => new BearingObservation(44.3, 25.8 + 0.01 * i, 90)).ToList();
        var fix = BearingFix.Solve(obs);
        Assert.False(fix.Resolved);
        Assert.Equal("Too little angle change", fix.Reason);
    }

    [Fact]
    public void A_site_that_jumps_is_unresolved()
    {
        var first = Observe(44.5, 26.0, 50);
        var second = Observe(44.45, 26.5, 50);
        var mixed = first.Take(25).Concat(second.Skip(25)).ToList();
        var fix = BearingFix.Solve(mixed);
        Assert.False(fix.Resolved);
        Assert.NotNull(fix.Reason);
    }

    [Fact]
    public void A_far_site_seen_over_a_short_leg_is_too_uncertain_or_too_little_angle()
    {
        // 600 km away, 64 km leg: a few degrees of angle change at most.
        var fix = BearingFix.Solve(Observe(49.8, 26.2, 100, noiseDeg: 1.5));
        Assert.False(fix.Resolved);
        Assert.Contains(fix.Reason, new[] { "Too uncertain", "Too little angle change" });
    }
}
