using PMDA.Core.Events;
using PMDA.Core.Import;
using PMDA.Core.Parsing;
using PMDA.Data;

namespace PMDA.Api.Tests.Support;

/// <summary>Hand-built events stored straight through the SQL sink, for exact assertions.</summary>
internal static class TestFlights
{
    public static readonly DateTime Start = new(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);

    public static async Task<Guid> StoreAsync(string connectionString, FlightEvent[] events, string tail = "T", string mission = "M",
        string customer = "SAMPLE", long errorCount = 0, DateTime? start = null)
    {
        var flightStart = start ?? Start;
        var id = Guid.NewGuid();
        var sink = new SqlEventSink(connectionString);
        await sink.BeginFlightAsync(new FlightImportInfo(id, new FlightHeader(customer, 1, flightStart, tail, mission), "test.bin", null), default);
        var stored = events.Select((e, i) => e with { FlightId = id, Seq = i, TimeUtc = flightStart + (e.TimeUtc - Start) }).ToArray();
        if (stored.Length > 0)
            await sink.WriteBatchAsync(id, stored, default);
        await sink.CompleteFlightAsync(new FlightImportSummary(id, stored.Length, errorCount,
            stored.Length > 0 ? stored.Min(e => e.TimeUtc) : null, stored.Length > 0 ? stored.Max(e => e.TimeUtc) : null), default);
        return id;
    }

    public static FlightEvent Emitter(int ms, int id, float bearing = 90, float freq = 9000, float amplitude = -50, float pri = 1000) => new()
    {
        TimeUtc = Start.AddMilliseconds(ms),
        EventType = EventType.Emitter,
        EmitterId = id,
        BearingDeg = bearing,
        FrequencyMHz = freq,
        PriUs = pri,
        PulseWidthUs = 1,
        AmplitudeDb = amplitude,
    };

    public static FlightEvent Cm(int ms, short kind, int count) => new()
    {
        TimeUtc = Start.AddMilliseconds(ms),
        EventType = EventType.Countermeasure,
        SubType = kind,
        Count = count,
    };

    public static FlightEvent Nav(int ms, double lat, double lon, float alt) => new()
    {
        TimeUtc = Start.AddMilliseconds(ms),
        EventType = EventType.Navigation,
        Lat = lat,
        Lon = lon,
        AltM = alt,
    };
}
