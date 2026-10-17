using System.Net;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using PMDA.Data;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class FlightsApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = _api.CreateClient();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> ImportAsync(byte[] file)
    {
        var job = (await (await _api.UploadAsync(_client, file)).Content.ReadFromJsonAsync<ImportJobDto>(Json))!;
        var final = await _api.WaitForFinalAsync(_client, job.Id);
        Assert.Equal(ImportState.Completed, final.State);
        return final.FlightId!.Value;
    }

    [Fact]
    public async Task Imported_flight_is_listed_and_has_details()
    {
        var id = await ImportAsync(SampleFiles.Flight(events: 42));

        var page = await _client.GetFromJsonAsync<FlightPageDto>("/api/flights", Json);
        var summary = Assert.Single(page!.Items);
        Assert.Equal(id, summary.Id);
        Assert.Equal(42, summary.EventCount);
        Assert.Equal(FlightStatus.Completed, summary.Status);
        Assert.Equal(1, page.Total);

        var detail = await _client.GetFromJsonAsync<FlightDetailDto>($"/api/flights/{id}", Json);
        Assert.Equal("SAMPLE", detail!.CustomerId);
        Assert.Equal("flight.bin", detail.SourceName);
        Assert.Equal(SampleFiles.Start, detail.FirstEventUtc);
    }

    [Fact]
    public async Task List_filters_by_tail_and_pages()
    {
        await ImportAsync(SampleFiles.Flight(seed: 1, tail: "AAA"));
        await ImportAsync(SampleFiles.Flight(seed: 2, tail: "BBB"));
        await ImportAsync(SampleFiles.Flight(seed: 3, tail: "BBB"));

        var bbb = await _client.GetFromJsonAsync<FlightPageDto>("/api/flights?tail=BBB&pageSize=1&page=2", Json);

        Assert.Equal(2, bbb!.Total);
        Assert.Equal("BBB", Assert.Single(bbb.Items).Tail);
        Assert.Equal(2, bbb.Page);
    }

    [Fact]
    public async Task Diagnostics_lists_parse_problems()
    {
        var id = await ImportAsync(SampleFiles.Flight(withBadRecord: true));

        var diagnostics = await _client.GetFromJsonAsync<DiagnosticDto[]>($"/api/flights/{id}/diagnostics", Json);

        Assert.Contains(diagnostics!, d => d.Message.Contains("Checksum"));
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("page=0")]
    public async Task Invalid_paging_is_validation_problem(string query)
    {
        var response = await _client.GetAsync($"/api/flights?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Unknown_flight_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{Guid.NewGuid()}/diagnostics")).StatusCode);
    }
}
