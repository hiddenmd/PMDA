using Microsoft.AspNetCore.Http.HttpResults;
using PMDA.Api.Contracts;

namespace PMDA.Api.Database;

public static class DatabaseEndpoints
{
    public static IEndpointRouteBuilder MapDatabase(this IEndpointRouteBuilder app)
    {
        // Anonymous at the routing level: before a database exists nobody can sign in. Each handler checks who may configure.
        var group = app.MapGroup("/api/database").WithTags("Database").AllowAnonymous();
        group.MapGet("", Status).WithName("GetDatabaseStatus")
            .WithSummary("The database connection's state, and its settings for those allowed to change them.");
        group.MapPost("/test", Test).WithName("TestDatabase")
            .WithSummary("Try a connection without saving it.")
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapPut("", Save).WithName("SaveDatabase")
            .WithSummary("Test, prepare the schema, save and switch to a connection.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static async Task<Ok<DatabaseStatusDto>> Status(HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        var (state, message) = await setup.StateAsync(ct);
        var can = setup.CanConfigure(http, state);
        return TypedResults.Ok(new DatabaseStatusDto(state, can, setup.Source, can ? setup.CurrentSettings() : null, can ? message : null));
    }

    private static async Task<Results<Ok<DatabaseTestResultDto>, ValidationProblem, ProblemHttpResult>> Test(
        DatabaseSetupRequest request, HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        if (await Forbidden(http, setup, ct) is { } forbidden)
            return forbidden;
        var errors = setup.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(await setup.TestAsync(request, ct));
    }

    private static async Task<Results<Ok<DatabaseStatusDto>, ValidationProblem, ProblemHttpResult>> Save(
        DatabaseSetupRequest request, HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        if (await Forbidden(http, setup, ct) is { } forbidden)
            return forbidden;
        var errors = setup.Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        return await setup.SaveAsync(request, ct) switch
        {
            SaveOutcome.Busy => TypedResults.Problem("Imports are running. Wait for them to finish, then change the database.", statusCode: StatusCodes.Status409Conflict),
            SaveOutcome.Failed f => TypedResults.Problem(f.Message, title: "Could not use this database", statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.Ok(new DatabaseStatusDto(DatabaseState.Ready, setup.CanConfigure(http, DatabaseState.Ready), setup.Source, setup.CurrentSettings(), null)),
        };
    }

    private static async Task<ProblemHttpResult?> Forbidden(HttpContext http, DatabaseSetupService setup, CancellationToken ct)
    {
        var (state, _) = await setup.StateAsync(ct);
        if (setup.CanConfigure(http, state))
            return null;
        return TypedResults.Problem(
            state == DatabaseState.Ready ? "Only administrators can change the database." : "Set up the database on the server machine itself.",
            statusCode: StatusCodes.Status403Forbidden);
    }
}
