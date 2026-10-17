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
        var group = app.MapGroup("/api/flights/{id:guid}").WithTags("Playback");

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

        app.MapGet("/api/map", (IOptions<MapOptions> map) =>
                TypedResults.Ok(new MapSettingsDto(string.IsNullOrWhiteSpace(map.Value.TileUrl) ? null : map.Value.TileUrl, map.Value.Attribution, map.Value.MaxZoom)))
            .WithTags("Playback").WithName("GetMapSettings").WithSummary("Base-map tile source for the map panel.");
        return app;
    }

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
