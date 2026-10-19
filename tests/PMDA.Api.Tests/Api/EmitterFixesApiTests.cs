using System.Net;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Tests.Support;
using PMDA.Core.Events;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class EmitterFixesApiTests(SqlDatabase db) : IAsyncLifetime
{
    private const double Rad = Math.PI / 180;
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = await _api.CreateSignedInClientAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double φ1 = lat1 * Rad, φ2 = lat2 * Rad, Δλ = (lon2 - lon1) * Rad;
        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        return (Math.Atan2(y, x) / Rad + 360) % 360;
    }

    private static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        double dφ = (lat2 - lat1) * Rad, dλ = (lon2 - lon1) * Rad;
        var a = Math.Sin(dφ / 2) * Math.Sin(dφ / 2) + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Sin(dλ / 2) * Math.Sin(dλ / 2);
        return 2 * 6_371_000 * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>
    /// 400 s flying due east along 44.3° N (heading 90) at about 160 m/s; three fixed sites and one emitter that
    /// switches site halfway. Relative bearing = true bearing − heading, with 1.5° noise.
    /// </summary>
    private static FlightEvent[] Flight()
    {
        var random = new Random(7);
        double Noise() => 1.5 * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        var sites = new (int Id, double Lat, double Lon)[] { (1, 44.45, 26.05), (2, 44.10, 26.30), (3, 44.55, 26.55) };
        var events = new List<FlightEvent>();
        for (var ms = 0; ms <= 400_000; ms += 500)
        {
            var lon = 25.8 + 0.8 * ms / 400_000.0;
            if (ms % 1000 == 0)
                events.Add(TestFlights.Nav(ms, 44.3, lon, 6000));
            foreach (var s in sites)
                events.Add(TestFlights.Emitter(ms, s.Id, (float)((BearingDeg(44.3, lon, s.Lat, s.Lon) - 90 + Noise() + 720) % 360), freq: 9000 + s.Id));
            var (mLat, mLon) = ms < 200_000 ? (44.5, 26.0) : (44.45, 26.5);
            events.Add(TestFlights.Emitter(ms, 4, (float)((BearingDeg(44.3, lon, mLat, mLon) - 90 + Noise() + 720) % 360), freq: 5600));
        }
        return events.ToArray();
    }

    [Fact]
    public async Task Fixed_sites_are_resolved_near_the_truth_and_the_moving_emitter_is_not()
    {
        var id = await TestFlights.StoreAsync(db.ConnectionString, Flight());
        var fixes = (await _client.GetFromJsonAsync<EmitterFixDto[]>($"/api/flights/{id}/emitter-fixes", Json))!;

        Assert.Equal([1, 2, 3, 4], fixes.Select(f => f.EmitterId).Order());
        foreach (var (emitter, lat, lon) in new[] { (1, 44.45, 26.05), (2, 44.10, 26.30), (3, 44.55, 26.55) })
        {
            var f = fixes.Single(x => x.EmitterId == emitter);
            Assert.True(f.Resolved, f.Reason);
            Assert.True(DistanceM(f.Lat!.Value, f.Lon!.Value, lat, lon) < 1_000, $"emitter {emitter} off by {DistanceM(f.Lat!.Value, f.Lon!.Value, lat, lon):F0} m");
            Assert.InRange(f.FrequencyMHz, 9000 + emitter - 1, 9000 + emitter + 1);
            Assert.InRange(f.FirstMs, 0, 2_000);
            Assert.InRange(f.LastMs, 398_000, 400_000);
        }
        var moving = fixes.Single(x => x.EmitterId == 4);
        Assert.False(moving.Resolved);
        Assert.NotNull(moving.Reason);
    }

    [Fact]
    public async Task Unknown_flight_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/flights/{Guid.NewGuid()}/emitter-fixes")).StatusCode);
}
