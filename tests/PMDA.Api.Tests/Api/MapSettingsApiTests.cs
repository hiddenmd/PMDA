using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PMDA.Api.Contracts;
using PMDA.Api.Tests.Support;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class MapSettingsApiTests(SqlDatabase db) : IAsyncLifetime
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
    public async Task Defaults_to_keyless_OpenFreeMap_with_terrain()
    {
        var s = (await _client.GetFromJsonAsync<MapSettingsDto>("/api/map", Json))!;
        Assert.Equal("OpenFreeMap", s.Source);
        Assert.Equal("https://tiles.openfreemap.org/planet", s.TileJsonUrl);
        Assert.Equal("https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf", s.GlyphsUrl);
        Assert.Equal("https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png", s.TerrainUrl);
        Assert.Null(s.PmtilesUrl);
        Assert.Contains("OpenStreetMap", s.Attribution);
        Assert.DoesNotContain("api_key", s.TileJsonUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pmtiles_endpoint_is_404_when_not_configured() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/map/tiles.pmtiles")).StatusCode);

    [Fact]
    public async Task Pmtiles_file_is_served_with_range_support()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
        try
        {
            await using var api = new PmdaApiFactory(db, settings: new() { ["Map:Source"] = "PMTiles", ["Map:PmtilesPath"] = path });
            var client = await api.CreateSignedInClientAsync();
            var s = (await client.GetFromJsonAsync<MapSettingsDto>("/api/map", Json))!;
            Assert.Equal("PMTiles", s.Source);
            Assert.Equal("/api/map/tiles.pmtiles", s.PmtilesUrl);

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/map/tiles.pmtiles");
            request.Headers.Range = new RangeHeaderValue(10, 19);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i), await response.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
