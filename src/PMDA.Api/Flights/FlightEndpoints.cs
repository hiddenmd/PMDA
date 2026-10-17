using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using PMDA.Api.Auth;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
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

        group.MapDelete("/{id:guid}", Delete).WithName("DeleteFlight").WithSummary("Delete one flight and its data.")
            .RequireAuthorization(Policies.Admin)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/delete", DeleteMany).WithName("DeleteFlights").WithSummary("Delete the given flights and their data.")
            .RequireAuthorization(Policies.Admin)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/", DeleteAll).WithName("DeleteAllFlights")
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Delete every flight. Requires confirm=DELETE; refused while imports run.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{id:guid}", Get).WithName("GetFlight")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/diagnostics", Diagnostics).WithName("GetFlightDiagnostics")
            .WithSummary("Problems found while parsing the flight file.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    /// <summary>Query string of the flight list. Text filters match any part of the value.</summary>
    public sealed record FlightListRequest(
        [property: FromQuery(Name = "page")] int Page = 1,
        [property: FromQuery(Name = "pageSize")] int PageSize = 50,
        [property: FromQuery(Name = "customerId")] string? CustomerId = null,
        [property: FromQuery(Name = "tail")] string? Tail = null,
        [property: FromQuery(Name = "mission")] string? Mission = null,
        [property: FromQuery(Name = "status")] FlightStatus? Status = null,
        [property: FromQuery(Name = "startFrom")] DateTime? StartFrom = null,
        [property: FromQuery(Name = "startTo")] DateTime? StartTo = null,
        [property: FromQuery(Name = "hasProblems")] bool? HasProblems = null,
        [property: FromQuery(Name = "sort")] FlightSort Sort = FlightSort.ImportedNewest);

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

    public const int MaxDeleteBatch = 1_000;
    public const string DeleteAllConfirmation = "DELETE";

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Delete(
        Guid id, FlightRepository flights, IHubContext<ImportHub, IImportClient> hub, CancellationToken ct)
    {
        var flight = await flights.GetAsync(id, ct);
        if (flight is null)
            return TypedResults.NotFound();
        if (flight.Status == FlightStatus.Importing)
            return TypedResults.Problem("The flight is still being imported.", statusCode: StatusCodes.Status409Conflict);

        await flights.DeleteAsync([id], ct);
        await hub.Clients.All.FlightsChanged();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<DeleteResultDto>, ValidationProblem, ProblemHttpResult>> DeleteMany(
        DeleteFlightsRequest request, FlightRepository flights, IHubContext<ImportHub, IImportClient> hub, CancellationToken ct)
    {
        var ids = (request.Ids ?? []).Distinct().ToArray();
        if (ids.Length is 0 or > MaxDeleteBatch)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = [$"Give between 1 and {MaxDeleteBatch} flight ids."] });

        foreach (var id in ids)
        {
            if ((await flights.GetAsync(id, ct))?.Status == FlightStatus.Importing)
                return TypedResults.Problem($"Flight {id} is still being imported.", statusCode: StatusCodes.Status409Conflict);
        }
        var deleted = await flights.DeleteAsync(ids, ct);
        await hub.Clients.All.FlightsChanged();
        return TypedResults.Ok(new DeleteResultDto(deleted));
    }

    private static async Task<Results<Ok<DeleteResultDto>, ValidationProblem, ProblemHttpResult>> DeleteAll(
        FlightRepository flights, ImportJobStore jobs, IHubContext<ImportHub, IImportClient> hub, CancellationToken ct, string? confirm = null)
    {
        if (confirm != DeleteAllConfirmation)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["confirm"] = [$"Pass confirm={DeleteAllConfirmation} to delete every flight."] });
        if (jobs.HasActiveJobs())
            return TypedResults.Problem("Imports are running. Wait for them to finish or cancel them first.", statusCode: StatusCodes.Status409Conflict);

        var deleted = await flights.DeleteAllAsync(ct);
        await hub.Clients.All.FlightsChanged();
        return TypedResults.Ok(new DeleteResultDto(deleted));
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
