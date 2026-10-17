using Microsoft.AspNetCore.Http.HttpResults;
using PMDA.Api.Contracts;
using PMDA.Data;

namespace PMDA.Api.Flights;

public static class FlightEndpoints
{
    public const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapFlights(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/flights").WithTags("Flights");
        group.MapGet("/", List).WithName("ListFlights").WithSummary("Imported flights, newest first.");
        group.MapGet("/{id:guid}", Get).WithName("GetFlight")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/diagnostics", Diagnostics).WithName("GetFlightDiagnostics")
            .WithSummary("Problems found while parsing the flight file.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Results<Ok<FlightPageDto>, ValidationProblem>> List(
        FlightRepository flights, CancellationToken ct,
        int page = 1, int pageSize = 50, string? customerId = null, string? tail = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
            errors[nameof(page)] = ["Must be 1 or more."];
        if (pageSize is < 1 or > MaxPageSize)
            errors[nameof(pageSize)] = [$"Must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var result = await flights.ListAsync(new FlightQuery(page, pageSize, NullIfEmpty(customerId), NullIfEmpty(tail)), ct);
        return TypedResults.Ok(new FlightPageDto(result.Items.Select(FlightSummaryDto.From).ToList(), result.Page, result.PageSize, result.Total));
    }

    private static async Task<Results<Ok<FlightDetailDto>, NotFound>> Get(Guid id, FlightRepository flights, CancellationToken ct) =>
        await flights.GetAsync(id, ct) is { } flight ? TypedResults.Ok(FlightDetailDto.From(flight)) : TypedResults.NotFound();

    private static async Task<Results<Ok<DiagnosticDto[]>, NotFound>> Diagnostics(Guid id, FlightRepository flights, CancellationToken ct)
    {
        if (await flights.GetAsync(id, ct) is null)
            return TypedResults.NotFound();
        var items = await flights.GetDiagnosticsAsync(id, ct);
        return TypedResults.Ok(items.Select(d => new DiagnosticDto(d.FileOffset, d.Message)).ToArray());
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
