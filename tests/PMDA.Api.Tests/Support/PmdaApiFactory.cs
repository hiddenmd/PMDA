using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PMDA.Api.Contracts;
using PMDA.Api.Imports;

namespace PMDA.Api.Tests.Support;

/// <summary>The real API, hosted in memory against the test database.</summary>
public sealed class PmdaApiFactory(SqlDatabase db) : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string UploadDirectory { get; } = Path.Combine(Path.GetTempPath(), "pmda-test-uploads", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Pmda", db.ConnectionString);
        builder.UseSetting("Database:EnsureCreatedOnStart", "false");
        builder.UseSetting("Imports:TempDirectory", UploadDirectory);
    }

    /// <summary>A client with a signed-in session (cookies kept by the client).</summary>
    public Task<HttpClient> CreateSignedInClientAsync() => Task.FromResult(CreateClient());

    public async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName = "flight.bin")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "file", fileName);
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
    }
}
