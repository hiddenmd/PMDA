using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PMDA.Api.Contracts;

namespace PMDA.Api.Imports;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

        group.MapPost("/", Upload)
            .WithName("UploadImport")
            .WithSummary("Upload a flight recording and queue it for import.")
            .DisableAntiforgery() // API used by our own client and tools; no cookie auth
            .WithMetadata(new RequestSizeLimitAttribute(long.MaxValue)) // real limit enforced by FormOptions
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", List).WithName("ListImports").WithSummary("Imports known to this server, newest first.");
        group.MapGet("/{id:guid}", Get).WithName("GetImport")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{id:guid}", Cancel).WithName("CancelImport").WithSummary("Cancel a queued or running import.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapHub<ImportHub>(ImportHub.Path);
        return app;
    }

    private static async Task<Results<Accepted<ImportJobDto>, ProblemHttpResult>> Upload(
        IFormFile file, ImportService imports, IOptions<ImportsOptions> options, CancellationToken ct)
    {
        if (file.Length == 0)
            return TypedResults.Problem("The uploaded file is empty.", statusCode: StatusCodes.Status400BadRequest);
        if (file.Length > options.Value.MaxUploadBytes)
            return TypedResults.Problem($"The file is larger than {options.Value.MaxUploadBytes} bytes.", statusCode: StatusCodes.Status413PayloadTooLarge);

        await using var content = file.OpenReadStream();
        var result = await imports.SubmitAsync(content, file.FileName, file.Length, ct);
        if (result.Status == SubmitStatus.QueueFull)
            return TypedResults.Problem("The import queue is full. Try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var job = ImportJobDto.From(result.Job!.Snapshot());
        return TypedResults.Accepted($"/api/imports/{job.Id}", job);
    }

    private static Ok<ImportJobDto[]> List(ImportService imports) =>
        TypedResults.Ok(imports.List().Select(ImportJobDto.From).ToArray());

    private static Results<Ok<ImportJobDto>, NotFound> Get(Guid id, ImportService imports) =>
        imports.Find(id) is { } job ? TypedResults.Ok(ImportJobDto.From(job)) : TypedResults.NotFound();

    private static Results<Accepted<ImportJobDto>, NotFound, ProblemHttpResult> Cancel(Guid id, ImportService imports)
    {
        if (imports.Cancel(id) is not var (result, job))
            return TypedResults.NotFound();
        if (result == CancelRequest.AlreadyFinished)
            return TypedResults.Problem($"The import already finished ({job.State}).", statusCode: StatusCodes.Status409Conflict);
        return TypedResults.Accepted($"/api/imports/{id}", ImportJobDto.From(job));
    }
}
