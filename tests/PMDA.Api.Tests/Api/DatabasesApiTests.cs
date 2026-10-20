using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using PMDA.Api.Contracts;
using PMDA.Api.Database;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using PMDA.Data;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class DatabasesApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);

    public async Task InitializeAsync() => await db.ResetAsync();

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private DatabaseEntryRequest Bravo(string name = "Bravo", bool readOnly = false, string? database = null)
    {
        var b = new SqlConnectionStringBuilder(db.SecondConnectionString);
        return new DatabaseEntryRequest(null, name, b.DataSource, database ?? b.InitialCatalog, DatabaseAuthentication.Windows, null, null,
            true, true, readOnly, Enabled: true, CreateIfMissing: false);
    }

    [Fact]
    public async Task Admins_manage_the_list_of_databases()
    {
        var admin = await _api.CreateSignedInClientAsync();

        // Before any change: the configured connection, as "main", without a list file.
        var list = (await admin.GetFromJsonAsync<DatabaseListDto>("/api/databases", Json))!;
        var main = Assert.Single(list.Databases);
        Assert.Equal(("main", DatabaseState.Ready, true, 0L), (main.Id, main.State, main.IsImportDefault, main.FlightCount));
        Assert.False(File.Exists(_api.RegistryPath));

        var added = await admin.PostAsJsonAsync("/api/databases", Bravo(), Json);
        added.EnsureSuccessStatusCode();
        list = (await added.Content.ReadFromJsonAsync<DatabaseListDto>(Json))!;
        var bravo = list.Databases.Single(d => d.Id == "bravo");
        Assert.Equal((DatabaseState.Ready, false), (bravo.State, bravo.IsImportDefault));
        Assert.True(File.Exists(_api.RegistryPath));

        (await admin.PutAsJsonAsync("/api/databases/bravo", Bravo("Bravo 2"), Json)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/databases/default", new SetDefaultDatabaseRequest("bravo"), Json)).EnsureSuccessStatusCode();
        var targets = (await admin.GetFromJsonAsync<DatabaseChoicesDto>("/api/databases/choices", Json))!;
        Assert.Equal("bravo", targets.DefaultImportId);
        Assert.Contains(targets.Databases, t => t is { Id: "bravo", Name: "Bravo 2", ReadOnly: false });

        var removed = await admin.DeleteAsync("/api/databases/bravo");
        removed.EnsureSuccessStatusCode();
        list = (await removed.Content.ReadFromJsonAsync<DatabaseListDto>(Json))!;
        Assert.Equal(["main"], list.Databases.Select(d => d.Id));
        Assert.Equal("main", list.DefaultImportId); // the default moved back
    }

    [Fact]
    public async Task Analysts_only_see_where_imports_may_go()
    {
        var analyst = await _api.CreateSignedInClientAsync(UserRole.Analyst);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/databases")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PostAsJsonAsync("/api/databases", Bravo(), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.DeleteAsync("/api/databases/main")).StatusCode);
        var targets = (await analyst.GetFromJsonAsync<DatabaseChoicesDto>("/api/databases/choices", Json))!;
        Assert.Equal("main", targets.DefaultImportId);
    }

    [Fact]
    public async Task A_database_with_running_imports_cannot_be_removed()
    {
        var admin = await _api.CreateSignedInClientAsync();
        var job = new ImportJob(Guid.NewGuid(), "busy.bin", 1, "none", TimeProvider.System, "main", "Main");
        _api.Service<ImportJobStore>().Add(job);
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync("/api/databases/main")).StatusCode);
        }
        finally
        {
            job.RequestCancel();
        }
    }

    [Fact]
    public async Task Read_only_databases_must_already_be_pmda_databases()
    {
        var admin = await _api.CreateSignedInClientAsync();
        var notPmda = await admin.PostAsJsonAsync("/api/databases", Bravo("Master", readOnly: true, database: "master"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, notPmda.StatusCode);
        Assert.Contains("no PMDA flights table", await notPmda.Content.ReadAsStringAsync());

        (await admin.PostAsJsonAsync("/api/databases", Bravo("Archive", readOnly: true), Json)).EnsureSuccessStatusCode();
        var targets = (await admin.GetFromJsonAsync<DatabaseChoicesDto>("/api/databases/choices", Json))!;
        Assert.Contains(targets.Databases, t => t is { Id: "archive", ReadOnly: true });
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/databases/default", new SetDefaultDatabaseRequest("archive"), Json)).StatusCode);
    }

    [Fact]
    public async Task Connections_are_tested_without_saving()
    {
        var admin = await _api.CreateSignedInClientAsync();
        var ok = (await (await admin.PostAsJsonAsync("/api/databases/test", Bravo(), Json)).Content.ReadFromJsonAsync<DatabaseTestResultDto>(Json))!;
        Assert.True(ok.Ok && ok.DatabaseExists);
        var down = (await (await admin.PostAsJsonAsync("/api/databases/test", Bravo() with { Server = "127.0.0.1,1" }, Json)).Content.ReadFromJsonAsync<DatabaseTestResultDto>(Json))!;
        Assert.False(down.Ok);
        Assert.Single((await admin.GetFromJsonAsync<DatabaseListDto>("/api/databases", Json))!.Databases);
    }
}
