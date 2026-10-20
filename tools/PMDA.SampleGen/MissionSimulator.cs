using PMDA.Core.Events;
using PMDA.Parsers.Sample;

namespace PMDA.SampleGen;

/// <summary>
/// Simulates a sortie and writes it in the sample format: an aircraft flies a route over ground
/// radars; each radar has a fixed site, parameters, an on/off schedule and a scan pattern. The RWR
/// sees a radar while it is on, in range and illuminating the aircraft; bearing (relative to the
/// aircraft heading) and amplitude follow from the geometry. A tracking radar (not early warning)
/// that gets close enough locks on (continuous illumination) and the aircraft dispenses chaff. Fire-control radars sometimes
/// launch a missile while locked; the missile warner then calls it and the aircraft dispenses
/// flares. Aural alerts are recorded as they would sound in the cockpit, and countermeasures run
/// out: there is a fixed load of chaff and flares.
/// </summary>
internal sealed class MissionSimulator(Scenario scenario)
{
    private const double TickSeconds = 0.1;          // navigation at 10 Hz
    private const double AircraftSpeedMs = 230;      // ~450 kt
    private const double MaxTurnDegPerSec = 3;       // standard-rate turn
    private const double CenterLat = 44.43, CenterLon = 26.10;
    private const double AreaRadiusM = 120_000;
    private const double NewThreatGapS = 10;          // unseen this long, a radar is "new" again
    private const int ChaffLoad = 30, FlareLoad = 14; // the SAMPLE symbology profile's load

    private readonly Random _random = new(scenario.Seed);

    public sealed record Result(long Records, int Corrupt, long Bytes);

    public Result Write(string path)
    {
        // A sortie flies a planned route from an airfield past fixed sites; otherwise laps of a random area.
        var plan = scenario.Sortie;
        var start = scenario.StartUtc ?? plan?.StartUtc ?? new DateTime(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc).AddMinutes(scenario.Seed % 120);
        var emitters = plan is null
            ? Enumerable.Range(1, scenario.Emitters).Select(id => CreateEmitter(id)).ToArray()
            : plan.Threats.Select((site, i) => CreateEmitter(i + 1, site)).ToArray();
        var route = plan is null ? CreateRoute() : null;
        var sortie = plan is null ? null : new SortieFlight(plan);

        var ticks = (long)(scenario.Duration.TotalSeconds / TickSeconds);
        var emitterEvery = Math.Max(1, (int)Math.Round(1 / (scenario.EmitterRateHz * TickSeconds)));
        var subTicks = Math.Max(1, (int)Math.Round(scenario.EmitterRateHz * TickSeconds));
        var corruptEvery = scenario.CorruptRecords > 0 ? Math.Max(1, ticks / scenario.CorruptRecords) : long.MaxValue;

        long records = 0;
        var corrupt = 0;
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            var w = new SampleFormatWriter(file);
            w.WriteHeader(start, scenario.Tail, scenario.Mission);

            var (lat, lon) = route?[0] ?? (sortie!.Lat, sortie.Lon);
            var heading = route is null ? sortie!.HeadingDeg : Geo.BearingDeg(lat, lon, route[1].Lat, route[1].Lon);
            var waypoint = 1;
            var alt = sortie?.AltM ?? 3_000.0;
            var nextChaff = 0.0;
            var chaff = new Store(1, ChaffLoad, AlertKind.ChaffLow, AlertKind.ChaffOut);
            var flares = new Store(2, FlareLoad, AlertKind.FlaresLow, AlertKind.FlaresOut);
            var missiles = new List<(double At, Emitter Shooter)>();

            for (long tick = 0; tick < ticks; tick++)
            {
                var t = tick * TickSeconds;
                var offset = TimeSpan.FromSeconds(t);

                if (sortie is not null)
                {
                    // The recording ends once the aircraft has landed and slowed to taxi speed.
                    if (!sortie.Step(TickSeconds, Noise))
                        break;
                    (lat, lon, alt, heading) = (sortie.Lat, sortie.Lon, sortie.AltM, sortie.HeadingDeg);
                }
                else
                {
                    // Fly toward the current waypoint with a limited turn rate.
                    var (wLat, wLon) = route![waypoint];
                    if (Geo.DistanceM(lat, lon, wLat, wLon) < 2_000)
                    {
                        waypoint = (waypoint + 1) % route.Length;
                        (wLat, wLon) = route[waypoint];
                    }
                    var turn = Math.Clamp(Geo.Delta(heading, Geo.BearingDeg(lat, lon, wLat, wLon)), -MaxTurnDegPerSec * TickSeconds, MaxTurnDegPerSec * TickSeconds);
                    heading = Geo.Normalize(heading + turn);
                    (lat, lon) = Geo.Move(lat, lon, heading, AircraftSpeedMs * TickSeconds);
                    alt += (Math.Sin(t / 90) * 600 + 3_500 - alt) * 0.01 + Noise(2);
                }

                w.WriteNavigation(offset, lat, lon, (float)alt);
                records++;

                if (tick % corruptEvery == corruptEvery / 2 && corrupt < scenario.CorruptRecords)
                {
                    WriteCorruption(w, offset, corrupt);
                    corrupt++;
                }

                // Missiles already in the air reach the warner's range.
                for (var i = missiles.Count - 1; i >= 0; i--)
                {
                    var (at, shooter) = missiles[i];
                    if (t < at)
                        continue;
                    missiles.RemoveAt(i);
                    var from = Geo.Normalize(Geo.BearingDeg(lat, lon, shooter.Lat, shooter.Lon) - heading + Noise(4));
                    w.WriteAlert(offset, AlertKind.MissileApproach, shooter.Id, (float)from);
                    records += 1 + Dispense(w, offset, chaff, 2) + Dispense(w, offset, flares, 2);
                }

                if (tick % emitterEvery != 0)
                    continue;

                var locked = false;
                foreach (var e in emitters)
                {
                    var distance = Geo.DistanceM(lat, lon, e.Lat, e.Lon);
                    var lockOn = e.CanTrack && e.IsOn(t) && distance < e.RangeM * 0.35;
                    var detected = lockOn || (e.IsOn(t) && distance <= e.RangeM && e.Illuminating(t));
                    var bearing = Geo.Normalize(Geo.BearingDeg(lat, lon, e.Lat, e.Lon) - heading + Noise(1.5));

                    if (!lockOn)
                        e.LaunchAt = null; // track broken before the shot
                    else if (!e.Locked)
                    {
                        w.WriteAlert(offset, AlertKind.RadarLock, e.Id, (float)bearing);
                        records++;
                        if (e.CanLaunch && _random.NextDouble() < 0.6)
                            e.LaunchAt = t + 4 + _random.NextDouble() * 6;
                    }
                    e.Locked = lockOn;
                    if (!detected)
                        continue;
                    locked |= lockOn;

                    if (t - e.LastSeen > NewThreatGapS)
                    {
                        w.WriteAlert(offset, AlertKind.NewThreat, e.Id, (float)bearing);
                        records++;
                    }
                    e.LastSeen = t;
                    if (e.LaunchAt is { } launch && t >= launch)
                    {
                        w.WriteAlert(offset, AlertKind.MissileLaunch, e.Id, (float)bearing);
                        records++;
                        e.LaunchAt = null;
                        missiles.Add((t + distance / 900 * 0.6, e)); // ~Mach 3, called in the last part of its flight
                    }

                    var amplitude = (float)Math.Clamp(-25 - 20 * Math.Log10(distance / 5_000) + Noise(1), -90, -10);
                    for (var s = 0; s < subTicks; s++)
                    {
                        var at = offset + TimeSpan.FromSeconds(s * TickSeconds / subTicks);
                        w.WriteEmitter(at, e.Id, e.FrequencyMHz + (float)Noise(0.5), e.PriUs * (1 + (float)Noise(0.002)),
                            e.PulseWidthUs, (float)bearing, amplitude);
                        records++;
                    }
                }

                // Under lock, dispense a little chaff every several seconds while it lasts.
                if (locked && t >= nextChaff)
                {
                    records += Dispense(w, offset, chaff, (byte)_random.Next(1, 3));
                    nextChaff = t + 8 + _random.NextDouble() * 8;
                }
            }

            if (scenario.TruncateEnd)
            {
                // A record header promising more bytes than the recorder managed to write.
                w.WriteGarbage([0x5A, 0xA5, 0x01, 0x00, 1, 2, 3, 4, 5, 6, 7, 8, 24, 0, 9, 9, 9]);
            }
        }

        return new Result(records, corrupt, new FileInfo(path).Length);
    }

    /// <summary>Releases up to <paramref name="count"/> from the store; calls LOW at a quarter left and OUT when empty.</summary>
    /// <returns>Records written.</returns>
    private static int Dispense(SampleFormatWriter w, TimeSpan offset, Store store, byte count)
    {
        var released = (byte)Math.Min(count, store.Left);
        if (released == 0)
            return 0;
        var before = store.Left;
        store.Left -= released;
        w.WriteCountermeasure(offset, store.Kind, released);
        var low = store.Load / 4;
        if (store.Left == 0)
            w.WriteAlert(offset, store.OutAlert);
        else if (before > low && store.Left <= low)
            w.WriteAlert(offset, store.LowAlert);
        else
            return 1;
        return 2;
    }

    private sealed class Store(byte kind, int load, AlertKind lowAlert, AlertKind outAlert)
    {
        public byte Kind { get; } = kind;
        public int Load { get; } = load;
        public int Left { get; set; } = load;
        public AlertKind LowAlert { get; } = lowAlert;
        public AlertKind OutAlert { get; } = outAlert;
    }

    private void WriteCorruption(SampleFormatWriter w, TimeSpan offset, int index)
    {
        // Alternate the two kinds of damage the parser must survive.
        if (index % 2 == 0)
            w.WriteRecord(SampleFormat.EmitterMessage, offset, new byte[24], corruptChecksum: true);
        else
            w.WriteGarbage([0xDE, 0xAD, 0xBE, 0xEF, 0x5A, 0x00, 0x13, 0x37]);
    }

    /// <param name="site">A fixed site (sorties); otherwise a random position and family in the patrol area.</param>
    private Emitter CreateEmitter(int id, ThreatSite? site = null)
    {
        var (lat, lon) = site is null
            ? Geo.Move(CenterLat, CenterLon, _random.NextDouble() * 360, Math.Sqrt(_random.NextDouble()) * AreaRadiusM)
            : (site.Lat, site.Lon);
        // A few radar families so the data has realistic clusters.
        var family = site?.Family ?? _random.Next(4);
        var (freq, pri, pw, range) = family switch
        {
            0 => (2_900 + _random.Next(200), 2_500 + _random.Next(1_500), 20 + _random.Next(15), 150_000),  // early warning
            1 => (5_400 + _random.Next(400), 800 + _random.Next(600), 2 + _random.Next(4), 90_000),          // acquisition
            2 => (9_000 + _random.Next(1_500), 150 + _random.Next(200), 0.4 + _random.NextDouble(), 45_000), // fire control
            _ => (14_000 + _random.Next(3_000), 60 + _random.Next(80), 0.2 + _random.NextDouble() * 0.3, 25_000), // short range
        };
        return new Emitter(
            (uint)id, lat, lon, freq, pri, (float)pw, range, CanTrack: family >= 1, CanLaunch: family >= 2,
            ScanPeriodS: 3 + _random.NextDouble() * 5,
            DwellS: 0.6 + _random.NextDouble() * 1.2,
            OnS: 120 + _random.NextDouble() * 480,
            OffS: 60 + _random.NextDouble() * 240,
            PhaseS: _random.NextDouble() * 600);
    }

    private (double Lat, double Lon)[] CreateRoute()
    {
        var points = new (double, double)[8];
        for (var i = 0; i < points.Length; i++)
        {
            var bearing = i * 360.0 / points.Length + _random.NextDouble() * 30;
            points[i] = Geo.Move(CenterLat, CenterLon, bearing, AreaRadiusM * (0.5 + _random.NextDouble() * 0.5));
        }
        return points;
    }

    private double Noise(double sigma) => sigma * (_random.NextDouble() + _random.NextDouble() + _random.NextDouble() - 1.5);

    private sealed record Emitter(
        uint Id, double Lat, double Lon, float FrequencyMHz, float PriUs, float PulseWidthUs, double RangeM, bool CanTrack, bool CanLaunch,
        double ScanPeriodS, double DwellS, double OnS, double OffS, double PhaseS)
    {
        public double LastSeen { get; set; } = double.NegativeInfinity;
        public bool Locked { get; set; }
        public double? LaunchAt { get; set; }

        public bool IsOn(double t) => (t + PhaseS) % (OnS + OffS) < OnS;
        public bool Illuminating(double t) => (t + PhaseS) % ScanPeriodS < DwellS;
    }
}
