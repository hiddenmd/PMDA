using System.Net;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using PMDA.Core.Events;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class PlaybackApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;
    private Guid _flightId;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = _api.CreateClient();
        // 100 emitter records 10 ms apart: 0–990 ms.
        var job = (await (await _api.UploadAsync(_client, SampleFiles.Flight(events: 100))).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        var final = await _api.WaitForFinalAsync(_client, job.Id);
        Assert.Equal(ImportState.Completed, final.State);
        _flightId = final.FlightId!.Value;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Frames_summarise_emitters_per_step()
    {
        var result = await _client.GetFromJsonAsync<PlaybackFramesDto>(
            $"/api/flights/{_flightId}/frames?fromMs=0&toMs=1000&stepMs=250", Json);

        Assert.Equal(250, result!.StepMs);
        Assert.Equal([0L, 250, 500, 750], result.Frames.Select(f => f.OffsetMs));
        Assert.Equal(100, result.Frames.SelectMany(f => f.Emitters).Sum(e => e.Hits));
        Assert.All(result.Frames, f => Assert.Equal(90f, f.Emitters[0].BearingDeg, 0.01f));
    }

    [Fact]
    public async Task Overview_spans_the_flight()
    {
        var overview = await _client.GetFromJsonAsync<FlightOverviewDto>($"/api/flights/{_flightId}/overview?buckets=10", Json);

        Assert.Equal(10, overview!.Buckets);
        Assert.Equal(991, overview.SpanMs);
        var emitters = Assert.Single(overview.Series);
        Assert.Equal(EventType.Emitter, emitters.EventType);
        Assert.Equal(100, emitters.Counts.Sum());
    }

    [Theory]
    [InlineData("fromMs=-1&toMs=10&stepMs=10")]
    [InlineData("fromMs=10&toMs=10&stepMs=10")]
    [InlineData("fromMs=0&toMs=1000&stepMs=1")]
    [InlineData("fromMs=0&toMs=100000000&stepMs=10")]
    public async Task Invalid_frame_windows_are_validation_problems(string query)
    {
        var response = await _client.GetAsync($"/api/flights/{_flightId}/frames?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Unknown_flight_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{Guid.NewGuid()}/frames?fromMs=0&toMs=100&stepMs=10")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{Guid.NewGuid()}/overview")).StatusCode);
    }
}
