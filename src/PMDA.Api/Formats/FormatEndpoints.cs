using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using PMDA.Api.Auth;
using PMDA.Api.Contracts;
using PMDA.Formats.Demo;
using PMDA.Formats.Dialect;
using PMDA.Formats.Projects;
using PMDA.Formats.Recipes;

namespace PMDA.Api.Formats;

/// <summary>Header-defined recording formats: one project per recorder, its vendor .h versions, recipe and event rules.</summary>
public static class FormatEndpoints
{
    private const long MaxHeaderBytes = 8 << 20;

    public static IEndpointRouteBuilder MapFormats(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/formats").WithTags("Formats");

        // Any signed-in user: the formats to choose from when importing.
        group.MapGet("", List).WithName("ListFormats").WithSummary("Header-defined recording formats.");

        var admin = group.MapGroup("").RequireAuthorization(Policies.Admin);
        admin.MapGet("/{id}", Get).WithName("GetFormat").WithSummary("A format with its header report, messages and tables.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapGet("/{id}/header", GetHeader).WithName("GetFormatHeader").WithSummary("The text of one header version (default: the latest).");
        admin.MapPost("", Create).WithName("CreateFormat").WithSummary("Start a new project.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/demo", CreateDemo).WithName("CreateDemoFormat").WithSummary("Add the invented demo project (header and recipe).");
        admin.MapPut("/{id}/header", UploadHeader).WithName("UploadFormatHeader")
            .WithSummary("Upload a vendor .h; it becomes the next version. The answer compares it with the previous one.")
            .DisableAntiforgery()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/{id}", SaveProject).WithName("SaveFormat").WithSummary("Save settings, recipe and event rules.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/{id}/dry-run", DryRun).WithName("DryRunFormat")
            .WithSummary("Decode a recording with the current header and recipe without storing anything.")
            .DisableAntiforgery()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/{id}", Delete).WithName("DeleteFormat").WithSummary("Forget a project; tables already created stay in the databases.");
        return app;
    }

    private static Ok<FormatSummaryDto[]> List(FormatProjectStore store) =>
        TypedResults.Ok(store.List().Select(p =>
        {
            var current = store.Current(p.Id);
            var diagnostics = current?.Format.Diagnostics ?? [];
            return new FormatSummaryDto(p.Id, p.Name, current?.Version, current?.Format.CanImport ?? false,
                current?.Format.StoredTables.Count() ?? 0,
                diagnostics.Count(d => d.Severity == Severity.Error),
                diagnostics.Count(d => d.Severity == Severity.Warning));
        }).ToArray());

    private static FormatDetailDto Detail(FormatProjectStore store, FormatProject project)
    {
        var current = store.Current(project.Id);
        var versions = store.Versions(project.Id).Select(v => new FormatVersionDto(v.Version, v.Sha256, v.Bytes, v.SavedUtc));
        return FormatDtoMapping.Detail(project, current?.Version, versions, current?.Format);
    }

    private static Results<Ok<FormatDetailDto>, NotFound> Get(string id, FormatProjectStore store) =>
        store.Find(id) is { } project ? TypedResults.Ok(Detail(store, project)) : TypedResults.NotFound();

    private static Results<ContentHttpResult, NotFound> GetHeader(string id, int? version, FormatProjectStore store)
    {
        var v = version ?? store.Versions(id).LastOrDefault()?.Version;
        return v is { } n && store.Header(id, n) is { } text ? TypedResults.Text(text, "text/plain", Encoding.UTF8) : TypedResults.NotFound();
    }

    private static Results<Created<FormatDetailDto>, ProblemHttpResult> Create(CreateFormatRequest request, FormatProjectStore store)
    {
        var id = request.Id?.Trim() ?? "";
        if (!FormatProjectStore.IdPattern().IsMatch(id))
            return TypedResults.Problem("The id is 1–32 letters, digits, '-' or '_', for example PROJ-A.", statusCode: StatusCodes.Status400BadRequest);
        if (store.Find(id) is not null)
            return TypedResults.Problem($"There is already a project '{id}'.", statusCode: StatusCodes.Status409Conflict);
        var name = string.IsNullOrWhiteSpace(request.Name) ? id : request.Name.Trim();
        var project = new FormatProject { Id = id, Name = name };
        store.Save(project);
        return TypedResults.Created($"/api/formats/{id}", Detail(store, project));
    }

    private static Ok<FormatDetailDto> CreateDemo(FormatProjectStore store)
    {
        var project = DemoFormat.Project;
        if (store.Find(project.Id) is null)
            store.Save(project);
        store.AddVersion(project.Id, DemoFormat.Header);
        return TypedResults.Ok(Detail(store, store.Find(project.Id)!));
    }

    private static async Task<Results<Ok<FormatUploadResultDto>, NotFound, ProblemHttpResult>> UploadHeader(
        string id, IFormFile file, FormatProjectStore store, CancellationToken ct)
    {
        if (store.Find(id) is not { } project)
            return TypedResults.NotFound();
        if (file.Length == 0 || file.Length > MaxHeaderBytes)
            return TypedResults.Problem($"Upload a header file of at most {MaxHeaderBytes >> 20} MB.", statusCode: StatusCodes.Status400BadRequest);
        string text;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = await reader.ReadToEndAsync(ct);
        if (text.Contains('\0'))
            return TypedResults.Problem("That is not a text file.", statusCode: StatusCodes.Status400BadRequest);

        var before = store.Current(id);
        var version = store.AddVersion(id, text);
        var after = store.Current(id)!;
        var diff = before is not null && before.Version != version ? FormatDiff.Compare(before.Format, after.Format) : null;
        return TypedResults.Ok(new FormatUploadResultDto(Detail(store, project), diff, before?.Version != version));
    }

    private static Results<Ok<FormatDetailDto>, NotFound, ProblemHttpResult> SaveProject(string id, FormatProject project, FormatProjectStore store)
    {
        if (store.Find(id) is null)
            return TypedResults.NotFound();
        if (!string.Equals(project.Id, id, StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem("The project id cannot change.", statusCode: StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(project.Name))
            return TypedResults.Problem("The project needs a name.", statusCode: StatusCodes.Status400BadRequest);
        foreach (var rule in project.Events)
        {
            foreach (var (property, expr) in rule.Fields)
            {
                if (!RecipeEngine.EventProperties.Contains(property))
                    return TypedResults.Problem($"Event rule {rule.Kind}: '{property}' is not an event field ({string.Join(", ", RecipeEngine.EventProperties)}).", statusCode: StatusCodes.Status400BadRequest);
                try { FieldExpr.Parse(expr); }
                catch (FormatException ex) { return TypedResults.Problem($"Event rule {rule.Kind}: {ex.Message}", statusCode: StatusCodes.Status400BadRequest); }
            }
            if (rule.When is { Length: > 0 } when)
            {
                try { foreach (var part in when.Split("&&")) Condition.Parse(part); }
                catch (FormatException ex) { return TypedResults.Problem($"Event rule {rule.Kind}: {ex.Message}", statusCode: StatusCodes.Status400BadRequest); }
            }
        }
        if (project.Recipe.Nav is { } nav)
            foreach (var expr in nav.Fields.Values)
            {
                try { FieldExpr.Parse(expr); }
                catch (FormatException ex) { return TypedResults.Problem($"Nav source: {ex.Message}", statusCode: StatusCodes.Status400BadRequest); }
            }
        store.Save(project with { Id = id });
        return TypedResults.Ok(Detail(store, store.Find(id)!));
    }

    private static async Task<Results<Ok<DryRunDto>, NotFound, ProblemHttpResult>> DryRun(
        string id, IFormFile file, FormatProjectStore store, CancellationToken ct)
    {
        if (store.Find(id) is null)
            return TypedResults.NotFound();
        if (store.Current(id) is not { } current)
            return TypedResults.Problem("Upload the project's header first.", statusCode: StatusCodes.Status400BadRequest);
        if (!current.Format.CanImport)
            return TypedResults.Problem("The header or recipe has errors; fix them first.", statusCode: StatusCodes.Status400BadRequest);

        // The decode needs to seek (flight info is read ahead), so the upload is spooled to a temporary file.
        var temp = Path.Combine(Path.GetTempPath(), $"pmda-dry-{Guid.NewGuid():N}.bin");
        try
        {
            await using (var spool = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                await file.CopyToAsync(spool, ct);
            await using var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            var report = await Task.Run(() => FlightRun.DryRun(current.Format, stream, ct), ct);
            return TypedResults.Ok(DryRunDto.From(report));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static Results<NoContent, NotFound> Delete(string id, FormatProjectStore store) =>
        store.Delete(id) ? TypedResults.NoContent() : TypedResults.NotFound();
}
