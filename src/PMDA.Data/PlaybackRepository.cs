using Dapper;
using Microsoft.Data.SqlClient;
using PMDA.Core.Events;

namespace PMDA.Data;

/// <summary>One emitter as seen during one playback step.</summary>
public sealed record EmitterHit(int EmitterId, int Hits, float FrequencyMHz, float PriUs, float PulseWidthUs, float BearingDeg, float AmplitudeDb);

public sealed record CountermeasureCount(short Kind, int Count);

public sealed record Position(double Lat, double Lon, float AltM);

/// <summary>Everything that happened in one step of flight time, starting at <see cref="OffsetMs"/>.</summary>
public sealed record PlaybackFrame(long OffsetMs, IReadOnlyList<EmitterHit> Emitters, IReadOnlyList<CountermeasureCount> Countermeasures, Position? Position);

/// <summary>The flight path, downsampled: parallel arrays, T in ms from the flight start, in time order.</summary>
public sealed record FlightTrack(long[] T, double[] Lat, double[] Lon, float[] AltM);

/// <summary>One aural warning. Bearing is relative to the aircraft nose, like the RWR's.</summary>
public sealed record AlertRecord(long OffsetMs, AlertKind Kind, int? EmitterId, float? BearingDeg);

public sealed record EmitterBearingSlice(int EmitterId, long OffsetMs, double RelativeBearingDeg, float FrequencyMHz, float PriUs, float PulseWidthUs);

public sealed record FlightOverview(long SpanMs, int Buckets, IReadOnlyDictionary<EventType, long[]> Counts);

/// <summary>
/// Aggregated reads for playback. Raw events are far too many to send to a client at high speed,
/// so they are summarised per time step in SQL, where the columnstore makes this cheap.
/// Offsets are milliseconds from the flight's recorded start (Flight.StartUtc).
/// </summary>
public sealed class PlaybackRepository(IConnectionSource connection)
{
    public PlaybackRepository(string connectionString) : this(new FixedConnection(connectionString)) { }

    public const int MaxStepsPerRequest = 2_000;

    /// <returns>Frames for steps that have data, ordered by offset; null when the flight does not exist.</returns>
    public async Task<IReadOnlyList<PlaybackFrame>?> GetFramesAsync(Guid flightId, long fromMs, long toMs, long stepMs, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(stepMs, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toMs, fromMs);
        if ((toMs - fromMs + stepMs - 1) / stepMs > MaxStepsPerRequest)
            throw new ArgumentOutOfRangeException(nameof(stepMs), $"At most {MaxStepsPerRequest} steps per request.");

        await using var conn = new SqlConnection(connection.Value);
        var start = await GetStartAsync(conn, flightId, ct);
        if (start is null)
            return null;

        var args = new
        {
            flightId,
            Start = start.Value,
            From = fromMs,
            Step = stepMs,
            FromUtc = start.Value.AddMilliseconds(fromMs),
            ToUtc = start.Value.AddMilliseconds(toMs),
            Emitter = (byte)EventType.Emitter,
            Countermeasure = (byte)EventType.Countermeasure,
            Navigation = (byte)EventType.Navigation,
        };

        // Bearing uses a circular mean: averaging 359° and 1° must give 0°, not 180°.
        const string sql = """
            WITH e AS (
                SELECT (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Step AS StepIndex, *
                FROM dbo.FlightEvent
                WHERE FlightId = @flightId AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc
            )
            SELECT StepIndex, EmitterId, COUNT(*) AS Hits,
                   CAST(AVG(FrequencyMHz) AS real) AS FrequencyMHz, CAST(AVG(PriUs) AS real) AS PriUs,
                   CAST(AVG(PulseWidthUs) AS real) AS PulseWidthUs, MAX(AmplitudeDb) AS AmplitudeDb,
                   CAST(DEGREES(ATN2(AVG(SIN(RADIANS(CAST(BearingDeg AS float)))), AVG(COS(RADIANS(CAST(BearingDeg AS float)))))) AS real) AS BearingDeg
            FROM e WHERE EventType = @Emitter AND EmitterId IS NOT NULL
            GROUP BY StepIndex, EmitterId;

            WITH e AS (
                SELECT (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Step AS StepIndex, SubType, [Count]
                FROM dbo.FlightEvent
                WHERE FlightId = @flightId AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc AND EventType = @Countermeasure
            )
            SELECT StepIndex, ISNULL(SubType, 0) AS Kind, SUM(ISNULL([Count], 1)) AS [Count]
            FROM e GROUP BY StepIndex, ISNULL(SubType, 0);

            WITH e AS (
                SELECT (DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) - @From) / @Step AS StepIndex, Lat, Lon, AltM
                FROM dbo.FlightEvent
                WHERE FlightId = @flightId AND TimeUtc >= @FromUtc AND TimeUtc < @ToUtc AND EventType = @Navigation
                  AND Lat IS NOT NULL AND Lon IS NOT NULL
            )
            SELECT StepIndex, AVG(Lat) AS Lat, AVG(Lon) AS Lon, CAST(AVG(AltM) AS real) AS AltM
            FROM e GROUP BY StepIndex;
            """;

        await using var grid = await conn.QueryMultipleAsync(new CommandDefinition(sql, args, commandTimeout: 120, cancellationToken: ct));
        var emitters = (await grid.ReadAsync<EmitterRow>()).ToLookup(r => r.StepIndex);
        var countermeasures = (await grid.ReadAsync<CountermeasureRow>()).ToLookup(r => r.StepIndex);
        var positions = (await grid.ReadAsync<PositionRow>()).ToDictionary(r => r.StepIndex);

        var steps = emitters.Select(g => g.Key).Concat(countermeasures.Select(g => g.Key)).Concat(positions.Keys).Distinct().Order();
        return steps.Select(step => new PlaybackFrame(
                fromMs + step * stepMs,
                emitters[step].OrderBy(r => r.EmitterId)
                    .Select(r => new EmitterHit(r.EmitterId, r.Hits, r.FrequencyMHz, r.PriUs, r.PulseWidthUs, NormalizeDeg(r.BearingDeg), r.AmplitudeDb))
                    .ToList(),
                countermeasures[step].OrderBy(r => r.Kind).Select(r => new CountermeasureCount(r.Kind, r.Count)).ToList(),
                positions.TryGetValue(step, out var p) ? new Position(p.Lat, p.Lon, p.AltM) : null))
            .ToList();
    }

    /// <summary>Event counts per type in <paramref name="buckets"/> equal slices from flight start to the last event.</summary>
    public async Task<FlightOverview?> GetOverviewAsync(Guid flightId, int buckets, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(buckets, 1);

        await using var conn = new SqlConnection(connection.Value);
        var flight = await conn.QuerySingleOrDefaultAsync<(DateTime StartUtc, DateTime? LastEventUtc)>(new CommandDefinition(
            "SELECT StartUtc, LastEventUtc FROM dbo.Flight WHERE FlightId = @flightId AND Status <> 3;", new { flightId }, cancellationToken: ct));
        if (flight == default)
            return null;

        var spanMs = flight.LastEventUtc is { } last ? Math.Max(1, (long)(last - flight.StartUtc).TotalMilliseconds + 1) : 1;
        var rows = await conn.QueryAsync<(int Bucket, byte EventType, long Count)>(new CommandDefinition("""
            SELECT CAST(DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) * @Buckets / @SpanMs AS int) AS Bucket,
                   EventType, COUNT_BIG(*) AS [Count]
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND TimeUtc >= @Start
            GROUP BY CAST(DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) * @Buckets / @SpanMs AS int), EventType;
            """,
            new { flightId, Start = flight.StartUtc, Buckets = (long)buckets, SpanMs = spanMs },
            commandTimeout: 120,
            cancellationToken: ct));

        var counts = new Dictionary<EventType, long[]>();
        foreach (var (bucket, type, count) in rows)
        {
            if (!counts.TryGetValue((EventType)type, out var series))
                counts[(EventType)type] = series = new long[buckets];
            series[Math.Clamp(bucket, 0, buckets - 1)] += count;
        }
        return new FlightOverview(spanMs, buckets, counts);
    }

    public const int MaxTrackPoints = 20_000;
    public const int MaxAlerts = 20_000;

    /// <summary>
    /// The navigation track in at most <paramref name="points"/> time slices, each the average position of its
    /// samples at their average time. Null when the flight does not exist; empty arrays when it has no positions.
    /// Averaging longitudes is wrong across the antimeridian; flights there would need a different reduction.
    /// </summary>
    public async Task<FlightTrack?> GetTrackAsync(Guid flightId, int points, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(points, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(points, MaxTrackPoints);

        await using var conn = new SqlConnection(connection.Value);
        var flight = await conn.QuerySingleOrDefaultAsync<(DateTime StartUtc, DateTime? LastEventUtc)>(new CommandDefinition(
            "SELECT StartUtc, LastEventUtc FROM dbo.Flight WHERE FlightId = @flightId AND Status <> 3;", new { flightId }, cancellationToken: ct));
        if (flight == default)
            return null;

        var spanMs = flight.LastEventUtc is { } last ? Math.Max(1, (long)(last - flight.StartUtc).TotalMilliseconds + 1) : 1;
        var slices = (await conn.QueryAsync<(long T, double Lat, double Lon, double AltM)>(new CommandDefinition("""
            WITH n AS (
                SELECT DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) AS T, Lat, Lon, AltM
                FROM dbo.FlightEvent
                WHERE FlightId = @flightId AND EventType = @Navigation AND Lat IS NOT NULL AND Lon IS NOT NULL AND TimeUtc >= @Start
            )
            SELECT AVG(T) AS T, AVG(Lat) AS Lat, AVG(Lon) AS Lon, ISNULL(AVG(CAST(AltM AS float)), 0) AS AltM
            FROM n
            GROUP BY T * @Points / @SpanMs
            ORDER BY T;
            """,
            new { flightId, Start = flight.StartUtc, Points = (long)points, SpanMs = spanMs, Navigation = (byte)EventType.Navigation },
            commandTimeout: 120,
            cancellationToken: ct))).AsList();

        return new FlightTrack(
            slices.Select(x => x.T).ToArray(),
            slices.Select(x => x.Lat).ToArray(),
            slices.Select(x => x.Lon).ToArray(),
            slices.Select(x => (float)x.AltM).ToArray());
    }

    /// <summary>Aural warnings in time order (at most <see cref="MaxAlerts"/>); null when the flight does not exist.</summary>
    public async Task<IReadOnlyList<AlertRecord>?> GetAlertsAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        var start = await GetStartAsync(conn, flightId, ct);
        if (start is null)
            return null;

        var rows = await conn.QueryAsync<(long OffsetMs, short Kind, int? EmitterId, float? BearingDeg)>(new CommandDefinition("""
            SELECT TOP (@Max) DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) AS OffsetMs, ISNULL(SubType, 0) AS Kind, EmitterId, BearingDeg
            FROM dbo.FlightEvent
            WHERE FlightId = @flightId AND EventType = @Warning
            ORDER BY TimeUtc, Seq;
            """,
            new { flightId, Start = start.Value, Max = MaxAlerts, Warning = (byte)EventType.Warning },
            commandTimeout: 120,
            cancellationToken: ct));

        return rows.Select(r => new AlertRecord(
                r.OffsetMs,
                Enum.IsDefined((AlertKind)r.Kind) ? (AlertKind)r.Kind : AlertKind.Unknown,
                r.EmitterId,
                r.BearingDeg is { } b ? NormalizeDeg(b) : null))
            .ToList();
    }

    public const long BearingSliceMs = 2_000;

    /// <summary>
    /// Per emitter, the circular-mean relative bearing in <see cref="BearingSliceMs"/> slices (offset ms from the flight start),
    /// with the emitter's mean frequency, PRI and pulse width. Null when the flight does not exist.
    /// </summary>
    public async Task<IReadOnlyList<EmitterBearingSlice>?> GetEmitterBearingsAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connection.Value);
        var start = await GetStartAsync(conn, flightId, ct);
        if (start is null)
            return null;

        var rows = await conn.QueryAsync<EmitterBearingSlice>(new CommandDefinition("""
            WITH e AS (
                SELECT EmitterId, DATEDIFF_BIG(MILLISECOND, @Start, TimeUtc) AS T, RADIANS(CAST(BearingDeg AS float)) AS B,
                       FrequencyMHz, PriUs, PulseWidthUs
                FROM dbo.FlightEvent
                WHERE FlightId = @flightId AND EventType = @Emitter AND EmitterId IS NOT NULL AND BearingDeg IS NOT NULL AND TimeUtc >= @Start
            )
            SELECT EmitterId, AVG(T) AS OffsetMs, DEGREES(ATN2(AVG(SIN(B)), AVG(COS(B)))) AS RelativeBearingDeg,
                   CAST(AVG(CAST(FrequencyMHz AS float)) AS real) AS FrequencyMHz,
                   CAST(AVG(CAST(PriUs AS float)) AS real) AS PriUs,
                   CAST(AVG(CAST(PulseWidthUs AS float)) AS real) AS PulseWidthUs
            FROM e
            GROUP BY EmitterId, T / @SliceMs
            ORDER BY EmitterId, OffsetMs;
            """,
            new { flightId, Start = start.Value, SliceMs = BearingSliceMs, Emitter = (byte)EventType.Emitter },
            commandTimeout: 300,
            cancellationToken: ct));
        return rows.AsList();
    }

    private static async Task<DateTime?> GetStartAsync(SqlConnection conn, Guid flightId, CancellationToken ct) =>
        await conn.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT StartUtc FROM dbo.Flight WHERE FlightId = @flightId AND Status <> 3;", new { flightId }, cancellationToken: ct));

    private static float NormalizeDeg(float deg) => ((deg % 360) + 360) % 360;

    private sealed record EmitterRow(long StepIndex, int EmitterId, int Hits, float FrequencyMHz, float PriUs, float PulseWidthUs, float AmplitudeDb, float BearingDeg);
    private sealed record CountermeasureRow(long StepIndex, short Kind, int Count);
    private sealed record PositionRow(long StepIndex, double Lat, double Lon, float AltM);
}
