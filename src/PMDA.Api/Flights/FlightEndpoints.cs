using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using PMDA.Api.Auth;
using PMDA.Api.Contracts;
using PMDA.Api.Database;
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
            .WithFlightStore()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/delete", DeleteMany).WithName("DeleteFlights").WithSummary("Delete the given flights and their data.")
            .RequireAuthorization(Policies.Admin)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/", DeleteAll).WithName("DeleteAllFlights")
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Delete every flight in one database. Requires databaseId and confirm=DELETE; refused while imports run.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{id:guid}", Get).WithName("GetFlight")
            .WithFlightStore()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/diagnostics", Diagnostics).WithName("GetFlightDiagnostics")
            .WithFlightStore()
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
        [property: FromQuery(Name = "sort")] FlightSort Sort = FlightSort.ImportedNewest,
        [property: FromQuery(Name = "databaseId")] string? DatabaseId = null);

    private static async Task<Results<Ok<FlightPageDto>, ValidationProblem>> List(
        [AsParameters] FlightListRequest q, FlightCatalog catalog, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (q.Page < 1)
            errors["page"] = ["Must be 1 or more."];
        if (q.PageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        if (q.Page >= 1 && q.PageSize is >= 1 and <= MaxPageSize && (long)q.Page * q.PageSize > FlightCatalog.MaxDepth)
            errors["page"] = [$"Pages beyond row {FlightCatalog.MaxDepth} are not listed: narrow the filters or change the sort."];
        if (q.StartFrom is { } from && q.StartTo is { } to && to <= from)
            errors["startTo"] = ["Must be after startFrom."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var query = new FlightQuery(q.Page, q.PageSize, NullIfEmpty(q.CustomerId), NullIfEmpty(q.Tail), NullIfEmpty(q.Mission),
            q.Status, Utc(q.StartFrom), Utc(q.StartTo), q.HasProblems, q.Sort);
        return TypedResults.Ok(await catalog.ListAsync(query, NullIfEmpty(q.DatabaseId), ct));
    }

    public const int MaxDeleteBatch = 1_000;
    public const string DeleteAllConfirmation = "DELETE";

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Delete(
        Guid id, FlightStore store, FlightPurger purger, IHubContext<ImportHub, IImportClient> hub, CancellationToken ct)
    {
        if (store.ReadOnly)
            return ReadOnly(store);
        var flights = store.Flights;
        var flight = await flights.GetAsync(id, ct);
        if (flight is null)
            return TypedResults.NotFound();
        if (flight.Status == FlightStatus.Importing)
            return TypedResults.Problem("The flight is still being imported.", statusCode: StatusCodes.Status409Conflict);

        await flights.DeleteAsync([id], ct);
        purger.Wake();
        await hub.Clients.All.FlightsChanged();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<DeleteResultDto>, ValidationProblem, ProblemHttpResult>> DeleteMany(
        DeleteFlightsRequest request, FlightStores stores, FlightPurger purger, IHubContext<ImportHub, IImportClient> hub, CancellationToken ct)
    {
        var ids = (request.Ids ?? []).Distinct().ToArray();
        if (ids.Length is 0 or > MaxDeleteBatch)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = [$"Give between 1 and {MaxDeleteBatch} flight ids."] });

        // Each id to its database; unknown ids are ignored, as before.
        var byStore = new Dictionary<FlightStore, List<Guid>>();
        foreach (var id in ids)
        {
            if (await stores.LocateAsync(id, ct) is not { } store)
                continue;
            if (store.ReadOnly)
                return ReadOnly(store);
            if ((await store.Flights.GetAsync(id, ct))?.Status == FlightStatus.Importing)
                return TypedResults.Problem($"Flight {id} is still being imported.", statusCode: StatusCodes.Status409Conflict);
            if (!byStore.TryGetValue(store, out var list))
                byStore[store] = list = [];
            list.Add(id);
        }
        var deleted = 0;
        foreach (var (store, storeIds) in byStore)
            deleted += await store.Flights.DeleteAsync(storeIds, ct);
        purger.Wake();
        await hub.Clients.All.FlightsChanged();
        return TypedResults.Ok(new DeleteResultDto(deleted));
    }

    private static async Task<Results<Ok<DeleteResultDto>, ValidationProblem, ProblemHttpResult>> DeleteAll(
        FlightStores stores, ImportJobStore jobs, IHubContext<ImportHub, IImportClient> hub, CancellationToken ct,
        string? confirm = null, string? databaseId = null)
    {
        if (confirm != DeleteAllConfirmation)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["confirm"] = [$"Pass confirm={DeleteAllConfirmation} to delete every flight."] });
        if (stores.Get(databaseId) is not { Usable: true } store)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["databaseId"] = ["Name the database whose flights to delete."] });
        if (store.ReadOnly)
            return ReadOnly(store);
        if (jobs.HasActiveJobs())
            return TypedResults.Problem("Imports are running. Wait for them to finish or cancel them first.", statusCode: StatusCodes.Status409Conflict);

        var deleted = await store.Flights.DeleteAllAsync(ct);
        await hub.Clients.All.FlightsChanged();
        return TypedResults.Ok(new DeleteResultDto(deleted));
    }

    private static async Task<Ok<string[]>> Customers(FlightCatalog catalog, CancellationToken ct) =>
        TypedResults.Ok(await catalog.CustomersAsync(ct));

    private static ProblemHttpResult ReadOnly(FlightStore store) =>
        TypedResults.Problem($"'{store.Name}' is read-only.", statusCode: StatusCodes.Status409Conflict);

    /// <summary>Dates without an offset are taken as UTC, like every time in PMDA.</summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };

    private static async Task<Results<Ok<FlightDetailDto>, NotFound>> Get(Guid id, FlightStore store, CancellationToken ct) =>
        await store.Flights.GetAsync(id, ct) is { } flight ? TypedResults.Ok(FlightDetailDto.From(flight, store)) : TypedResults.NotFound();

    private static async Task<Results<Ok<DiagnosticDto[]>, NotFound>> Diagnostics(Guid id, FlightStore store, CancellationToken ct)
    {
        var flights = store.Flights;
        if (await flights.GetAsync(id, ct) is null)
            return TypedResults.NotFound();
        var items = await flights.GetDiagnosticsAsync(id, ct);
        return TypedResults.Ok(items.Select(d => new DiagnosticDto(d.FileOffset, d.Message)).ToArray());
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
