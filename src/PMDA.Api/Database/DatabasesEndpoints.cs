using Microsoft.AspNetCore.Http.HttpResults;
using PMDA.Api.Auth;
using PMDA.Api.Contracts;

namespace PMDA.Api.Database;

public static class DatabasesEndpoints
{
    public static IEndpointRouteBuilder MapDatabases(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/databases").WithTags("Databases");

        // Any signed-in user: the databases to filter by and to import into.
        group.MapGet("/choices", (DatabasesService databases) => TypedResults.Ok(databases.Choices()))
            .WithName("ListDatabaseChoices").WithSummary("Enabled databases (to filter flights, and to import into unless read-only) and the import default.");

        var admin = group.MapGroup("").RequireAuthorization(Policies.Admin);
        admin.MapGet("", List).WithName("ListDatabases").WithSummary("Every registered flight database with its state.");
        admin.MapPost("/test", Test).WithName("TestDatabase").WithSummary("Try a connection without saving it.");
        admin.MapPost("", Add).WithName("AddDatabase").WithSummary("Register a flight database (creating it or its tables when asked).")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        admin.MapPut("/default", SetDefault).WithName("SetDefaultDatabase").WithSummary("Choose where imports go by default.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/{id}", Update).WithName("UpdateDatabase")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapDelete("/{id}", Remove).WithName("RemoveDatabase").WithSummary("Forget a database; it is not dropped.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static async Task<Ok<DatabaseListDto>> List(DatabasesService databases, CancellationToken ct) =>
        TypedResults.Ok(await databases.ListAsync(ct));

    private static async Task<Results<Ok<DatabaseTestResultDto>, ValidationProblem>> Test(
        DatabaseEntryRequest request, DatabasesService databases, CancellationToken ct)
    {
        var errors = databases.Validate(request with { Name = string.IsNullOrWhiteSpace(request.Name) ? "test" : request.Name, Id = null, CreateIfMissing = false },
            databases.Existing(request.Id));
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(await databases.TestAsync(request, ct));
    }

    private static async Task<Results<Ok<DatabaseListDto>, ValidationProblem, ProblemHttpResult, NotFound>> Add(
        DatabaseEntryRequest request, DatabasesService databases, CancellationToken ct)
    {
        var errors = databases.Validate(request, null);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        return await Result(await databases.AddAsync(request, ct), databases, ct);
    }

    private static async Task<Results<Ok<DatabaseListDto>, ValidationProblem, ProblemHttpResult, NotFound>> Update(
        string id, DatabaseEntryRequest request, DatabasesService databases, CancellationToken ct)
    {
        if (databases.Existing(id) is not { } existing)
            return TypedResults.NotFound();
        var errors = databases.Validate(request, existing);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        return await Result(await databases.UpdateAsync(id, request, ct), databases, ct);
    }

    private static async Task<Results<Ok<DatabaseListDto>, ValidationProblem, ProblemHttpResult, NotFound>> Remove(
        string id, DatabasesService databases, CancellationToken ct) =>
        await Result(await databases.RemoveAsync(id, ct), databases, ct);

    private static async Task<Results<Ok<DatabaseListDto>, ValidationProblem, ProblemHttpResult, NotFound>> SetDefault(
        SetDefaultDatabaseRequest request, DatabasesService databases, CancellationToken ct) =>
        await Result(await databases.SetDefaultAsync(request.Id ?? "", ct), databases, ct);

    /// <summary>Every change answers with the whole, fresh list.</summary>
    private static async Task<Results<Ok<DatabaseListDto>, ValidationProblem, ProblemHttpResult, NotFound>> Result(
        DatabaseChange change, DatabasesService databases, CancellationToken ct) => change switch
        {
            DatabaseChange.NotFound => TypedResults.NotFound(),
            DatabaseChange.Busy b => TypedResults.Problem(b.Message, statusCode: StatusCodes.Status409Conflict),
            DatabaseChange.Failed f => TypedResults.Problem(f.Message, title: "Could not use this database", statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.Ok(await databases.ListAsync(ct)),
        };
}
