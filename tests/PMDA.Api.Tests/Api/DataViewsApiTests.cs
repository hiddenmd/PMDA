using System.Net;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Tests.Support;
using PMDA.Core.Events;
using PMDA.Data;
using static PMDA.Api.Tests.Support.PmdaApiFactory;
using static PMDA.Api.Tests.Support.TestFlights;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class DataViewsApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;
    private Guid _flight;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = await _api.CreateSignedInClientAsync();
        _flight = await StoreAsync(db.ConnectionString,
        [
            Emitter(100, 7, bearing: 359, freq: 9000, amplitude: -40, pri: 100),
            Emitter(300, 7, bearing: 1, freq: 9010, amplitude: -35, pri: 110),
            Emitter(500, 9, bearing: 90, freq: 3000, amplitude: -60, pri: 3000),
            Nav(200, 44.0, 26.0, 3000),
            Nav(700, 44.2, 26.2, 3100),
            Cm(2100, kind: 1, count: 3),
            Cm(2600, kind: 2, count: 2),
            Emitter(5500, 7, bearing: 180, freq: 9000, amplitude: -30, pri: 100),
            Nav(5900, 44.4, 26.4, 3300),
        ]);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Events_are_one_type_in_the_window_ordered_by_sequence()
    {
        var page = await _client.GetFromJsonAsync<EventPageDto>($"/api/flights/{_flight}/events?type=Emitter&fromMs=0&toMs=6000", Json);

        Assert.Equal(EventType.Emitter, page!.Type);
        Assert.Equal([0L, 1, 2, 7], page.Rows.Select(r => r.Seq));
        Assert.Equal([100L, 300, 500, 5500], page.Rows.Select(r => r.OffsetMs));
        Assert.Equal(9010f, page.Rows[1].FrequencyMHz);
        Assert.False(page.HasMore);
        Assert.Null(page.NextAfterSeq);
    }

    [Fact]
    public async Task Events_page_with_afterSeq()
    {
        var first = await _client.GetFromJsonAsync<EventPageDto>($"/api/flights/{_flight}/events?type=Emitter&fromMs=0&toMs=6000&limit=2", Json);
        var rest = await _client.GetFromJsonAsync<EventPageDto>(
            $"/api/flights/{_flight}/events?type=Emitter&fromMs=0&toMs=6000&limit=2&afterSeq={first!.NextAfterSeq}", Json);

        Assert.True(first.HasMore);
        Assert.Equal([0L, 1], first.Rows.Select(r => r.Seq));
        Assert.Equal([2L, 7], rest!.Rows.Select(r => r.Seq));
        Assert.False(rest.HasMore);
    }

    [Theory]
    [InlineData("type=Emitter&fromMs=0&toMs=0")]
    [InlineData("type=Emitter&fromMs=0&toMs=3600001")]
    [InlineData("type=Emitter&fromMs=0&toMs=100&limit=0")]
    [InlineData("type=Nope&fromMs=0&toMs=100")]
    public async Task Invalid_event_queries_are_bad_requests(string query)
    {
        var response = await _client.GetAsync($"/api/flights/{_flight}/events?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Parameter_series_is_per_emitter_per_bucket()
    {
        var s = await _client.GetFromJsonAsync<ParameterSeriesDto>($"/api/flights/{_flight}/series/parameter?param=Frequency&fromMs=0&toMs=6000&buckets=6", Json);

        Assert.Equal(1000, s!.Window.BucketMs);
        Assert.Equal([7, 9], s.Series.Select(e => e.EmitterId));
        var seven = s.Series[0];
        Assert.Equal([500L, 5500], seven.T); // bucket centres
        Assert.Equal(9005, seven.V[0], 3);
    }

    [Fact]
    public async Task Bearing_series_uses_a_circular_mean()
    {
        var s = await _client.GetFromJsonAsync<ParameterSeriesDto>($"/api/flights/{_flight}/series/parameter?param=Bearing&fromMs=0&toMs=6000&buckets=6", Json);

        var v = s!.Series[0].V[0];
        Assert.True(v < 0.5 || v > 359.5, $"mean of 359° and 1° should be ~0°, was {v}");
    }

    [Fact]
    public async Task Scatter_pairs_two_parameters()
    {
        var s = await _client.GetFromJsonAsync<ScatterSeriesDto>($"/api/flights/{_flight}/series/scatter?x=Frequency&y=Pri&fromMs=0&toMs=6000&buckets=6", Json);

        var nine = s!.Series.Single(e => e.EmitterId == 9);
        Assert.Equal([3000.0], nine.X);
        Assert.Equal([3000.0], nine.Y);
    }

    [Fact]
    public async Task Profile_has_altitude_and_countermeasure_marks()
    {
        var s = await _client.GetFromJsonAsync<ProfileSeriesDto>($"/api/flights/{_flight}/series/profile?buckets=6", Json);

        Assert.Equal(0, s!.Window.FromMs);
        Assert.Equal(5901, s.Window.ToMs); // whole flight by default
        Assert.Equal(3050, s.AltM[0], 3);
        Assert.Equal([new CountermeasureMarkDto(2100, 1, 3), new CountermeasureMarkDto(2600, 2, 2)], s.Countermeasures);
    }

    [Fact]
    public async Task Rate_counts_events_per_type_per_bucket()
    {
        var s = await _client.GetFromJsonAsync<RateSeriesDto>($"/api/flights/{_flight}/series/rate?fromMs=0&toMs=6000&buckets=6", Json);

        Assert.Equal(6, s!.T.Length);
        Assert.Equal([3L, 0, 0, 0, 0, 1], s.Series.Single(x => x.EventType == EventType.Emitter).Counts);
        Assert.Equal([0L, 0, 2, 0, 0, 0], s.Series.Single(x => x.EventType == EventType.Countermeasure).Counts);
    }

    [Fact]
    public async Task Series_of_unknown_flight_is_not_found()
    {
        var response = await _client.GetAsync($"/api/flights/{Guid.NewGuid()}/series/rate");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Flight_list_filters_and_sorts()
    {
        await db.ResetAsync();
        var a = await StoreAsync(db.ConnectionString, [Nav(0, 1, 1, 1)], tail: "ALPHA-07", mission: "STRIKE-4", start: Start);
        var b = await StoreAsync(db.ConnectionString, [Nav(0, 1, 1, 1)], tail: "BRAVO-21", mission: "RECCE-2", errorCount: 26, start: Start.AddDays(1));
        var c = await StoreAsync(db.ConnectionString, [Nav(0, 1, 1, 1)], tail: "OSCAR-90", mission: "PATROL-9", customer: "CUST-B", start: Start.AddDays(2));

        async Task<Guid[]> Ids(string q) =>
            (await _client.GetFromJsonAsync<FlightPageDto>($"/api/flights?{q}", Json))!.Items.Select(f => f.Id).ToArray();

        Assert.Equal([b], await Ids("tail=avo")); // contains, case-insensitive collation
        Assert.Equal([a], await Ids("mission=STRIKE"));
        Assert.Equal([c], await Ids("customerId=CUST-B"));
        Assert.Equal([b], await Ids("hasProblems=true"));
        Assert.Equal([b, a], await Ids($"startTo={Start.AddDays(2):O}&sort=StartNewest"));
        Assert.Equal([a, b, c], await Ids("sort=Tail"));
        Assert.Equal([a, b, c], await Ids("sort=StartOldest&status=Completed"));
        Assert.Equal(["CUST-B", "SAMPLE"], (await _client.GetFromJsonAsync<string[]>("/api/flights/customers", Json))!);
    }
}
