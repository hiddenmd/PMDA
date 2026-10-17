using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class ImportsApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = await _api.CreateSignedInClientAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Upload_is_accepted_and_completes_with_a_flight()
    {
        var response = await _api.UploadAsync(_client, SampleFiles.Flight(events: 500), "sortie-1.bin");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        Assert.Equal($"/api/imports/{accepted.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal("sortie-1.bin", accepted.FileName);

        var final = await _api.WaitForFinalAsync(_client, accepted.Id);
        Assert.Equal(ImportState.Completed, final.State);
        Assert.Equal(500, final.EventCount);
        Assert.NotNull(final.FlightId);
        Assert.NotNull(final.FinishedUtc);
        Assert.Empty(Directory.EnumerateFiles(_api.UploadDirectory)); // upload cleaned up
    }

    [Fact]
    public async Task Hub_clients_receive_every_change_ending_in_completed()
    {
        await using var hub = _api.CreateHubConnection();
        var updates = new List<ImportJobDto>();
        var done = new TaskCompletionSource<ImportJobDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<ImportJobDto>(nameof(IImportClient.ImportUpdated), job =>
        {
            lock (updates)
                updates.Add(job);
            if (ImportStates.IsFinal(job.State))
                done.TrySetResult(job);
        });
        await hub.StartAsync();

        var response = await _api.UploadAsync(_client, SampleFiles.Flight(events: 100));
        var id = (await response.Content.ReadFromJsonAsync<ImportJobDto>(Json))!.Id;
        var final = await done.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(id, final.Id);
        Assert.Equal(ImportState.Completed, final.State);
        lock (updates)
        {
            Assert.Contains(updates, u => u.State == ImportState.Running);
            Assert.Equal(updates.Select(u => u.Version).Order(), updates.Select(u => u.Version).Distinct().Order());
        }
    }

    [Fact]
    public async Task Same_file_twice_ends_as_duplicate()
    {
        var file = SampleFiles.Flight();
        var first = (await (await _api.UploadAsync(_client, file)).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        await _api.WaitForFinalAsync(_client, first.Id);

        var second = (await (await _api.UploadAsync(_client, file)).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        var final = await _api.WaitForFinalAsync(_client, second.Id);

        Assert.Equal(ImportState.Duplicate, final.State);
    }

    [Fact]
    public async Task Unrecognized_file_fails_with_reason()
    {
        var response = await _api.UploadAsync(_client, "definitely not a flight"u8.ToArray());
        var job = (await response.Content.ReadFromJsonAsync<ImportJobDto>(Json))!;

        var final = await _api.WaitForFinalAsync(_client, job.Id);

        Assert.Equal(ImportState.Failed, final.State);
        Assert.Contains("not recognized", final.FailureReason);
        Assert.Null(final.FlightId);
    }

    [Fact]
    public async Task Empty_file_is_bad_request_problem()
    {
        var response = await _api.UploadAsync(_client, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("empty", problem!.Detail);
    }

    [Fact]
    public async Task Unknown_import_is_not_found_problem()
    {
        var get = await _client.GetAsync($"/api/imports/{Guid.NewGuid()}");
        var cancel = await _client.DeleteAsync($"/api/imports/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal("application/problem+json", get.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
    }

    [Fact]
    public async Task Cancelling_a_finished_import_is_conflict()
    {
        var job = (await (await _api.UploadAsync(_client, SampleFiles.Flight())).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        await _api.WaitForFinalAsync(_client, job.Id);

        var response = await _client.DeleteAsync($"/api/imports/{job.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_a_queued_import_cancels_it()
    {
        // Park a job in the store without a worker picking it up, then cancel through the API.
        var store = _api.Service<ImportJobStore>();
        var job = new ImportJob(Guid.NewGuid(), "parked.bin", 1, Path.Combine(_api.UploadDirectory, "none"), TimeProvider.System);
        store.Add(job);

        var response = await _client.DeleteAsync($"/api/imports/{job.Id}");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ImportJobDto>(Json);
        Assert.Equal(ImportState.Cancelled, dto!.State);
    }

    [Fact]
    public async Task List_returns_newest_first()
    {
        var a = (await (await _api.UploadAsync(_client, SampleFiles.Flight(seed: 1))).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        var b = (await (await _api.UploadAsync(_client, SampleFiles.Flight(seed: 2))).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;

        var list = await _client.GetFromJsonAsync<ImportJobDto[]>("/api/imports", Json);

        Assert.Equal([b.Id, a.Id], list!.Select(j => j.Id));
    }

    [Fact]
    public async Task OpenApi_document_describes_endpoints_with_string_enums()
    {
        var doc = await _client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"/api/imports\"", doc);
        Assert.Contains("\"/api/imports/{id}\"", doc);
        Assert.Contains("\"/api/flights\"", doc);
        Assert.Contains("\"/api/flights/{id}/diagnostics\"", doc);
        Assert.Contains("\"Queued\"", doc);
        Assert.Contains("multipart/form-data", doc);
    }
}
