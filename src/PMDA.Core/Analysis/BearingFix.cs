namespace PMDA.Core.Analysis;

/// <summary>One RWR bearing: where the aircraft was and the true bearing (degrees, clockwise from north) to the emitter.</summary>
public readonly record struct BearingObservation(double Lat, double Lon, double TrueBearingDeg);

/// <summary>
/// An estimated emitter position with its 95% uncertainty ellipse (semi-axes in metres; <see cref="AzimuthDeg"/> is the
/// major axis direction clockwise from north, 0–180), or an unresolved result with a <see cref="Reason"/>.
/// </summary>
public sealed record BearingFixResult(
    bool Resolved, double? Lat, double? Lon, double MajorM, double MinorM, double AzimuthDeg,
    int Count, double SpreadDeg, double RmsResidualDeg, string? Reason);

/// <summary>
/// Bearing-only least-squares fix for a fixed emitter, in a local tangent plane (east, north) around the observations.
/// Each bearing is a line through the observer; the fix minimises range-weighted squared perpendicular distances to
/// those lines (weight 1/r², so residuals are angular), then the angular residuals give σθ and the covariance
/// σθ²·(Σ w n nᵀ)⁻¹ gives the ellipse.
/// </summary>
public static class BearingFix
{
    public const int MinObservations = 12;
    public const double MinSpreadDeg = 10;
    public const double MaxConditionNumber = 1e6;
    public const double MaxRmsResidualDeg = 6;
    public const double MaxMajorM = 50_000;
    public const double RangeFloorM = 1_000;
    public const int WeightedIterations = 3;
    public const double Chi2Two95 = 5.991;
    public const int RefineIterations = 5;

    private const double EarthRadiusM = 6_371_000;
    private const double Rad = Math.PI / 180;

    public static BearingFixResult Solve(IReadOnlyList<BearingObservation> observations)
    {
        var n = observations.Count;
        if (n < MinObservations)
            return Unresolved(n, "Too few bearings");

        var lat0 = observations.Average(o => o.Lat);
        var lon0 = observations.Average(o => o.Lon);
        var kx = Rad * EarthRadiusM * Math.Cos(lat0 * Rad);
        var ky = Rad * EarthRadiusM;
        var p = observations.Select(o => (X: (o.Lon - lon0) * kx, Y: (o.Lat - lat0) * ky, T: o.TrueBearingDeg * Rad)).ToArray();

        double sx = 0, sy = 0, a11 = 0, a12 = 0, a22 = 0;
        for (var iteration = 0; iteration <= WeightedIterations; iteration++)
        {
            a11 = a12 = a22 = 0;
            double b1 = 0, b2 = 0;
            foreach (var o in p)
            {
                // Normal to the bearing line: the bearing direction is (sin θ, cos θ) in (east, north).
                var nx = Math.Cos(o.T);
                var ny = -Math.Sin(o.T);
                var w = 1.0;
                if (iteration > 0)
                {
                    var r = Math.Max(RangeFloorM, Math.Sqrt((sx - o.X) * (sx - o.X) + (sy - o.Y) * (sy - o.Y)));
                    w = 1 / (r * r);
                }
                var d = nx * o.X + ny * o.Y;
                a11 += w * nx * nx;
                a12 += w * nx * ny;
                a22 += w * ny * ny;
                b1 += w * nx * d;
                b2 += w * ny * d;
            }
            var det = a11 * a22 - a12 * a12;
            var half = (a11 + a22) / 2;
            var root = Math.Sqrt(Math.Max(0, half * half - det));
            var (l1, l2) = (half + root, half - root);
            if (l2 <= 0 || l1 / l2 > MaxConditionNumber)
                return Unresolved(n, "Too little angle change");
            sx = (a22 * b1 - a12 * b2) / det;
            sy = (a11 * b2 - a12 * b1) / det;
        }

        // The tangent plane ignores meridian convergence and great-circle curvature (about 100 m over 60 km), so polish
        // the fix with Gauss-Newton on the true spherical bearings, minimising the squared angular residuals.
        for (var iteration = 0; iteration < RefineIterations; iteration++)
        {
            double h11 = 0, h12 = 0, h22 = 0, g1 = 0, g2 = 0;
            var fixLat = lat0 + sy / ky;
            var fixLon = lon0 + sx / kx;
            for (var i = 0; i < n; i++)
            {
                var dx = sx - p[i].X;
                var dy = sy - p[i].Y;
                var r2 = Math.Max(RangeFloorM * RangeFloorM, dx * dx + dy * dy);
                var e = Wrap(p[i].T - GreatCircleBearing(observations[i].Lat, observations[i].Lon, fixLat, fixLon));
                double j1 = dy / r2, j2 = -dx / r2; // d(bearing)/d(east, north) of the fix
                h11 += j1 * j1; h12 += j1 * j2; h22 += j2 * j2;
                g1 += j1 * e; g2 += j2 * e;
            }
            var hDet = h11 * h22 - h12 * h12;
            if (hDet <= 0) break;
            sx += (h22 * g1 - h12 * g2) / hDet;
            sy += (h11 * g2 - h12 * g1) / hDet;
        }

        // Angular residuals (spherical bearing from each observer to the fix vs the measured bearing).
        var sumSq = 0.0;
        var angles = new double[n];
        var finalLat = lat0 + sy / ky;
        var finalLon = lon0 + sx / kx;
        for (var i = 0; i < n; i++)
        {
            var predicted = GreatCircleBearing(observations[i].Lat, observations[i].Lon, finalLat, finalLon);
            var delta = Wrap(predicted - p[i].T);
            sumSq += delta * delta;
            angles[i] = Math.Atan2(p[i].X - sx, p[i].Y - sy); // direction from the fix to the observer
        }
        var sigma = Math.Sqrt(sumSq / Math.Max(1, n - 2));
        var rmsDeg = Math.Sqrt(sumSq / n) / Rad;
        var spreadDeg = Spread(angles) / Rad;

        if (rmsDeg > MaxRmsResidualDeg)
            return Unresolved(n, "Bearings inconsistent (moving emitter?)", spreadDeg, rmsDeg);
        if (spreadDeg < MinSpreadDeg)
            return Unresolved(n, "Too little angle change", spreadDeg, rmsDeg);

        // Covariance C = σ²·A⁻¹ (A is the final weighted normal matrix), then its eigen-decomposition.
        var detA = a11 * a22 - a12 * a12;
        var s2 = sigma * sigma / detA;
        double c11 = s2 * a22, c22 = s2 * a11, c12 = -s2 * a12;
        var mid = (c11 + c22) / 2;
        var rad = Math.Sqrt(Math.Max(0, (c11 - c22) * (c11 - c22) / 4 + c12 * c12));
        var major = Math.Sqrt(Chi2Two95 * (mid + rad));
        var minor = Math.Sqrt(Chi2Two95 * Math.Max(0, mid - rad));
        var phi = 0.5 * Math.Atan2(2 * c12, c11 - c22); // major axis from +east, counter-clockwise
        var azimuth = ((90 - phi / Rad) % 180 + 180) % 180;

        if (major > MaxMajorM)
            return Unresolved(n, "Too uncertain", spreadDeg, rmsDeg);

        return new BearingFixResult(true, lat0 + sy / ky, lon0 + sx / kx, major, minor, azimuth, n, spreadDeg, rmsDeg, null);
    }

    private static BearingFixResult Unresolved(int n, string reason, double spreadDeg = 0, double rmsDeg = 0) =>
        new(false, null, null, 0, 0, 0, n, spreadDeg, rmsDeg, reason);

    /// <summary>Initial great-circle bearing (radians, clockwise from north) from point 1 to point 2.</summary>
    private static double GreatCircleBearing(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        double f1 = lat1Deg * Rad, f2 = lat2Deg * Rad, dl = (lon2Deg - lon1Deg) * Rad;
        return Math.Atan2(Math.Sin(dl) * Math.Cos(f2), Math.Cos(f1) * Math.Sin(f2) - Math.Sin(f1) * Math.Cos(f2) * Math.Cos(dl));
    }

    /// <summary>Angle wrapped to (−π, π].</summary>
    private static double Wrap(double a)
    {
        a %= 2 * Math.PI;
        if (a <= -Math.PI) a += 2 * Math.PI;
        if (a > Math.PI) a -= 2 * Math.PI;
        return a;
    }

    /// <summary>The smallest arc (radians) containing all directions: 2π minus the largest gap between neighbours.</summary>
    private static double Spread(double[] angles)
    {
        var sorted = angles.Select(a => (a + 2 * Math.PI) % (2 * Math.PI)).Order().ToArray();
        var largestGap = 2 * Math.PI - (sorted[^1] - sorted[0]);
        for (var i = 1; i < sorted.Length; i++)
            largestGap = Math.Max(largestGap, sorted[i] - sorted[i - 1]);
        return 2 * Math.PI - largestGap;
    }
}
