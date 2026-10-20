using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PMDA.Api.Contracts;
using PMDA.Api.Auth;
using PMDA.Api.Imports;
using PMDA.Data;

namespace PMDA.Api.Tests.Support;

/// <summary>The real API, hosted in memory against the test database.</summary>
public sealed class PmdaApiFactory(SqlDatabase db, Dictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string UploadDirectory { get; } = Path.Combine(Path.GetTempPath(), "pmda-test-uploads", Guid.NewGuid().ToString("N"));

    /// <summary>Database settings file for this host: never the machine's real %ProgramData% file.</summary>
    public string SettingsPath { get; } = Path.Combine(Path.GetTempPath(), "pmda-test-settings", Guid.NewGuid().ToString("N"), "database.json");

    /// <summary>This host's own user accounts file (in the same temporary folder).</summary>
    public string UsersPath => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "users.json");

    /// <summary>This host's own header-defined format projects.</summary>
    public string FormatsDirectory => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "formats");

    /// <summary>This host's own list of flight databases (none until a test writes one: then the connection string is "main").</summary>
    public string RegistryPath => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "databases.json");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Pmda", db.ConnectionString);
        builder.UseSetting("Database:SettingsPath", SettingsPath);
        builder.UseSetting("Auth:UsersPath", UsersPath);
        builder.UseSetting("Database:RegistryPath", RegistryPath);
        builder.UseSetting("Auth:MigrateUsersFromSql", "false");
        builder.UseSetting("Database:EnsureCreatedOnStart", "false");
        builder.UseSetting("Imports:TempDirectory", UploadDirectory);
        builder.UseSetting("Formats:Directory", FormatsDirectory);
        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);
    }

    public const string AdminUser = "test-admin";
    public const string AnalystUser = "test-analyst";
    public const string Password = "correct-horse-1";

    private string? _sessionCookie;

    /// <summary>Extra settings for a test host (e.g. Auth options).</summary>
    public Dictionary<string, string> Settings { get; } = new();

    /// <summary>Makes sure the standard test users exist with the known password.</summary>
    public async Task EnsureUsersAsync()
    {
        var users = Service<IUserStore>();
        var auth = Service<AuthService>();
        foreach (var (name, role) in new[] { (AdminUser, UserRole.Admin), (AnalystUser, UserRole.Analyst) })
        {
            var user = await users.FindByNameAsync(name, default);
            if (user is null)
                await users.CreateAsync(auth.NewUser(name, name, Password, role), default);
            else
                await users.SetPasswordAsync(user.UserId, auth.Hash(user, Password), default);
        }
    }

    /// <summary>A client signed in through the real login endpoint (the client keeps the cookie).</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(UserRole role = UserRole.Admin)
    {
        await EnsureUsersAsync();
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(role == UserRole.Admin ? AdminUser : AnalystUser, Password));
        response.EnsureSuccessStatusCode();
        _sessionCookie = response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).First(c => c.StartsWith("pmda.session=", StringComparison.Ordinal));
        return client;
    }

    /// <summary>
    /// Registers these flight databases for the host (call before the first request). Each is (id, name, connection
    /// string, read-only); the first writable one is the import default.
    /// </summary>
    public void UseDatabases(params (string Id, string Name, string ConnectionString, bool ReadOnly)[] databases)
    {
        var entries = databases.Select(d =>
        {
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(d.ConnectionString);
            return new PMDA.Api.Database.DatabaseEntry(d.Id, d.Name, b.DataSource, b.InitialCatalog, PMDA.Api.Database.DatabaseAuthentication.Windows,
                null, null, b.Encrypt != Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional, b.TrustServerCertificate, d.ReadOnly, Enabled: true);
        }).ToList();
        var registry = new PMDA.Api.Database.DatabaseRegistry(entries.FirstOrDefault(e => !e.ReadOnly)?.Id, entries);
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        File.WriteAllText(RegistryPath, JsonSerializer.Serialize(registry, Json));
    }

    public async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName = "flight.bin", string? databaseId = null, string? format = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "file", fileName);
        if (databaseId is not null)
            form.Add(new StringContent(databaseId), "databaseId");
        if (format is not null)
            form.Add(new StringContent(format), "format");
        return await client.PostAsync("/api/imports", form);
    }

    public async Task<ImportJobDto> WaitForFinalAsync(HttpClient client, Guid importId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var job = await client.GetFromJsonAsync<ImportJobDto>($"/api/imports/{importId}", Json);
            if (ImportStates.IsFinal(job!.State))
                return job;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Import {importId} still {job.State}.");
            await Task.Delay(50);
        }
    }

    public HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, ImportHub.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                if (_sessionCookie is not null)
                    o.Headers["Cookie"] = _sessionCookie;
                o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets over the handler
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    public T Service<T>() where T : notnull => Services.GetRequiredService<T>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(UploadDirectory))
            Directory.Delete(UploadDirectory, recursive: true);
        if (disposing && Path.GetDirectoryName(SettingsPath) is { } settings && Directory.Exists(settings))
            Directory.Delete(settings, recursive: true);
    }
}
