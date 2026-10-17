using PMDA.Core.Events;
using PMDA.Data;

namespace PMDA.Api.Contracts;

public sealed record EventRowDto(
    long Seq,
    long OffsetMs,
    short? SubType,
    int? EmitterId,
    float? FrequencyMHz,
    float? PriUs,
    float? PulseWidthUs,
    float? BearingDeg,
    float? AmplitudeDb,
    double? Lat,
    double? Lon,
    float? AltM,
    int? Count);

/// <param name="NextAfterSeq">Pass as afterSeq to read the rest of the window; null when there is no more.</param>
public sealed record EventPageDto(EventType Type, long FromMs, long ToMs, EventRowDto[] Rows, bool HasMore, long? NextAfterSeq)
{
    public static EventPageDto From(EventType type, long fromMs, long toMs, EventPage page) => new(
        type, fromMs, toMs,
        page.Rows.Select(r => new EventRowDto(r.Seq, r.OffsetMs, r.SubType, r.EmitterId, r.FrequencyMHz, r.PriUs, r.PulseWidthUs,
            r.BearingDeg, r.AmplitudeDb, r.Lat, r.Lon, r.AltM, r.Count)).ToArray(),
        page.HasMore, page.NextAfterSeq);
}

public sealed record SeriesWindowDto(long FromMs, long ToMs, long BucketMs)
{
    public static SeriesWindowDto From(SeriesWindow w) => new(w.FromMs, w.ToMs, w.BucketMs);
}

/// <param name="T">Bucket centres, ms from flight start.</param>
public sealed record EmitterSeriesDto(int EmitterId, long[] T, double[] V);

public sealed record ParameterSeriesDto(SeriesParam Param, SeriesWindowDto Window, EmitterSeriesDto[] Series);

public sealed record EmitterScatterDto(int EmitterId, double[] X, double[] Y);

public sealed record ScatterSeriesDto(SeriesParam X, SeriesParam Y, SeriesWindowDto Window, EmitterScatterDto[] Series);

/// <param name="Kind">1 = chaff, 2 = flare.</param>
public sealed record CountermeasureMarkDto(long T, short Kind, int Count);

public sealed record ProfileSeriesDto(SeriesWindowDto Window, long[] T, double[] AltM, CountermeasureMarkDto[] Countermeasures);

public sealed record RateSeriesItemDto(EventType EventType, long[] Counts);

public sealed record RateSeriesDto(SeriesWindowDto Window, long[] T, RateSeriesItemDto[] Series);
