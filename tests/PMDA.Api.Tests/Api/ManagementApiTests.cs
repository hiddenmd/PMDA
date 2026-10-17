using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using static PMDA.Api.Tests.Support.PmdaApiFactory;
using static PMDA.Api.Tests.Support.TestFlights;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class ManagementApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = await _api.CreateSignedInClientAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private Task<Guid> Flight(string tail = "T") => StoreAsync(db.ConnectionString, [Emitter(0, 1), Nav(10, 1, 1, 1)], tail: tail);

    private Task<long> Count(string table) => db.ScalarAsync($"SELECT COUNT(*) FROM dbo.{table}");

    [Fact]
    public async Task Delete_one_flight_removes_it_and_its_events()
    {
        var keep = await Flight("KEEP");
        var gone = await Flight("GONE");

        var response = await _client.DeleteAsync($"/api/flights/{gone}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{gone}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/flights/{keep}")).StatusCode);
        Assert.Equal(2, await Count("FlightEvent"));
    }

    [Fact]
    public async Task Delete_unknown_flight_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/flights/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Delete_selected_flights()
    {
        var a = await Flight();
        var b = await Flight();
        var c = await Flight();

        var response = await _client.PostAsJsonAsync("/api/flights/delete", new DeleteFlightsRequest([a, b, Guid.NewGuid()]), Json);

        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<DeleteResultDto>(Json))!.Deleted); // the unknown id is ignored
        Assert.Equal(1, await Count("Flight"));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/flights/{c}")).StatusCode);
    }

    [Fact]
    public async Task Delete_selected_needs_ids()
    {
        var response = await _client.PostAsJsonAsync("/api/flights/delete", new DeleteFlightsRequest([]), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_all_needs_typed_confirmation()
    {
        await Flight();

        var response = await _client.DeleteAsync("/api/flights");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await Count("Flight"));
    }

    [Fact]
    public async Task Delete_all_removes_everything_and_notifies_clients()
    {
        await Flight();
        await Flight();
        await using var hub = _api.CreateHubConnection();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On(nameof(IImportClient.FlightsChanged), () => changed.TrySetResult());
        await hub.StartAsync();

        var response = await _client.DeleteAsync("/api/flights?confirm=DELETE");

        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<DeleteResultDto>(Json))!.Deleted);
        Assert.Equal(0, await Count("Flight"));
        Assert.Equal(0, await Count("FlightEvent"));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Delete_all_is_refused_while_an_import_runs()
    {
        await Flight();
        var job = new ImportJob(Guid.NewGuid(), "running.bin", 1, "none", TimeProvider.System);
        _api.Service<ImportJobStore>().Add(job); // queued, never picked up

        var response = await _client.DeleteAsync("/api/flights?confirm=DELETE");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await Count("Flight"));
        job.RequestCancel();
    }
}
