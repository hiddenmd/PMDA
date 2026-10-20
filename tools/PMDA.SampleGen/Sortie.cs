namespace PMDA.SampleGen;

/// <summary>A point of the route: where to go, at what altitude (m above sea level) and speed (m/s).</summary>
internal sealed record Waypoint(string Name, double Lat, double Lon, double AltM, double SpeedMs);

/// <summary>A fixed radar site: a made-up position and a radar family (0 early warning … 3 short range).</summary>
internal sealed record ThreatSite(double Lat, double Lon, int Family);

/// <summary>
/// A complete sortie from an airfield and back: takeoff roll on one runway heading, the route, then a straight-in
/// approach and landing on the reciprocal runway.
/// </summary>
internal sealed record SortiePlan(
    string Airfield,
    double FieldLat,
    double FieldLon,
    double FieldElevationM,
    double TakeoffHeadingDeg,
    double RunwayLengthM,
    DateTime StartUtc,
    Waypoint[] Route,
    ThreatSite[] Threats)
{
    public double LandingHeadingDeg => Geo.Normalize(TakeoffHeadingDeg + 180);

    /// <summary>Where the takeoff roll starts: the far end of the runway behind the airfield reference point.</summary>
    public (double Lat, double Lon) TakeoffPoint => Geo.Move(FieldLat, FieldLon, LandingHeadingDeg, RunwayLengthM / 2);

    /// <summary>Where landing aircraft touch down: the threshold of the reciprocal runway.</summary>
    public (double Lat, double Lon) LandingThreshold => Geo.Move(FieldLat, FieldLon, TakeoffHeadingDeg, RunwayLengthM / 2 - 300);

    /// <summary>
    /// A range training sortie from Queen Alia International (Jordan) into the eastern desert. Every threat site is
    /// invented and sits in empty desert, as the emitters of a training range would; none stands for a real
    /// air-defence position.
    /// </summary>
    public static readonly SortiePlan JordanRange = new(
        Airfield: "Queen Alia International (OJAI)",
        FieldLat: 31.7226,
        FieldLon: 35.9932,
        FieldElevationM: 722,
        TakeoffHeadingDeg: 84,      // runway 08, landing on 26
        RunwayLengthM: 3_660,
        StartUtc: new DateTime(2026, 3, 14, 6, 10, 0, DateTimeKind.Utc),
        Route:
        [
            new("DEP", 31.738, 36.150, 2_500, 170),
            new("CLIMB", 31.800, 36.450, 7_600, 230),
            new("TRANSIT", 31.950, 36.950, 7_600, 250),
            new("RANGE ENTRY", 32.020, 37.200, 1_300, 250),
            new("LL1", 31.980, 37.420, 1_150, 265),
            new("LL2", 32.120, 37.580, 1_100, 265),
            new("IP", 32.080, 37.800, 1_100, 270),
            new("POP", 32.160, 37.920, 2_500, 240),
            new("TARGET", 32.200, 38.000, 1_500, 260),
            new("EGRESS1", 32.320, 37.860, 1_200, 270),
            new("EGRESS2", 32.300, 37.500, 1_200, 265),
            new("CLIMBOUT", 32.180, 37.150, 6_100, 240),
            new("RTB", 31.950, 36.600, 6_100, 230),
            new("DESCENT", 31.780, 36.300, 2_500, 160),
            // Final approach fix: 15 km out on the runway 26 centreline (east of the field).
            new("FAF", 31.7363, 36.1504, 1_500, 85),
        ],
        Threats:
        [
            new(32.450, 37.300, 0), new(31.800, 38.050, 0),
            new(32.050, 37.550, 1), new(32.250, 37.700, 1), new(31.950, 37.850, 1), new(32.300, 38.100, 1),
            new(32.000, 37.500, 2), new(32.150, 37.660, 2), new(32.050, 37.950, 2), new(32.250, 37.820, 2), new(32.100, 38.100, 2),
            new(32.190, 38.020, 3), new(32.220, 37.970, 3), new(32.170, 37.960, 3),
        ]);
}

/// <summary>
/// Flies a <see cref="SortiePlan"/>: accelerates down the runway, rotates, climbs out straight ahead before
/// turning, follows the route with bank-limited turns (steeper at low level) and limited climb and descent rates,
/// then flies a 3° glide path to the threshold, lands and rolls out to taxi speed.
/// </summary>
internal sealed class SortieFlight(SortiePlan plan)
{
    private const double G = 9.81;
    private const double RotateSpeedMs = 82;
    private const double ArriveWithinM = 2_500;

    private enum Phase { TakeoffRoll, Airborne, Final, Rollout, Stopped }

    private Phase _phase = Phase.TakeoffRoll;
    private int _waypoint;
    private double _verticalSpeed;

    public double Lat { get; private set; } = plan.TakeoffPoint.Lat;
    public double Lon { get; private set; } = plan.TakeoffPoint.Lon;
    public double AltM { get; private set; } = plan.FieldElevationM;
    public double HeadingDeg { get; private set; } = plan.TakeoffHeadingDeg;
    public double SpeedMs { get; private set; }

    /// <summary>Advances the aircraft by <paramref name="dt"/> seconds; false once it has landed and stopped.</summary>
    public bool Step(double dt, Func<double, double> noise)
    {
        switch (_phase)
        {
            case Phase.TakeoffRoll:
                SpeedMs += 3.2 * dt;
                Move(dt);
                if (SpeedMs >= RotateSpeedMs)
                    _phase = Phase.Airborne;
                return true;

            case Phase.Airborne:
                FlyRoute(dt, noise);
                return true;

            case Phase.Final:
                FlyFinal(dt);
                return true;

            case Phase.Rollout:
                SpeedMs = Math.Max(0, SpeedMs - 2.4 * dt);
                Move(dt);
                if (SpeedMs < 8)
                    _phase = Phase.Stopped;
                return true;

            default:
                return false;
        }
    }

    private void FlyRoute(double dt, Func<double, double> noise)
    {
        var target = plan.Route[_waypoint];
        if (Geo.DistanceM(Lat, Lon, target.Lat, target.Lon) < ArriveWithinM)
        {
            if (++_waypoint == plan.Route.Length)
            {
                _phase = Phase.Final;
                return;
            }
            target = plan.Route[_waypoint];
        }

        var agl = AltM - plan.FieldElevationM;
        SpeedMs += Math.Clamp(target.SpeedMs - SpeedMs, -3 * dt, 2.5 * dt);

        // Wings level until 300 m above the field; then bank-limited turns, harder when low and fast.
        if (agl > 300)
        {
            var bankDeg = target.AltM < 2_000 && SpeedMs > 200 ? 60 : 30;
            var maxTurnDeg = G * Math.Tan(bankDeg * Math.PI / 180) / SpeedMs * 180 / Math.PI * dt;
            var wanted = Geo.Delta(HeadingDeg, Geo.BearingDeg(Lat, Lon, target.Lat, target.Lon));
            HeadingDeg = Geo.Normalize(HeadingDeg + Math.Clamp(wanted, -maxTurnDeg, maxTurnDeg));
        }

        // Climb or descend toward the waypoint's altitude; slow aircraft climb less steeply.
        var maxClimb = SpeedMs < 150 ? 12 : 22;
        var wantedVs = Math.Clamp((target.AltM - AltM) * 0.04, -30, maxClimb);
        _verticalSpeed += Math.Clamp(wantedVs - _verticalSpeed, -4 * dt, 4 * dt);
        AltM += _verticalSpeed * dt;
        // Low level over the desert: small terrain-following corrections.
        if (target.AltM < 2_000 && agl < 1_200)
            AltM += noise(0.6);

        Move(dt);
    }

    private void FlyFinal(double dt)
    {
        var (tLat, tLon) = plan.LandingThreshold;
        var distance = Geo.DistanceM(Lat, Lon, tLat, tLon);
        SpeedMs += Math.Clamp(72 - SpeedMs, -2 * dt, 1 * dt);

        // Track to the threshold, settling onto the runway heading as it nears.
        var toThreshold = Geo.BearingDeg(Lat, Lon, tLat, tLon);
        var wanted = Geo.Delta(HeadingDeg, toThreshold);
        HeadingDeg = Geo.Normalize(HeadingDeg + Math.Clamp(wanted, -2 * dt, 2 * dt));

        // 3° glide path; flare over the last few hundred metres.
        var glide = plan.FieldElevationM + Math.Max(0, distance - 300) * Math.Tan(3 * Math.PI / 180);
        AltM += Math.Clamp(glide - AltM, -8 * dt, 3 * dt);
        Move(dt);

        if (distance < 150 || AltM <= plan.FieldElevationM + 0.5)
        {
            AltM = plan.FieldElevationM;
            HeadingDeg = plan.LandingHeadingDeg;
            _phase = Phase.Rollout;
        }
    }

    private void Move(double dt) => (Lat, Lon) = Geo.Move(Lat, Lon, HeadingDeg, SpeedMs * dt);
}
