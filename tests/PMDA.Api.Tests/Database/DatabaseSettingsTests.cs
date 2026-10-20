using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PMDA.Api.Database;

namespace PMDA.Api.Tests.Database;

public sealed class DatabaseSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pmda-db-settings", Guid.NewGuid().ToString("N"));

    private DatabaseSettingsStore Store() =>
        new(Options.Create(new DatabaseOptions { SettingsPath = Path.Combine(_dir, "database.json") }), NullLogger<DatabaseSettingsStore>.Instance);

    [Theory]
    [InlineData("PMDA", true)]
    [InlineData("PMDA_Test-2", true)]
    [InlineData("bad;name", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Database_names_must_be_plain(string? name, bool valid) => Assert.Equal(valid, DatabaseConnections.IsValidName(name));

    [Fact]
    public void Builds_windows_and_sql_login_connection_strings()
    {
        var windows = new SqlConnectionStringBuilder(DatabaseConnections.Build("srv", "PMDA", DatabaseAuthentication.Windows, null, null, true, false));
        Assert.True(windows.IntegratedSecurity);
        Assert.Equal("PMDA", windows.ApplicationName);
        Assert.Equal(10, windows.ConnectTimeout);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, windows.Encrypt);

        var sql = new SqlConnectionStringBuilder(DatabaseConnections.Build("srv,1433", "PMDA", DatabaseAuthentication.SqlLogin, "pmda", "p@ss;word", false, true));
        Assert.False(sql.IntegratedSecurity);
        Assert.Equal("pmda", sql.UserID);
        Assert.Equal("p@ss;word", sql.Password);
        Assert.True(sql.TrustServerCertificate);
    }

    [Fact]
    public void Describes_a_connection_without_its_password()
    {
        var dto = DatabaseConnections.Describe("Server=srv;Database=PMDA;User ID=pmda;Password=secret;Encrypt=false");
        Assert.Equal(("srv", "PMDA", DatabaseAuthentication.SqlLogin, "pmda", true), (dto.Server, dto.Database, dto.Authentication, dto.User, dto.HasPassword));
        Assert.False(dto.Encrypt);
        Assert.Equal(DatabaseAuthentication.Windows, DatabaseConnections.Describe("Server=srv;Database=PMDA;Integrated Security=true").Authentication);
    }

    [Fact]
    public void Password_round_trips_through_dpapi()
    {
        if (!OperatingSystem.IsWindows()) return;
        var blob = PasswordProtector.Protect("s3cret!");
        Assert.DoesNotContain("s3cret", blob);
        Assert.Equal("s3cret!", PasswordProtector.Unprotect(blob));
    }

    [Fact]
    public void Saves_and_loads_settings_without_the_plain_password()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = Store();
        Assert.Null(store.Load());
        store.Save(new DatabaseSettings("srv", "PMDA", DatabaseAuthentication.SqlLogin, "pmda", PasswordProtector.Protect("s3cret!"), true, true));

        Assert.DoesNotContain("s3cret", File.ReadAllText(store.Path));
        var cs = new SqlConnectionStringBuilder(store.LoadConnectionString());
        Assert.Equal(("srv", "PMDA", "pmda", "s3cret!"), (cs.DataSource, cs.InitialCatalog, cs.UserID, cs.Password));
    }

    [Fact]
    public void A_corrupt_file_reads_as_not_configured_with_a_reason()
    {
        var store = Store();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.Path, "{ not json");
        Assert.Null(store.LoadConnectionString());
        Assert.Contains("could not be read", store.LoadError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
