using Microsoft.Data.SqlClient;
using PMDA.Api.Flights;
using PMDA.Api.Tests.Support;
using PMDA.Data;

namespace PMDA.Api.Tests.Flights;

/// <summary>The merged list sorts in C#; it must agree with SQL Server's ORDER BY, GUID tie-break included.</summary>
[Collection(SqlCollection.Name)]
public sealed class FlightOrderTests(SqlDatabase db)
{
    [Theory]
    [InlineData(FlightSort.StartNewest, "StartUtc DESC, FlightId")]
    [InlineData(FlightSort.StartOldest, "StartUtc ASC, FlightId")]
    [InlineData(FlightSort.MostEvents, "EventCount DESC, FlightId")]
    [InlineData(FlightSort.Tail, "Tail ASC, StartUtc DESC, FlightId")]
    public async Task Csharp_order_matches_sql_server(FlightSort sort, string orderBy)
    {
        var rng = new Random(42);
        var start = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);
        // Few distinct values, so most rows tie and the GUID decides.
        var flights = Enumerable.Range(0, 40).Select(_ => new FlightRecord(
            Guid.NewGuid(), "C", 1, $"TAIL-{rng.Next(3)}", "M", start.AddHours(rng.Next(3)), null, null, "f", FlightStatus.Completed, null,
            rng.Next(3), 0, start)).ToList();

        var values = string.Join(",", flights.Select((f, i) => $"(@id{i}, @t{i}, @s{i}, @e{i})"));
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT FlightId FROM (VALUES {values}) v(FlightId, Tail, StartUtc, EventCount) ORDER BY {orderBy};", conn);
        for (var i = 0; i < flights.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@id{i}", flights[i].FlightId);
            cmd.Parameters.AddWithValue($"@t{i}", flights[i].Tail);
            cmd.Parameters.Add(new SqlParameter($"@s{i}", System.Data.SqlDbType.DateTime2) { Value = flights[i].StartUtc });
            cmd.Parameters.AddWithValue($"@e{i}", flights[i].EventCount);
        }
        var sqlOrder = new List<Guid>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                sqlOrder.Add(reader.GetGuid(0));

        var csharp = flights.ToList();
        csharp.Sort((a, b) => FlightOrder.Compare(a, b, sort));
        Assert.Equal(sqlOrder, csharp.Select(f => f.FlightId));
    }
}
