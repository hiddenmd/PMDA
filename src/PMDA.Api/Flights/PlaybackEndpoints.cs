using Microsoft.AspNetCore.Http.HttpResults;
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
        return app;
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
