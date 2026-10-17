using Dapper;
using Microsoft.Data.SqlClient;
using PMDA.Core.Events;

namespace PMDA.Data;

/// <summary>Emitter parameters that can be plotted.</summary>
public enum SeriesParam
{
    Frequency,
    Pri,
    PulseWidth,
    Amplitude,
    Bearing,
}

/// <summary>A flight-time window in ms from the flight start, split into equal buckets.</summary>
public sealed record SeriesWindow(long FromMs, long ToMs, long BucketMs);

/// <summary>One emitter's points; T is the bucket centre (ms from flight start).</summary>
public sealed record EmitterSeries(int EmitterId, long[] T, double[] V);

public sealed record EmitterScatter(int EmitterId, double[] X, double[] Y);

public sealed record ProfileData(long[] T, double[] AltM, IReadOnlyList<CountermeasureMark> Countermeasures);

public sealed record CountermeasureMark(long T, short Kind, int Count);

public sealed record RateData(long[] T, IReadOnlyDictionary<EventType, long[]> Counts);

/// <summary>
/// Downsampled data for graphs: aggregated per time bucket (and per emitter) in SQL, so a plot of
/// an 11-million-event flight is a few thousand points.
/// </summary>
public sealed class SeriesRepository(string connectionString)
{
    public const int MaxBuckets = 4_000;

    /// <summary>Aggregate per bucket. Bearing is a circular mean: 359° and 1° average to 0°.</summary>
    private static string Aggregate(SeriesParam p) => p switch
    {
        SeriesParam.Frequency => "AVG(CAST(FrequencyMHz AS float))",
        SeriesParam.Pri => "AVG(CAST(PriUs AS float))",
        SeriesParam.PulseWidth => "AVG(CAST(PulseWidthUs AS float))",
        SeriesParam.Amplitude => "MAX(CAST(AmplitudeDb AS float))",
        SeriesParam.Bearing => "DEGREES(ATN2(AVG(SIN(RADIANS(CAST(BearingDeg AS float)))), AVG(COS(RADIANS(CAST(BearingDeg AS float))))))",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    public async Task<(SeriesWindow Window, IReadOnlyList<EmitterSeries> Series)?> ParameterAsync(
        Guid flightId, SeriesParam param, long? fromMs, long? toMs, int buckets, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        if (await WindowAsync(conn, flightId, fromMs, toMs, buckets, ct) is not { } w)
            return null;

        var rows = await conn.QueryAsync<(long Bucket, int EmitterId, double? Value)>(new CommandDefinition($"""
            SELECT (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Bucket AS Bucket, EmitterId, {Aggregate(param)} AS Value
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND EventType = @Emitter AND EmitterId IS NOT NULL
              AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc
            GROUP BY (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Bucket, EmitterId;
            """, w.Args(new { Emitter = (byte)EventType.Emitter }), commandTimeout: 120, cancellationToken: ct));

        var series = rows.Where(r => r.Value is not null)
            .GroupBy(r => r.EmitterId)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var points = g.OrderBy(r => r.Bucket).ToArray();
                return new EmitterSeries(
                    g.Key,
                    points.Select(r => w.Center(r.Bucket)).ToArray(),
                    points.Select(r => param == SeriesParam.Bearing ? NormalizeDeg(r.Value!.Value) : r.Value!.Value).ToArray());
            })
            .ToList();
        return (w.Window, series);
    }

    public async Task<(SeriesWindow Window, IReadOnlyList<EmitterScatter> Series)?> ScatterAsync(
        Guid flightId, SeriesParam x, SeriesParam y, long? fromMs, long? toMs, int buckets, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        if (await WindowAsync(conn, flightId, fromMs, toMs, buckets, ct) is not { } w)
            return null;

        var rows = await conn.QueryAsync<(int EmitterId, double? X, double? Y)>(new CommandDefinition($"""
            SELECT EmitterId, {Aggregate(x)} AS X, {Aggregate(y)} AS Y
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND EventType = @Emitter AND EmitterId IS NOT NULL
              AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc
            GROUP BY (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Bucket, EmitterId;
            """, w.Args(new { Emitter = (byte)EventType.Emitter }), commandTimeout: 120, cancellationToken: ct));

        double Fix(SeriesParam p, double v) => p == SeriesParam.Bearing ? NormalizeDeg(v) : v;
        var series = rows.Where(r => r.X is not null && r.Y is not null)
            .GroupBy(r => r.EmitterId)
            .OrderBy(g => g.Key)
            .Select(g => new EmitterScatter(g.Key, g.Select(r => Fix(x, r.X!.Value)).ToArray(), g.Select(r => Fix(y, r.Y!.Value)).ToArray()))
            .ToList();
        return (w.Window, series);
    }

    public async Task<(SeriesWindow Window, ProfileData Data)?> ProfileAsync(Guid flightId, long? fromMs, long? toMs, int buckets, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        if (await WindowAsync(conn, flightId, fromMs, toMs, buckets, ct) is not { } w)
            return null;

        var args = w.Args(new { Navigation = (byte)EventType.Navigation, Countermeasure = (byte)EventType.Countermeasure });
        await using var grid = await conn.QueryMultipleAsync(new CommandDefinition("""
            SELECT (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Bucket AS Bucket, AVG(CAST(AltM AS float)) AS AltM
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND EventType = @Navigation AND AltM IS NOT NULL AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc
            GROUP BY (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Bucket
            ORDER BY Bucket;

            SELECT DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) AS T, ISNULL(SubType, 0) AS Kind, ISNULL([Count], 1) AS [Count]
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND EventType = @Countermeasure AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc
            ORDER BY T;
            """, args, commandTimeout: 120, cancellationToken: ct));

        var alt = (await grid.ReadAsync<(long Bucket, double AltM)>()).ToArray();
        var marks = (await grid.ReadAsync<CountermeasureMark>()).AsList();
        return (w.Window, new ProfileData(alt.Select(a => w.Center(a.Bucket)).ToArray(), alt.Select(a => a.AltM).ToArray(), marks));
    }

    public async Task<(SeriesWindow Window, RateData Data)?> RateAsync(Guid flightId, long? fromMs, long? toMs, int buckets, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        if (await WindowAsync(conn, flightId, fromMs, toMs, buckets, ct) is not { } w)
            return null;

        var rows = await conn.QueryAsync<(long Bucket, byte EventType, long Count)>(new CommandDefinition("""
            SELECT (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Bucket AS Bucket, EventType, COUNT_BIG(*) AS [Count]
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc
            GROUP BY (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Bucket, EventType;
            """, w.Args(new { }), commandTimeout: 120, cancellationToken: ct));

        var n = (int)((w.Window.ToMs - w.Window.FromMs + w.Window.BucketMs - 1) / w.Window.BucketMs);
        var counts = new Dictionary<EventType, long[]>();
        foreach (var (bucket, type, count) in rows)
        {
            if (!counts.TryGetValue((EventType)type, out var series))
                counts[(EventType)type] = series = new long[n];
            if (bucket >= 0 && bucket < n)
                series[bucket] += count;
        }
        var t = Enumerable.Range(0, n).Select(i => w.Center(i)).ToArray();
        return (w.Window, new RateData(t, counts));
    }

    /// <summary>Resolves the window (default: whole flight) and bucket size; null when the flight does not exist.</summary>
    private static async Task<ResolvedWindow?> WindowAsync(SqlConnection conn, Guid flightId, long? fromMs, long? toMs, int buckets, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(buckets, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(buckets, MaxBuckets);

        var flight = await conn.QuerySingleOrDefaultAsync<(DateTime StartUtc, DateTime? LastEventUtc)>(new CommandDefinition(
            "SELECT StartUtc, LastEventUtc FROM dbo.Flight WHERE FlightId = @flightId;", new { flightId }, cancellationToken: ct));
        if (flight == default)
            return null;

        var spanMs = flight.LastEventUtc is { } last ? Math.Max(1, (long)(last - flight.StartUtc).TotalMilliseconds + 1) : 1;
        // An explicit window is honoured as asked (bucket sizes stay predictable for the caller);
        // only the default spans exactly the recorded flight.
        var from = Math.Max(0, fromMs ?? 0);
        var to = Math.Max(from + 1, toMs ?? spanMs);
        var bucket = Math.Max(1, (to - from + buckets - 1) / buckets);
        return new ResolvedWindow(flightId, flight.StartUtc, new SeriesWindow(from, to, bucket));
    }

    private sealed record ResolvedWindow(Guid FlightId, DateTime Start, SeriesWindow Window)
    {
        public long Center(long bucket) => Window.FromMs + bucket * Window.BucketMs + Window.BucketMs / 2;

        public DynamicParameters Args(object extra)
        {
            var p = new DynamicParameters(extra);
            p.Add("flightId", FlightId);
            p.Add("Start", Start);
            p.Add("From", Window.FromMs);
            p.Add("Bucket", Window.BucketMs);
            p.Add("FromUtc", Start.AddMilliseconds(Window.FromMs));
            p.Add("ToUtc", Start.AddMilliseconds(Window.ToMs));
            return p;
        }
    }

    private static double NormalizeDeg(double deg) => ((deg % 360) + 360) % 360;
}
