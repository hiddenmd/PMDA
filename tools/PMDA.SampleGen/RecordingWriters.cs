using PMDA.Core.Events;
using PMDA.Formats.Demo;
using PMDA.Formats.Framing;
using PMDA.Formats.Projects;
using PMDA.Parsers.Sample;

namespace PMDA.SampleGen;

/// <summary>What the simulator records, in whichever file format is being written.</summary>
internal interface IRecordingWriter
{
    void WriteHeader(DateTime startUtc, string tail, string missionId);
    void WriteNavigation(TimeSpan offset, double lat, double lon, float altM, double headingDeg);
    void WriteEmitter(TimeSpan offset, uint emitterId, float freqMHz, float priUs, float pwUs, float bearingDeg, float amplitudeDb);
    void WriteCountermeasure(TimeSpan offset, byte kind, byte count);
    void WriteAlert(TimeSpan offset, AlertKind kind, uint emitterId = 0, float? bearingDeg = null);
    /// <summary>Damage the reader must survive.</summary>
    void WriteCorruption(TimeSpan offset, int index);
    /// <summary>A record whose header promises more bytes than the file holds.</summary>
    void WriteTruncatedEnd();
    void Finish(TimeSpan offset);
}

/// <summary>The built-in synthetic sample format.</summary>
internal sealed class SampleRecordingWriter(Stream stream) : IRecordingWriter
{
    private readonly SampleFormatWriter _w = new(stream);

    public void WriteHeader(DateTime startUtc, string tail, string missionId) => _w.WriteHeader(startUtc, tail, missionId);
    public void WriteNavigation(TimeSpan offset, double lat, double lon, float altM, double headingDeg) => _w.WriteNavigation(offset, lat, lon, altM);
    public void WriteEmitter(TimeSpan offset, uint emitterId, float freqMHz, float priUs, float pwUs, float bearingDeg, float amplitudeDb) =>
        _w.WriteEmitter(offset, emitterId, freqMHz, priUs, pwUs, bearingDeg, amplitudeDb);
    public void WriteCountermeasure(TimeSpan offset, byte kind, byte count) => _w.WriteCountermeasure(offset, kind, count);
    public void WriteAlert(TimeSpan offset, AlertKind kind, uint emitterId = 0, float? bearingDeg = null) => _w.WriteAlert(offset, kind, emitterId, bearingDeg);

    public void WriteCorruption(TimeSpan offset, int index)
    {
        // Alternate the two kinds of damage the parser must survive.
        if (index % 2 == 0)
            _w.WriteRecord(SampleFormat.EmitterMessage, offset, new byte[24], corruptChecksum: true);
        else
            _w.WriteGarbage([0xDE, 0xAD, 0xBE, 0xEF, 0x5A, 0x00, 0x13, 0x37]);
    }

    public void WriteTruncatedEnd() => _w.WriteGarbage([0x5A, 0xA5, 0x01, 0x00, 1, 2, 3, 4, 5, 6, 7, 8, 24, 0, 9, 9, 9]);
    public void Finish(TimeSpan offset) { }
}

/// <summary>
/// A raw recorder dump in the invented demo header's format, as a recorder would write it: separate threat
/// new/extension/update/delete events (updates about once a second per threat), navigation, countermeasures and
/// a sync record with the date and tail. PMDA's recipe merges the threat events on import.
/// </summary>
internal sealed class DemoHeaderRecordingWriter(Stream stream) : IRecordingWriter
{
    private const double UpdateEveryS = 1.0;
    private const double DropAfterS = 12;
    private const int ChaffLoad = 30, FlareLoad = 14;

    private readonly CompiledFormat _format = DemoFormat.Compile();
    private readonly Dictionary<uint, Threat> _live = new();
    private RawDumpWriter _w = null!;
    private DateTime _start;
    private int _chaff = ChaffLoad, _flares = FlareLoad;
    private double _lastSweep;

    private sealed class Threat
    {
        public double LastWritten = double.NegativeInfinity;
        public double LastSeen;
        public bool Tracking, Launch;
        public float Freq, Pri, Pw, Bearing, Amplitude;
    }

    private long Tod(TimeSpan offset) => (long)((_start.TimeOfDay + offset).TotalMilliseconds % 86_400_000);

    private static Dictionary<string, object> F(params (string Key, object Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    public void WriteHeader(DateTime startUtc, string tail, string missionId)
    {
        _w = new RawDumpWriter(_format, stream);
        _start = startUtc;
        _w.Write(12, Tod(TimeSpan.Zero), F(("event_code", 12), ("reason", 0), ("new_flight", 1), ("sync_code", 0xABCDABCDL), ("sw_major", 4), ("sw_minor", 2),
            ("ac_type", 16), ("year", startUtc.Year % 100), ("month", startUtc.Month), ("day", startUtc.Day), ("tail_num", tail)));
        _w.Write(7, Tod(TimeSpan.Zero), F(("event_code", 7), ("display", 0), ("wow", 0)));
    }

    public void WriteNavigation(TimeSpan offset, double lat, double lon, float altM, double headingDeg)
    {
        var t = offset.TotalSeconds;
        _w.Write(13, Tod(offset), F(("event_code", 13), ("valid_pos", 1), ("valid_att", 1), ("lat", lat), ("lon", lon), ("alt", altM / 0.3048),
            ("hdg", headingDeg), ("gs", 450.0)));
        if (t - _lastSweep < 1)
            return;
        _lastSweep = t;
        foreach (var (id, threat) in _live.Where(kv => t - kv.Value.LastSeen > DropAfterS).ToList())
        {
            _w.Write(5, Tod(offset), F(("event_code", 5), ("threat_id", (int)id), ("reason", 1)));
            _live.Remove(id);
        }
    }

    public void WriteEmitter(TimeSpan offset, uint emitterId, float freqMHz, float priUs, float pwUs, float bearingDeg, float amplitudeDb)
    {
        var t = offset.TotalSeconds;
        var isNew = !_live.TryGetValue(emitterId, out var threat);
        threat ??= _live[emitterId] = new Threat();
        (threat.Freq, threat.Pri, threat.Pw, threat.Bearing, threat.Amplitude, threat.LastSeen) = (freqMHz, priUs, pwUs, bearingDeg, amplitudeDb, t);
        if (isNew)
        {
            WriteThreat(1, offset, emitterId, threat);
            // The recorder follows a new threat with its PRI list.
            _w.WriteList(2, Tod(offset), F(("event_code", 2), ("threat_id", (int)emitterId), ("word_count", 6), ("list_kind", 0)), "threat_struct", "pris",
                [F(("pri_value", (double)priUs), ("pri_flags", 1)), F(("pri_value", priUs * 1.04), ("pri_flags", 0)), F(("pri_value", priUs * 0.97), ("pri_flags", 0))]);
            return;
        }
        if (t - threat.LastWritten >= UpdateEveryS)
            WriteThreat(3, offset, emitterId, threat);
    }

    private void WriteThreat(int eventId, TimeSpan offset, uint id, Threat threat)
    {
        threat.LastWritten = offset.TotalSeconds;
        _w.Write(eventId, Tod(offset), F(
            ("event_code", eventId),
            ("threat_id", (int)id),
            ("aoa", ((threat.Bearing % 360) + 360) % 360),
            ("power", Math.Clamp(threat.Amplitude, -100f, 27f)),
            ("lethal", threat.Tracking ? 1 : 0),
            ("tracking", threat.Tracking ? 1 : 0),
            ("launch", threat.Launch ? 1 : 0),
            ("frq", (double)threat.Freq),
            ("pri", (double)threat.Pri),
            ("pw", (double)threat.Pw),
            ("symbol", (int)id),
            ("site_name", $"SITE {id:00}"),
            ("lobes_0_lobe_id", (int)id),
            ("lobes_0_quality", 0.9)));
        threat.Launch = false;
    }

    public void WriteCountermeasure(TimeSpan offset, byte kind, byte count)
    {
        if (kind == 1) _chaff = Math.Max(0, _chaff - count);
        else _flares = Math.Max(0, _flares - count);
        _w.Write(20, Tod(offset), F(("event_code", 20), ("kind", (int)kind), ("count", (int)count), ("chaff_left", _chaff), ("flare_left", _flares)));
    }

    public void WriteAlert(TimeSpan offset, AlertKind kind, uint emitterId = 0, float? bearingDeg = null)
    {
        // The demo recorder has no aural-warning message: lock and launch are flags on the threat's next update.
        if (!_live.TryGetValue(emitterId, out var threat))
            return;
        if (kind == AlertKind.RadarLock)
            threat.Tracking = true;
        else if (kind == AlertKind.MissileLaunch)
            threat.Launch = true;
        else
            return;
        WriteThreat(3, offset, emitterId, threat);
    }

    public void WriteCorruption(TimeSpan offset, int index) => stream.Write([0xDE, 0xAD, 0xBE, 0xEF, 0xA5, 0x00, 0x13, 0x37]);

    public void WriteTruncatedEnd()
    {
        stream.Write(_format.Frame!.Write(13, 0, 44));
        stream.Write(new byte[4]);
    }

    public void Finish(TimeSpan offset)
    {
        foreach (var id in _live.Keys.ToList())
            _w.Write(5, Tod(offset), F(("event_code", 5), ("threat_id", (int)id), ("reason", 0)));
        _live.Clear();
    }
}
