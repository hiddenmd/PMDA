using Microsoft.Extensions.Caching.Memory;
using PMDA.Api.Contracts;
using PMDA.Core.Analysis;
using PMDA.Data;

namespace PMDA.Api.Flights;

/// <summary>
/// Estimated fixed-emitter sites per flight: true bearings (track heading + relative bearing) at each 2 s slice,
/// solved by <see cref="BearingFix"/>. Completed flights never change, so results are cached per flight.
/// </summary>
public sealed class EmitterFixService(PlaybackRepository playback, IMemoryCache cache)
{
    private const long TrackStepMs = 1_000;

    public async Task<EmitterFixDto[]?> GetAsync(Guid flightId, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(flightId), out EmitterFixDto[]? cached))
            return cached;

        var slices = await playback.GetEmitterBearingsAsync(flightId, ct);
        if (slices is null)
            return null;

        var span = slices.Count == 0 ? 0 : slices.Max(s => s.OffsetMs);
        var points = (int)Math.Clamp(span / TrackStepMs + 2, 2, PlaybackRepository.MaxTrackPoints);
        var track = await playback.GetTrackAsync(flightId, points, ct);
        if (track is null)
            return null;

        var fixes = slices
            .GroupBy(s => s.EmitterId)
            .Select(g => Solve(g.Key, g.ToList(), track))
            .OrderBy(f => f.EmitterId)
            .ToArray();

        cache.Set(Key(flightId), fixes, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(6) });
        return fixes;
    }

    private static string Key(Guid flightId) => $"emitter-fixes:{flightId}";

    private static EmitterFixDto Solve(int emitterId, List<EmitterBearingSlice> slices, FlightTrack track)
    {
        var observations = new List<BearingObservation>(slices.Count);
        foreach (var s in slices)
        {
            if (Sample(track, s.OffsetMs) is not { } here)
                continue;
            var trueBearing = ((here.HeadingDeg + s.RelativeBearingDeg) % 360 + 360) % 360;
            observations.Add(new BearingObservation(here.Lat, here.Lon, trueBearing));
        }
        var fix = BearingFix.Solve(observations);
        return new EmitterFixDto(
            emitterId, fix.Resolved, fix.Lat, fix.Lon, fix.MajorM, fix.MinorM, fix.AzimuthDeg, fix.Count, fix.SpreadDeg, fix.RmsResidualDeg, fix.Reason,
            (float)slices.Average(s => s.FrequencyMHz),
            (float)slices.Average(s => s.PriUs),
            (float)slices.Average(s => s.PulseWidthUs),
            slices[0].OffsetMs,
            slices[^1].OffsetMs);
    }

    /// <summary>Position at <paramref name="ms"/> and the track heading there (from the neighbouring samples); null outside the track or when stationary.</summary>
    private static (double Lat, double Lon, double HeadingDeg)? Sample(FlightTrack track, long ms)
    {
        var t = track.T;
        if (t.Length < 2 || ms < t[0] || ms > t[^1])
            return null;
        var i = Array.BinarySearch(t, ms);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, t.Length - 2);
        var f = (double)(ms - t[i]) / Math.Max(1, t[i + 1] - t[i]);
        var lat = track.Lat[i] + (track.Lat[i + 1] - track.Lat[i]) * f;
        var lon = track.Lon[i] + (track.Lon[i + 1] - track.Lon[i]) * f;
        if (track.Lat[i] == track.Lat[i + 1] && track.Lon[i] == track.Lon[i + 1])
            return null;
        return (lat, lon, Bearing(track.Lat[i], track.Lon[i], track.Lat[i + 1], track.Lon[i + 1]));
    }

    private static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        double φ1 = lat1 * rad, φ2 = lat2 * rad, Δλ = (lon2 - lon1) * rad;
        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        return (Math.Atan2(y, x) / rad + 360) % 360;
    }
}
