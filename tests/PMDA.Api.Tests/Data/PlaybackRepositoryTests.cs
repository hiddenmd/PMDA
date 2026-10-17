using PMDA.Api.Tests.Support;
using PMDA.Core.Events;
using PMDA.Core.Import;
using PMDA.Core.Parsing;
using PMDA.Data;

namespace PMDA.Api.Tests.Data;

[Collection(SqlCollection.Name)]
public sealed class PlaybackRepositoryTests(SqlDatabase db) : IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);
    private readonly PlaybackRepository _playback = new(db.ConnectionString);
    private Guid _flightId;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _flightId = await StoreFlightAsync(
        [
            // step 0 (0–999 ms): emitter 7 seen at 359° and 1°, emitter 9 once; a position
            Emitter(100, 7, bearing: 359, freq: 9000, amplitude: -40),
            Emitter(300, 7, bearing: 1, freq: 9010, amplitude: -35),
            Emitter(500, 9, bearing: 90, freq: 3000, amplitude: -60),
            Nav(200, 44.0, 26.0, 3000),
            Nav(700, 44.2, 26.2, 3100),
            // step 1 (1000–1999 ms): nothing
            // step 2 (2000–2999 ms): chaff ×3 + ×2, flare ×2
            Cm(2100, kind: 1, count: 3),
            Cm(2600, kind: 1, count: 2),
            Cm(2600, kind: 2, count: 2),
            // step 5: emitter 7 again
            Emitter(5500, 7, bearing: 180, freq: 9000, amplitude: -30),
        ]);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Frames_aggregate_each_step_and_skip_empty_steps()
    {
        var frames = await _playback.GetFramesAsync(_flightId, 0, 6000, 1000, default);

        Assert.NotNull(frames);
        Assert.Equal([0L, 2000L, 5000L], frames.Select(f => f.OffsetMs));

        var first = frames[0];
        Assert.Equal([7, 9], first.Emitters.Select(e => e.EmitterId));
        var seven = first.Emitters[0];
        Assert.Equal(2, seven.Hits);
        Assert.Equal(9005f, seven.FrequencyMHz, 0.01f);
        Assert.Equal(-35f, seven.AmplitudeDb); // peak
        Assert.True(seven.BearingDeg < 0.5f || seven.BearingDeg > 359.5f, $"circular mean of 359° and 1° should be ~0°, was {seven.BearingDeg}");
        Assert.Equal(44.1, first.Position!.Lat, 6);
        Assert.Equal(3050f, first.Position.AltM, 0.01f);

        var second = frames[1];
        Assert.Empty(second.Emitters);
        Assert.Equal([new CountermeasureCount(1, 5), new CountermeasureCount(2, 2)], second.Countermeasures);
        Assert.Null(second.Position);
    }

    [Fact]
    public async Task Frames_honour_the_window_and_step()
    {
        var frames = await _playback.GetFramesAsync(_flightId, 2000, 6000, 250, default);

        Assert.NotNull(frames);
        Assert.Equal([2000L, 2500L, 5500L], frames.Select(f => f.OffsetMs));
        Assert.Equal(180f, Assert.Single(frames[2].Emitters).BearingDeg, 0.01f);
    }

    [Fact]
    public async Task Frames_for_an_unknown_flight_are_null()
    {
        Assert.Null(await _playback.GetFramesAsync(Guid.NewGuid(), 0, 1000, 100, default));
    }

    [Fact]
    public async Task Too_many_steps_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _playback.GetFramesAsync(_flightId, 0, 1_000_000, 10, default));
    }

    [Fact]
    public async Task Overview_counts_events_per_type_per_bucket()
    {
        var overview = await _playback.GetOverviewAsync(_flightId, 6, default);

        Assert.NotNull(overview);
        Assert.Equal(5501, overview.SpanMs); // start to last event, inclusive
        Assert.Equal([3L, 0, 0, 0, 0, 1], overview.Counts[EventType.Emitter]);
        Assert.Equal([0L, 0, 3, 0, 0, 0], overview.Counts[EventType.Countermeasure]);
        Assert.Equal(2, overview.Counts[EventType.Navigation].Sum());
    }

    private async Task<Guid> StoreFlightAsync(FlightEvent[] events)
    {
        var id = Guid.NewGuid();
        var sink = new SqlEventSink(db.ConnectionString);
        await sink.BeginFlightAsync(new FlightImportInfo(id, new FlightHeader("SAMPLE", 1, Start, "T", "M"), "test.bin", null), default);
        var withIds = events.Select((e, i) => e with { FlightId = id, Seq = i }).ToArray();
        await sink.WriteBatchAsync(id, withIds, default);
        await sink.CompleteFlightAsync(new FlightImportSummary(id, withIds.Length, 0, withIds.Min(e => e.TimeUtc), withIds.Max(e => e.TimeUtc)), default);
        return id;
    }

    private static FlightEvent Emitter(int ms, int id, float bearing, float freq, float amplitude) => new()
    {
        TimeUtc = Start.AddMilliseconds(ms),
        EventType = EventType.Emitter,
        EmitterId = id,
        BearingDeg = bearing,
        FrequencyMHz = freq,
        PriUs = 1000,
        PulseWidthUs = 1,
        AmplitudeDb = amplitude,
    };

    private static FlightEvent Cm(int ms, short kind, int count) => new()
    {
        TimeUtc = Start.AddMilliseconds(ms),
        EventType = EventType.Countermeasure,
        SubType = kind,
        Count = count,
    };

    private static FlightEvent Nav(int ms, double lat, double lon, float alt) => new()
    {
        TimeUtc = Start.AddMilliseconds(ms),
        EventType = EventType.Navigation,
        Lat = lat,
        Lon = lon,
        AltM = alt,
    };
}
