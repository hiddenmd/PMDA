using System.Data.SqlTypes;
using Microsoft.Data.SqlClient;
using PMDA.Api.Contracts;
using PMDA.Api.Database;
using PMDA.Data;

namespace PMDA.Api.Flights;

/// <summary>
/// The flight list across every enabled database: each is asked for the first page × pageSize rows with the same
/// filters and sort, the answers are merged in the API and the page is cut from the merge. A database that fails is
/// reported, not fatal.
/// </summary>
public sealed class FlightCatalog(FlightStores stores, ILogger<FlightCatalog> logger)
{
    /// <summary>Deepest row a merged page may reach: every database returns up to this many rows.</summary>
    public const int MaxDepth = 10_000;

    public async Task<FlightPageDto> ListAsync(FlightQuery query, string? databaseId, CancellationToken ct)
    {
        var targets = databaseId is null ? stores.Enabled : stores.Enabled.Where(s => s.Id == databaseId).ToList();
        var depth = query.Page * query.PageSize;
        var top = query with { Page = 1, PageSize = depth };

        var answers = await Task.WhenAll(targets.Select(async store =>
        {
            try
            {
                return (store, page: await store.Flights.ListAsync(top, ct), error: (string?)null);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Database {Database} could not be listed", store.Name);
                return (store, page: (PagedResult<FlightRecord>?)null, error: ex.Message);
            }
        }));

        var rows = answers
            .Where(a => a.page is not null)
            .SelectMany(a => a.page!.Items.Select(f => (flight: f, a.store)))
            .ToList();
        rows.Sort((a, b) => FlightOrder.Compare(a.flight, b.flight, query.Sort));
        foreach (var (flight, store) in rows)
            stores.Remember(flight.FlightId, store.Id);

        var items = rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(r => FlightSummaryDto.From(r.flight, r.store)).ToList();
        var total = answers.Where(a => a.page is not null).Sum(a => a.page!.Total);
        var unavailable = answers.Where(a => a.error is not null).Select(a => new UnavailableDatabaseDto(a.store.Id, a.store.Name, a.error!))
            .Concat(stores.All.Where(s => s.Entry.Enabled && !s.Usable && (databaseId is null || s.Id == databaseId))
                .Select(s => new UnavailableDatabaseDto(s.Id, s.Name, s.Error ?? "Unavailable.")))
            .ToList();
        return new FlightPageDto(items, query.Page, query.PageSize, total, unavailable);
    }

    /// <summary>Customers with flights in any reachable database.</summary>
    public async Task<string[]> CustomersAsync(CancellationToken ct)
    {
        var lists = await Task.WhenAll(stores.Enabled.Select(async store =>
        {
            try
            {
                return await store.Flights.CustomersAsync(ct);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Database {Database} could not list customers", store.Name);
                return [];
            }
        }));
        return lists.SelectMany(l => l).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

/// <summary>The flight list's sort orders in C#, matching <c>FlightRepository</c>'s ORDER BY (ties by FlightId as SQL orders it).</summary>
public static class FlightOrder
{
    public static int Compare(FlightRecord a, FlightRecord b, FlightSort sort)
    {
        var c = sort switch
        {
            FlightSort.ImportedNewest => b.ImportedUtc.CompareTo(a.ImportedUtc),
            FlightSort.StartNewest => b.StartUtc.CompareTo(a.StartUtc),
            FlightSort.StartOldest => a.StartUtc.CompareTo(b.StartUtc),
            FlightSort.Tail => StringComparer.OrdinalIgnoreCase.Compare(a.Tail, b.Tail) is var t and not 0 ? t : b.StartUtc.CompareTo(a.StartUtc),
            FlightSort.MostEvents => b.EventCount.CompareTo(a.EventCount),
            _ => 0,
        };
        return c != 0 ? c : new SqlGuid(a.FlightId).CompareTo(new SqlGuid(b.FlightId));
    }
}
