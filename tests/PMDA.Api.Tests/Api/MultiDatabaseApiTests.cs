using System.Net;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
using PMDA.Core.Events;
using PMDA.Api.Tests.Support;
using static PMDA.Api.Tests.Support.PmdaApiFactory;
using static PMDA.Api.Tests.Support.TestFlights;

namespace PMDA.Api.Tests.Api;

/// <summary>Two flight databases at once: one list, routing by flight, imports and deletes per database.</summary>
[Collection(SqlCollection.Name)]
public sealed class MultiDatabaseApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync() => await db.ResetAsync();

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<HttpClient> Open(bool bravoReadOnly = false, bool withDownDatabase = false)
    {
        var databases = new List<(string, string, string, bool)>
        {
            ("main", "Main", db.ConnectionString, false),
            ("bravo", "Bravo", db.SecondConnectionString, bravoReadOnly),
        };
        if (withDownDatabase)
            databases.Add(("down", "Down", "Server=127.0.0.1,1;Database=PMDA_Down_Test;Integrated Security=true;Connect Timeout=2", false));
        _api.UseDatabases([.. databases]);
        return _client = await _api.CreateSignedInClientAsync();
    }

    private static FlightEvent[] Events() => [Emitter(0, 1), Emitter(100, 1), Nav(0, 44.4, 26.1, 3000), Nav(500, 44.41, 26.1, 3000)];

    [Fact]
    public async Task One_list_merges_every_database_in_sort_order()
    {
        await Open();
        var a = await StoreAsync(db.ConnectionString, Events(), tail: "A", start: Start.AddHours(1));
        var b0 = await StoreAsync(db.SecondConnectionString, Events(), tail: "B0", start: Start);
        var b2 = await StoreAsync(db.SecondConnectionString, Events(), tail: "B2", start: Start.AddHours(2));

        var page = (await _client.GetFromJsonAsync<FlightPageDto>("/api/flights?sort=StartNewest", Json))!;

        Assert.Equal([b2, a, b0], page.Items.Select(f => f.Id));
        Assert.Equal(["Bravo", "Main", "Bravo"], page.Items.Select(f => f.DatabaseName));
        Assert.Equal(3, page.Total);
        Assert.Empty(page.Unavailable);

        var second = (await _client.GetFromJsonAsync<FlightPageDto>("/api/flights?sort=StartNewest&page=2&pageSize=2", Json))!;
        Assert.Equal([b0], second.Items.Select(f => f.Id));

        var bravoOnly = (await _client.GetFromJsonAsync<FlightPageDto>("/api/flights?databaseId=bravo", Json))!;
        Assert.Equal(2, bravoOnly.Total);
        Assert.All(bravoOnly.Items, f => Assert.Equal("bravo", f.DatabaseId));
    }

    [Fact]
    public async Task A_database_that_is_down_is_reported_and_the_others_still_list()
    {
        await Open(withDownDatabase: true);
        await StoreAsync(db.SecondConnectionString, Events());

        var page = (await _client.GetFromJsonAsync<FlightPageDto>("/api/flights", Json))!;

        Assert.Single(page.Items);
        var down = Assert.Single(page.Unavailable);
        Assert.Equal(("down", "Down"), (down.Id, down.Name));
        Assert.False(string.IsNullOrEmpty(down.Message));
    }

    [Fact]
    public async Task Every_view_of_a_flight_reads_the_database_holding_it()
    {
        await Open();
        var id = await StoreAsync(db.SecondConnectionString, Events(), tail: "BRAVO-1");

        var detail = (await _client.GetFromJsonAsync<FlightDetailDto>($"/api/flights/{id}", Json))!;
        Assert.Equal(("bravo", "Bravo", "BRAVO-1"), (detail.DatabaseId, detail.DatabaseName, detail.Tail));
        foreach (var path in new[] { "frames?fromMs=0&toMs=1000&stepMs=100", "track", "overview", "alerts", "events?type=Emitter&fromMs=0&toMs=1000", "series/rate", "emitter-fixes" })
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/flights/{id}/{path}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{Guid.NewGuid()}/track")).StatusCode);
    }

    [Fact]
    public async Task Imports_go_to_the_chosen_database()
    {
        await Open();
        var job = (await (await _api.UploadAsync(_client, SampleFiles.Flight(seed: 7), databaseId: "bravo")).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        Assert.Equal(("bravo", "Bravo"), (job.DatabaseId, job.DatabaseName));
        var done = await _api.WaitForFinalAsync(_client, job.Id);
        Assert.Equal(ImportState.Completed, done.State);

        var flight = (await _client.GetFromJsonAsync<FlightDetailDto>($"/api/flights/{done.FlightId}", Json))!;
        Assert.Equal("bravo", flight.DatabaseId);
        Assert.Equal(0, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.Flight")); // nothing in main

        // Without a choice: the import default (main).
        var other = (await (await _api.UploadAsync(_client, SampleFiles.Flight(seed: 8))).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        Assert.Equal("main", other.DatabaseId);
        await _api.WaitForFinalAsync(_client, other.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.UploadAsync(_client, SampleFiles.Flight(seed: 9), databaseId: "nope")).StatusCode);
    }

    [Fact]
    public async Task Read_only_databases_take_no_imports_or_deletes()
    {
        await Open(bravoReadOnly: true);
        var id = await StoreAsync(db.SecondConnectionString, Events());

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/flights/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/flights/delete", new DeleteFlightsRequest([id]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync("/api/flights?confirm=DELETE&databaseId=bravo")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.DeleteAsync("/api/flights?confirm=DELETE")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.UploadAsync(_client, SampleFiles.Flight(seed: 3), databaseId: "bravo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/flights/{id}")).StatusCode);
    }
}
