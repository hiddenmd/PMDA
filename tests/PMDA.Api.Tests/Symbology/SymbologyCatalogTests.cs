using System.Net;
using System.Net.Http.Json;
using PMDA.Api.Symbology;
using PMDA.Api.Tests.Support;

namespace PMDA.Api.Tests.Symbology;

public sealed class SymbologyCatalogTests : IDisposable
{
    private static readonly string Shipped = Path.Combine(FindRepoRoot(), "src", "PMDA.Api", "symbology");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pmda-symbology-" + Guid.NewGuid().ToString("N"));

    public SymbologyCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Shipped_profiles_are_valid_and_complete()
    {
        var catalog = new SymbologyCatalog(Shipped, "default");

        Assert.Equal(["default", "HELO-DEMO", "SAMPLE"], catalog.All.Select(p => p.Id).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Platform.Helicopter, catalog.Find("helo-demo")!.Platform);
        Assert.Equal("08", catalog.Find("SAMPLE")!.Threats.Single(t => t.Code == "08").Code);
    }

    [Fact]
    public void Customers_without_a_profile_get_the_default()
    {
        var catalog = new SymbologyCatalog(Shipped, "default");

        Assert.Equal("SAMPLE", catalog.ForCustomer("sample").Id);
        Assert.Equal("default", catalog.ForCustomer("CUSTOMER-X").Id);
    }

    [Fact]
    public void A_broken_profile_stops_loading_with_the_file_and_problem_named()
    {
        var text = File.ReadAllText(Path.Combine(Shipped, "default.json"))
            .Replace("\"rings\": [0.33, 0.66, 1.0]", "\"rings\": [0.66, 0.33]")
            .Replace("\"code\": \"X\"", "\"code\": \"TOOLONG\"");
        File.WriteAllText(Path.Combine(_dir, "default.json"), text);

        var error = Assert.Throws<InvalidOperationException>(() => new SymbologyCatalog(_dir, "default"));

        Assert.Contains("default.json", error.Message);
        Assert.Contains("rings must increase", error.Message);
        Assert.Contains("threats[0].code", error.Message);
    }

    [Fact]
    public void Unknown_fields_and_bad_json_are_rejected()
    {
        File.WriteAllText(Path.Combine(_dir, "default.json"), File.ReadAllText(Path.Combine(Shipped, "default.json")).Replace("\"name\":", "\"nmae\": \"typo\", \"name\":"));
        Assert.Contains("not valid", Assert.Throws<InvalidOperationException>(() => new SymbologyCatalog(_dir, "default")).Message);

        File.WriteAllText(Path.Combine(_dir, "default.json"), "{ not json");
        Assert.Contains("default.json", Assert.Throws<InvalidOperationException>(() => new SymbologyCatalog(_dir, "default")).Message);
    }

    [Fact]
    public void The_id_must_match_the_file_name_and_the_default_must_exist()
    {
        File.Copy(Path.Combine(Shipped, "default.json"), Path.Combine(_dir, "other.json"));
        Assert.Contains("must match the file name", Assert.Throws<InvalidOperationException>(() => new SymbologyCatalog(_dir, "default")).Message);

        File.Delete(Path.Combine(_dir, "other.json"));
        File.Copy(Path.Combine(Shipped, "SAMPLE.json"), Path.Combine(_dir, "SAMPLE.json"));
        Assert.Contains("Default symbology profile", Assert.Throws<InvalidOperationException>(() => new SymbologyCatalog(_dir, "default")).Message);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PMDA.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

[Collection(SqlCollection.Name)]
public sealed class SymbologyApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Profiles_are_served_to_signed_in_users()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/symbology")).StatusCode);
        var client = await _api.CreateSignedInClientAsync(PMDA.Data.UserRole.Analyst);

        var list = await client.GetFromJsonAsync<SymbologySummaryDto[]>("/api/symbology", PmdaApiFactory.Json);
        var forCustomer = await client.GetFromJsonAsync<SymbologyProfile>("/api/symbology/customer/SAMPLE", PmdaApiFactory.Json);
        var fallback = await client.GetFromJsonAsync<SymbologyProfile>("/api/symbology/customer/NOBODY", PmdaApiFactory.Json);

        Assert.Equal(3, list!.Length);
        Assert.Equal("SAMPLE", forCustomer!.Id);
        Assert.Equal("default", fallback!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/symbology/nope")).StatusCode);
    }
}
