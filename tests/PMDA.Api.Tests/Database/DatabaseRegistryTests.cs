using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PMDA.Api.Database;

namespace PMDA.Api.Tests.Database;

public sealed class DatabaseRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pmda-registry", Guid.NewGuid().ToString("N"));

    private DatabaseRegistryStore Store(string? connectionString = null)
    {
        var options = Options.Create(new DatabaseOptions
        {
            RegistryPath = Path.Combine(_dir, "databases.json"),
            SettingsPath = Path.Combine(_dir, "database.json"),
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Pmda"] = connectionString })
            .Build();
        var legacy = new DatabaseSettingsStore(options, NullLogger<DatabaseSettingsStore>.Instance);
        return new DatabaseRegistryStore(options, legacy, config, NullLogger<DatabaseRegistryStore>.Instance);
    }

    private static DatabaseEntry Entry(string id, string name, bool readOnly = false) =>
        new(id, name, "localhost", "PMDA_" + id.Replace('-', '_'), DatabaseAuthentication.Windows, null, null, true, true, readOnly, true);

    [Theory]
    [InlineData("Archive 2025", "archive-2025")]
    [InlineData("  Main!! DB  ", "main-db")]
    [InlineData("ÄÖ", "db")]
    public void Ids_come_from_names(string name, string id) => Assert.Equal(id, DatabaseIds.FromName(name, []));

    [Fact]
    public void Ids_are_unique_and_valid()
    {
        Assert.Equal("archive-2", DatabaseIds.FromName("Archive", ["archive"]));
        Assert.Equal("archive-3", DatabaseIds.FromName("Archive", ["archive", "archive-2"]));
        Assert.True(DatabaseIds.IsValid("main"));
        Assert.False(DatabaseIds.IsValid("Main"));
        Assert.False(DatabaseIds.IsValid(new string('a', 33)));
    }

    [Fact]
    public void Nothing_configured_means_no_databases()
    {
        var store = Store();
        var registry = store.Load();
        Assert.Empty(registry.Databases);
        Assert.Null(registry.DefaultImportId);
        Assert.False(store.FromFile);
    }

    [Fact]
    public void The_appsettings_connection_becomes_main()
    {
        var store = Store("Server=sql01;Database=PMDA;Integrated Security=true;TrustServerCertificate=true");
        var registry = store.Load();
        var main = Assert.Single(registry.Databases);
        Assert.Equal(("main", "Main", "sql01", "PMDA", false, true), (main.Id, main.Name, main.Server, main.Database, main.ReadOnly, main.Enabled));
        Assert.Equal("main", registry.DefaultImportId);
        Assert.False(store.FromFile);
        Assert.Equal("PMDA", new SqlConnectionStringBuilder(store.ConnectionString(main)).InitialCatalog);
    }

    [Fact]
    public void The_previous_settings_file_becomes_main()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = Store("Server=ignored;Database=Ignored;Integrated Security=true");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "database.json"), $$"""
            { "server": "sql02", "database": "Flights", "authentication": "SqlLogin", "user": "pmda",
              "protectedPassword": "{{PasswordProtector.Protect("s3cret")}}", "encrypt": true, "trustServerCertificate": false }
            """);
        var main = Assert.Single(store.Load().Databases);
        Assert.Equal(("sql02", "Flights", DatabaseAuthentication.SqlLogin, "pmda"), (main.Server, main.Database, main.Authentication, main.User));
        Assert.Equal("s3cret", new SqlConnectionStringBuilder(store.ConnectionString(main)).Password);
    }

    [Fact]
    public void Saved_registry_round_trips_without_plain_passwords()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = Store();
        var archive = Entry("archive", "Archive", readOnly: true) with
        {
            Authentication = DatabaseAuthentication.SqlLogin,
            User = "reader",
            ProtectedPassword = PasswordProtector.Protect("p@ss"),
        };
        store.Save(new DatabaseRegistry("main", [Entry("main", "Main"), archive]));

        Assert.DoesNotContain("p@ss", File.ReadAllText(Path.Combine(_dir, "databases.json")));
        var reloaded = Store("Server=ignored;Database=Ignored;Integrated Security=true");
        var registry = reloaded.Load();
        Assert.True(reloaded.FromFile);
        Assert.Equal(["main", "archive"], registry.Databases.Select(d => d.Id));
        Assert.True(registry.Databases[1].ReadOnly);
        Assert.Equal("p@ss", new SqlConnectionStringBuilder(reloaded.ConnectionString(registry.Databases[1])).Password);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
