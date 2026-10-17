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
        group.MapGet("/", List).WithName("ListFlights").WithSummary("Imported flights, filtered and sorted.");
        group.MapGet("/customers", Customers).WithName("ListCustomers").WithSummary("Customers that have flights.");
        group.MapGet("/{id:guid}", Get).WithName("GetFlight")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/diagnostics", Diagnostics).WithName("GetFlightDiagnostics")
            .WithSummary("Problems found while parsing the flight file.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    /// <summary>Query string of the flight list. Text filters match any part of the value.</summary>
    public sealed record FlightListRequest(
        int Page = 1,
        int PageSize = 50,
        string? CustomerId = null,
        string? Tail = null,
        string? Mission = null,
        FlightStatus? Status = null,
        DateTime? StartFrom = null,
        DateTime? StartTo = null,
        bool? HasProblems = null,
        FlightSort Sort = FlightSort.ImportedNewest);

    private static async Task<Results<Ok<FlightPageDto>, ValidationProblem>> List(
        [AsParameters] FlightListRequest q, FlightRepository flights, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (q.Page < 1)
            errors["page"] = ["Must be 1 or more."];
        if (q.PageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        if (q.StartFrom is { } from && q.StartTo is { } to && to <= from)
            errors["startTo"] = ["Must be after startFrom."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var query = new FlightQuery(q.Page, q.PageSize, NullIfEmpty(q.CustomerId), NullIfEmpty(q.Tail), NullIfEmpty(q.Mission),
            q.Status, Utc(q.StartFrom), Utc(q.StartTo), q.HasProblems, q.Sort);
        var result = await flights.ListAsync(query, ct);
        return TypedResults.Ok(new FlightPageDto(result.Items.Select(FlightSummaryDto.From).ToList(), result.Page, result.PageSize, result.Total));
    }

    private static async Task<Ok<string[]>> Customers(FlightRepository flights, CancellationToken ct) =>
        TypedResults.Ok((await flights.CustomersAsync(ct)).ToArray());

    /// <summary>Dates without an offset are taken as UTC, like every time in PMDA.</summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };

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
