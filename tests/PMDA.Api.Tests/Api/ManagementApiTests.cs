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
        await WaitUntilAsync(async () => await Count("FlightEvent") == 2); // purged in the background
        Assert.Equal(1, await Count("Flight"));
    }

    [Fact]
    public async Task A_deleted_flight_disappears_at_once_from_every_view()
    {
        var gone = await Flight("GONE");
        await Flight("KEEP");

        await _client.DeleteAsync($"/api/flights/{gone}");

        // Immediately, before any background purge:
        var list = await _client.GetFromJsonAsync<FlightPageDto>("/api/flights", Json);
        Assert.Equal(["KEEP"], list!.Items.Select(f => f.Tail));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{gone}/frames?fromMs=0&toMs=100&stepMs=10")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{gone}/series/rate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{gone}/events?type=Emitter&fromMs=0&toMs=100")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/flights/{gone}")).StatusCode);
    }

    [Fact]
    public async Task The_same_file_can_be_imported_again_right_after_its_flight_is_deleted()
    {
        var file = SampleFiles.Flight(events: 50, seed: 77);
        var first = (await (await _api.UploadAsync(_client, file)).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        var flightId = (await _api.WaitForFinalAsync(_client, first.Id)).FlightId!.Value;

        await _client.DeleteAsync($"/api/flights/{flightId}");
        var again = (await (await _api.UploadAsync(_client, file)).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;

        Assert.Equal(ImportState.Completed, (await _api.WaitForFinalAsync(_client, again.Id)).State);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(100);
        }
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
        await WaitUntilAsync(async () => await Count("Flight") == 1);
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

        var response = await _client.DeleteAsync("/api/flights?confirm=DELETE&databaseId=main");

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

        var response = await _client.DeleteAsync("/api/flights?confirm=DELETE&databaseId=main");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await Count("Flight"));
        job.RequestCancel();
    }
}
