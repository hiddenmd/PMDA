using Microsoft.Data.SqlClient;
using PMDA.Api.Database;

namespace PMDA.Api.Flights;

/// <summary>Marks endpoints whose route <c>{id}</c> is a flight: they run against the database holding it.</summary>
public sealed class RequiresFlightStore;

/// <summary>
/// Finds the database holding the flight in the route (<c>{id}</c>) before the endpoint runs (and before its
/// parameters are bound), so the per-request repositories point at it. 404 when no database holds the flight;
/// 503 when its database fails during the request. Runs after authorization, so anonymous callers still get 401.
/// </summary>
public sealed class FlightStoreMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, FlightStores stores)
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<RequiresFlightStore>() is null
            || http.Request.RouteValues["id"] is not string raw || !Guid.TryParse(raw, out var id))
        {
            await next(http);
            return;
        }

        var store = await stores.LocateAsync(id, http.RequestAborted);
        if (store is null)
        {
            await TypedResults.NotFound().ExecuteAsync(http);
            return;
        }
        http.Items[typeof(FlightStore)] = store;
        try
        {
            await next(http);
        }
        catch (SqlException) when (!http.Response.HasStarted)
        {
            await TypedResults.Problem($"The database '{store.Name}' holding this flight is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(http);
        }
    }

    /// <summary>The database of the flight this request is about (set by the middleware).</summary>
    public static FlightStore Current(IServiceProvider services) =>
        services.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[typeof(FlightStore)] as FlightStore
        ?? throw new InvalidOperationException("No flight database was resolved for this request: mark the endpoint WithFlightStore().");
}

public static class FlightStoreExtensions
{
    /// <summary>Resolves <c>{id}</c> to the database holding that flight for the endpoint (or every endpoint in the group).</summary>
    public static TBuilder WithFlightStore<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequiresFlightStore());
}
