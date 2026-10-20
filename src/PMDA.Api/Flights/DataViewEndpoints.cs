using Microsoft.AspNetCore.Http.HttpResults;
using PMDA.Api.Contracts;
using PMDA.Core.Events;
using PMDA.Data;

namespace PMDA.Api.Flights;

/// <summary>Message tables and graph data for one flight. Offsets are ms from the flight start.</summary>
public static class DataViewEndpoints
{
    public const int DefaultBuckets = 1_500;

    public static IEndpointRouteBuilder MapDataViews(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/flights/{id:guid}").WithFlightStore().WithTags("Data views");

        group.MapGet("/events", Events).WithName("GetFlightEvents")
            .WithSummary("Raw events of one type in a time window (max 1 h), paged by sequence number.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var series = group.MapGroup("/series");
        series.MapGet("/parameter", Parameter).WithName("GetParameterSeries")
            .WithSummary("An emitter parameter over time, per emitter, downsampled to buckets.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        series.MapGet("/scatter", Scatter).WithName("GetScatterSeries")
            .WithSummary("One emitter parameter against another, per emitter and bucket.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        series.MapGet("/profile", Profile).WithName("GetProfileSeries")
            .WithSummary("Altitude over time with countermeasure releases.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        series.MapGet("/rate", Rate).WithName("GetRateSeries")
            .WithSummary("Events per bucket by type.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Results<Ok<EventPageDto>, ValidationProblem, NotFound>> Events(
        Guid id, EventType type, long fromMs, long toMs, EventRepository events, CancellationToken ct, long afterSeq = -1, int limit = 2_000)
    {
        var errors = new Dictionary<string, string[]>();
        if (fromMs < 0)
            errors[nameof(fromMs)] = ["Must be 0 or more."];
        if (toMs <= fromMs || toMs - fromMs > EventRepository.MaxWindowMs)
            errors[nameof(toMs)] = [$"Must be after fromMs and at most {EventRepository.MaxWindowMs} ms later."];
        if (limit is < 1 or > EventRepository.MaxLimit)
            errors[nameof(limit)] = [$"Must be between 1 and {EventRepository.MaxLimit}."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var page = await events.GetAsync(id, type, fromMs, toMs, afterSeq, limit, ct);
        return page is null ? TypedResults.NotFound() : TypedResults.Ok(EventPageDto.From(type, fromMs, toMs, page));
    }

    private static async Task<Results<Ok<ParameterSeriesDto>, ValidationProblem, NotFound>> Parameter(
        Guid id, SeriesParam param, SeriesRepository series, CancellationToken ct, long? fromMs = null, long? toMs = null, int buckets = DefaultBuckets)
    {
        if (Validate(fromMs, toMs, buckets) is { } problem)
            return problem;
        var result = await series.ParameterAsync(id, param, fromMs, toMs, buckets, ct);
        if (result is not var (window, data))
            return TypedResults.NotFound();
        return TypedResults.Ok(new ParameterSeriesDto(param, SeriesWindowDto.From(window),
            data.Select(s => new EmitterSeriesDto(s.EmitterId, s.T, s.V)).ToArray()));
    }

    private static async Task<Results<Ok<ScatterSeriesDto>, ValidationProblem, NotFound>> Scatter(
        Guid id, SeriesParam x, SeriesParam y, SeriesRepository series, CancellationToken ct, long? fromMs = null, long? toMs = null, int buckets = DefaultBuckets)
    {
        if (Validate(fromMs, toMs, buckets) is { } problem)
            return problem;
        var result = await series.ScatterAsync(id, x, y, fromMs, toMs, buckets, ct);
        if (result is not var (window, data))
            return TypedResults.NotFound();
        return TypedResults.Ok(new ScatterSeriesDto(x, y, SeriesWindowDto.From(window),
            data.Select(s => new EmitterScatterDto(s.EmitterId, s.X, s.Y)).ToArray()));
    }

    private static async Task<Results<Ok<ProfileSeriesDto>, ValidationProblem, NotFound>> Profile(
        Guid id, SeriesRepository series, CancellationToken ct, long? fromMs = null, long? toMs = null, int buckets = DefaultBuckets)
    {
        if (Validate(fromMs, toMs, buckets) is { } problem)
            return problem;
        var result = await series.ProfileAsync(id, fromMs, toMs, buckets, ct);
        if (result is not var (window, data))
            return TypedResults.NotFound();
        return TypedResults.Ok(new ProfileSeriesDto(SeriesWindowDto.From(window), data.T, data.AltM,
            data.Countermeasures.Select(c => new CountermeasureMarkDto(c.T, c.Kind, c.Count)).ToArray()));
    }

    private static async Task<Results<Ok<RateSeriesDto>, ValidationProblem, NotFound>> Rate(
        Guid id, SeriesRepository series, CancellationToken ct, long? fromMs = null, long? toMs = null, int buckets = 300)
    {
        if (Validate(fromMs, toMs, buckets) is { } problem)
            return problem;
        var result = await series.RateAsync(id, fromMs, toMs, buckets, ct);
        if (result is not var (window, data))
            return TypedResults.NotFound();
        return TypedResults.Ok(new RateSeriesDto(SeriesWindowDto.From(window), data.T,
            data.Counts.OrderBy(c => c.Key).Select(c => new RateSeriesItemDto(c.Key, c.Value)).ToArray()));
    }

    private static ValidationProblem? Validate(long? fromMs, long? toMs, int buckets)
    {
        var errors = new Dictionary<string, string[]>();
        if (fromMs < 0)
            errors[nameof(fromMs)] = ["Must be 0 or more."];
        if (fromMs is not null && toMs is not null && toMs <= fromMs)
            errors[nameof(toMs)] = ["Must be after fromMs."];
        if (buckets is < 1 or > SeriesRepository.MaxBuckets)
            errors[nameof(buckets)] = [$"Must be between 1 and {SeriesRepository.MaxBuckets}."];
        return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : null;
    }
}
