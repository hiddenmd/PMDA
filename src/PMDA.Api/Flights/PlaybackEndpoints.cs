using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using PMDA.Api.Contracts;
using PMDA.Data;

namespace PMDA.Api.Flights;

public static class PlaybackEndpoints
{
    public const long MinStepMs = 10;
    public const long MaxStepMs = 3_600_000;
    public const int MaxBuckets = 4_000;

    public static IEndpointRouteBuilder MapPlayback(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/flights/{id:guid}").WithFlightStore().WithTags("Playback");

        group.MapGet("/frames", Frames).WithName("GetPlaybackFrames")
            .WithSummary("Events summarised per time step, for playback. Offsets are ms from the flight start.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/overview", Overview).WithName("GetFlightOverview")
            .WithSummary("Event counts per type in equal buckets across the whole flight.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/track", Track).WithName("GetFlightTrack")
            .WithSummary("The navigation track, downsampled to at most `points` positions.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/alerts", Alerts).WithName("GetFlightAlerts")
            .WithSummary("Aural warnings (new threat, lock, missile launch, missile approach, countermeasures low/out) in time order.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/emitter-fixes", EmitterFixes).WithName("GetEmitterFixes")
            .WithSummary("ESTIMATED fixed-emitter sites from RWR bearings (bearing-only least squares, 95% ellipses); unresolved emitters say why.")
            .ProducesProblem(StatusCodes.Status404NotFound);
  

        app.MapGet("/api/map", (IOptions<MapOptions> options) =>
            {
                var m = options.Value;
                var pmtiles = m.Source == MapSource.PMTiles && File.Exists(m.PmtilesPath) ? "/api/map/tiles.pmtiles" : null;
                var terrain = string.IsNullOrWhiteSpace(m.TerrainUrl) ? null : m.TerrainUrl;
                return TypedResults.Ok(new MapSettingsDto(
                    m.Source.ToString(),
                    m.Source == MapSource.OpenFreeMap ? m.TileJsonUrl : null,
                    string.IsNullOrWhiteSpace(m.GlyphsUrl) ? null : m.GlyphsUrl,
                    pmtiles,
                    terrain,
                    m.Attribution,
                    m.MaxZoom));
            })
            .WithTags("Playback").WithName("GetMapSettings").WithSummary("Base-map source, glyphs and terrain for the map panel.");

        app.MapGet("/api/map/tiles.pmtiles", Results<PhysicalFileHttpResult, NotFound> (IOptions<MapOptions> options) =>
                options.Value is { Source: MapSource.PMTiles, PmtilesPath: { } path } && File.Exists(path)
                    ? TypedResults.PhysicalFile(path, "application/octet-stream", enableRangeProcessing: true)
                    : TypedResults.NotFound())
            .WithTags("Playback").WithName("GetMapTiles").WithSummary("The configured offline PMTiles base map (HTTP range requests).");
  
        return app;
    }

    private static async Task<Results<Ok<EmitterFixDto[]>, NotFound>> EmitterFixes(Guid id, EmitterFixService fixes, CancellationToken ct) =>
        await fixes.GetAsync(id, ct) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
  

    private static async Task<Results<Ok<FlightTrackDto>, ValidationProblem, NotFound>> Track(
        Guid id, PlaybackRepository playback, CancellationToken ct, int points = 4_000)
    {
        if (points is < 2 or > PlaybackRepository.MaxTrackPoints)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(points)] = [$"Must be between 2 and {PlaybackRepository.MaxTrackPoints}."] });

        var track = await playback.GetTrackAsync(id, points, ct);
        return track is null ? TypedResults.NotFound() : TypedResults.Ok(FlightTrackDto.From(track));
    }

    private static async Task<Results<Ok<AlertDto[]>, NotFound>> Alerts(Guid id, PlaybackRepository playback, CancellationToken ct)
    {
        var alerts = await playback.GetAlertsAsync(id, ct);
        return alerts is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(alerts.Select(a => new AlertDto(a.OffsetMs, a.Kind, a.EmitterId, a.BearingDeg)).ToArray());
    }

    private static async Task<Results<Ok<PlaybackFramesDto>, ValidationProblem, NotFound>> Frames(
        Guid id, long fromMs, long toMs, long stepMs, PlaybackRepository playback, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (fromMs < 0)
            errors[nameof(fromMs)] = ["Must be 0 or more."];
        if (toMs <= fromMs)
            errors[nameof(toMs)] = ["Must be greater than fromMs."];
        if (stepMs is < MinStepMs or > MaxStepMs)
            errors[nameof(stepMs)] = [$"Must be between {MinStepMs} and {MaxStepMs}."];
        else if (toMs > fromMs && (toMs - fromMs + stepMs - 1) / stepMs > PlaybackRepository.MaxStepsPerRequest)
            errors[nameof(stepMs)] = [$"At most {PlaybackRepository.MaxStepsPerRequest} steps per request."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var frames = await playback.GetFramesAsync(id, fromMs, toMs, stepMs, ct);
        return frames is null ? TypedResults.NotFound() : TypedResults.Ok(PlaybackFramesDto.From(fromMs, toMs, stepMs, frames));
    }

    private static async Task<Results<Ok<FlightOverviewDto>, ValidationProblem, NotFound>> Overview(
        Guid id, PlaybackRepository playback, CancellationToken ct, int buckets = 400)
    {
        if (buckets is < 1 or > MaxBuckets)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(buckets)] = [$"Must be between 1 and {MaxBuckets}."] });

        var overview = await playback.GetOverviewAsync(id, buckets, ct);
        return overview is null ? TypedResults.NotFound() : TypedResults.Ok(FlightOverviewDto.From(overview));
    }
}
