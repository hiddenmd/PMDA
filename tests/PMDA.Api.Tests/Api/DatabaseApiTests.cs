using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using PMDA.Api.Contracts;
using PMDA.Api.Database;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using PMDA.Data;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class DatabaseApiTests(SqlDatabase db)
{
    private DatabaseSetupRequest TestDatabaseRequest(bool createIfMissing = false)
    {
        var b = new SqlConnectionStringBuilder(db.ConnectionString);
        return new DatabaseSetupRequest(b.DataSource, b.InitialCatalog, DatabaseAuthentication.Windows, null, null, true, true, createIfMissing);
    }

    [Fact]
    public async Task Admins_see_the_connection_analysts_do_not()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var status = await admin.GetFromJsonAsync<DatabaseStatusDto>("/api/database", PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.Ready, true, DatabaseSource.AppSettings), (status!.State, status.CanConfigure, status.Source));
        Assert.Equal(new SqlConnectionStringBuilder(db.ConnectionString).InitialCatalog, status.Settings!.Database);
        Assert.False(status.Settings.HasPassword);

        var analyst = await factory.CreateSignedInClientAsync(UserRole.Analyst);
        status = await analyst.GetFromJsonAsync<DatabaseStatusDto>("/api/database", PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.Ready, false), (status!.State, status.CanConfigure));
        Assert.Null(status.Settings);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PostAsJsonAsync("/api/database/test", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Tests_a_connection_and_reports_failures()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var ok = await (await admin.PostAsJsonAsync("/api/database/test", TestDatabaseRequest(), PmdaApiFactory.Json)).Content.ReadFromJsonAsync<DatabaseTestResultDto>(PmdaApiFactory.Json);
        Assert.True(ok!.Ok);
        Assert.True(ok.DatabaseExists);
        Assert.False(string.IsNullOrEmpty(ok.ServerVersion));

        var bad = TestDatabaseRequest() with { Server = "127.0.0.1,1" };
        var failed = await (await admin.PostAsJsonAsync("/api/database/test", bad, PmdaApiFactory.Json)).Content.ReadFromJsonAsync<DatabaseTestResultDto>(PmdaApiFactory.Json);
        Assert.False(failed!.Ok);
        Assert.False(string.IsNullOrEmpty(failed.Message));

        var invalid = TestDatabaseRequest() with { Database = "bad;name" };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/database/test", invalid, PmdaApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Saving_writes_the_file_and_switches()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var response = await admin.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<DatabaseStatusDto>(PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.Ready, DatabaseSource.File), (status!.State, status.Source));
        Assert.True(File.Exists(factory.SettingsPath));
        Assert.Contains("\"authentication\": \"Windows\"", File.ReadAllText(factory.SettingsPath));
        // Same database: the session survives the switch.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/flights")).StatusCode);
    }

    [Fact]
    public async Task Saving_is_refused_while_imports_run()
    {
        await using var factory = new PmdaApiFactory(db);
        var admin = await factory.CreateSignedInClientAsync();
        var jobs = factory.Service<ImportJobStore>();
        var job = new ImportJob(Guid.NewGuid(), "busy.bin", 1, Path.GetTempFileName(), TimeProvider.System);
        jobs.Add(job);
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
        }
        finally
        {
            jobs.Remove(job.Id);
        }
    }

    [Fact]
    public async Task Without_a_database_the_api_answers_503_and_only_the_server_machine_may_configure()
    {
        await using var factory = new PmdaApiFactory(db, new() { ["ConnectionStrings:Pmda"] = "" });
        var client = factory.CreateClient();
        var flights = await client.GetAsync("/api/flights");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, flights.StatusCode);
        Assert.Contains("Database not configured", await flights.Content.ReadAsStringAsync());

        var status = await client.GetFromJsonAsync<DatabaseStatusDto>("/api/database", PmdaApiFactory.Json);
        Assert.Equal((DatabaseState.NotConfigured, false, DatabaseSource.None), (status!.State, status.CanConfigure, status.Source));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/database", TestDatabaseRequest(), PmdaApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task First_setup_connects_the_running_api()
    {
        await using var factory = new PmdaApiFactory(db, new() { ["ConnectionStrings:Pmda"] = "", ["Auth:AllowRemoteSetup"] = "true" });
        var client = factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/database", TestDatabaseRequest(createIfMissing: true), PmdaApiFactory.Json);
        response.EnsureSuccessStatusCode();
        Assert.Equal(DatabaseState.Ready, (await response.Content.ReadFromJsonAsync<DatabaseStatusDto>(PmdaApiFactory.Json))!.State);

        var admin = await factory.CreateSignedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/flights")).StatusCode);
    }
}
