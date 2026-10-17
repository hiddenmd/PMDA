using Microsoft.AspNetCore.Http.HttpResults;

namespace PMDA.Api.Symbology;

public sealed record SymbologySummaryDto(string Id, string Name, Platform Platform);

public static class SymbologyEndpoints
{
    public static IEndpointRouteBuilder MapSymbology(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/symbology").WithTags("Symbology");
        group.MapGet("/", List).WithName("ListSymbologyProfiles").WithSummary("Available RWR symbology profiles.");
        group.MapGet("/{id}", Get).WithName("GetSymbologyProfile").WithSummary("One symbology profile by id.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/customer/{customerId}", ForCustomer).WithName("GetCustomerSymbology")
            .WithSummary("The profile a customer's flights use (the default when the customer has none).");
        return app;
    }

    private static Ok<SymbologySummaryDto[]> List(SymbologyCatalog catalog) =>
        TypedResults.Ok(catalog.All.OrderBy(p => p.Name).Select(p => new SymbologySummaryDto(p.Id, p.Name, p.Platform)).ToArray());

    private static Results<Ok<SymbologyProfile>, NotFound> Get(string id, SymbologyCatalog catalog) =>
        catalog.Find(id) is { } profile ? TypedResults.Ok(profile) : TypedResults.NotFound();

    private static Ok<SymbologyProfile> ForCustomer(string customerId, SymbologyCatalog catalog) => TypedResults.Ok(catalog.ForCustomer(customerId));
}
