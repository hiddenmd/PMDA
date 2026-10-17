using PMDA.Core.Events;
using PMDA.Data;

namespace PMDA.Api.Contracts;

public sealed record EmitterHitDto(int EmitterId, int Hits, float FrequencyMHz, float PriUs, float PulseWidthUs, float BearingDeg, float AmplitudeDb);

/// <param name="Kind">1 = chaff, 2 = flare (sample format; other formats map to these).</param>
public sealed record CountermeasureCountDto(short Kind, int Count);

public sealed record PositionDto(double Lat, double Lon, float AltM);

/// <param name="OffsetMs">Start of the step, in ms from the flight start.</param>
public sealed record PlaybackFrameDto(long OffsetMs, EmitterHitDto[] Emitters, CountermeasureCountDto[] Countermeasures, PositionDto? Position);

/// <summary>Frames for [FromMs, ToMs) in steps of StepMs; steps without data are omitted.</summary>
public sealed record PlaybackFramesDto(long FromMs, long ToMs, long StepMs, PlaybackFrameDto[] Frames)
{
    public static PlaybackFramesDto From(long fromMs, long toMs, long stepMs, IReadOnlyList<PlaybackFrame> frames) => new(
        fromMs, toMs, stepMs,
        frames.Select(f => new PlaybackFrameDto(
            f.OffsetMs,
            f.Emitters.Select(e => new EmitterHitDto(e.EmitterId, e.Hits, e.FrequencyMHz, e.PriUs, e.PulseWidthUs, e.BearingDeg, e.AmplitudeDb)).ToArray(),
            f.Countermeasures.Select(c => new CountermeasureCountDto(c.Kind, c.Count)).ToArray(),
            f.Position is { } p ? new PositionDto(p.Lat, p.Lon, p.AltM) : null)).ToArray());
}

public sealed record OverviewSeriesDto(EventType EventType, long[] Counts);

/// <summary>Event counts per type in equal buckets spanning SpanMs from the flight start.</summary>
public sealed record FlightOverviewDto(long SpanMs, int Buckets, OverviewSeriesDto[] Series)
{
    public static FlightOverviewDto From(FlightOverview o) => new(
        o.SpanMs, o.Buckets, o.Counts.OrderBy(c => c.Key).Select(c => new OverviewSeriesDto(c.Key, c.Value)).ToArray());
}
