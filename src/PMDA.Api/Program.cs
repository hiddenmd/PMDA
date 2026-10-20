using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.ResponseCompression;
using PMDA.Api;
using PMDA.Api.Auth;
using PMDA.Api.Database;
using PMDA.Api.Symbology;
using PMDA.Api.Flights;
using PMDA.Api.Formats;
using PMDA.Api.Imports;
using PMDA.Data;

var builder = WebApplication.CreateBuilder(args);

// Several flight databases at once. A request about one flight works on the database holding it: FlightStoreMiddleware
// finds it, and these per-request repositories point there.
builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(sp => FlightStoreMiddleware.Current(sp));
builder.Services.AddScoped(sp => FlightStoreMiddleware.Current(sp).Flights);
builder.Services.AddScoped(sp => FlightStoreMiddleware.Current(sp).Playback);
builder.Services.AddScoped(sp => FlightStoreMiddleware.Current(sp).Events);
builder.Services.AddScoped(sp => FlightStoreMiddleware.Current(sp).Series);
// User accounts live in a local file; on first start they are copied once from the legacy SQL database.
builder.Services.AddSingleton<IUserStore>(sp =>
{
    var auth = sp.GetRequiredService<IOptions<AuthOptions>>();
    var legacy = auth.Value.MigrateUsersFromSql ? LegacyUsers.Source(sp) : null;
    return new FileUserStore(auth, sp.GetRequiredService<ILogger<FileUserStore>>(), legacy, sp.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<DatabaseSettingsStore>();
builder.Services.AddSingleton<DatabaseRegistryStore>();
builder.Services.AddSingleton<FlightStores>();
builder.Services.AddSingleton<FlightCatalog>();
builder.Services.AddSingleton<DatabasesService>();
builder.Services.AddSingleton<DatabaseDiscovery>();
var database = builder.Configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
// Playback frames are large, repetitive JSON: compression cuts them several-fold.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true; // responses carry no secrets that would make BREACH a concern
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddOpenApi();

builder.Services.AddImports(builder.Configuration);
builder.Services.AddPmdaAuth(builder.Configuration);
builder.Services.AddOptions<SymbologyOptions>().Bind(builder.Configuration.GetSection(SymbologyOptions.Section));
builder.Services.AddSingleton<SymbologyCatalog>();
builder.Services.AddOptions<FormatsOptions>().Bind(builder.Configuration.GetSection(FormatsOptions.Section));
builder.Services.AddSingleton<FormatProjectStore>();
builder.Services.AddOptions<MapOptions>().Bind(builder.Configuration.GetSection(MapOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddMemoryCache(o => o.SizeLimit = 200);
builder.Services.AddScoped<EmitterFixService>();
builder.Services.AddSingleton<FlightPurger>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FlightPurger>());

var app = builder.Build();

// Load and validate RWR symbology now: a bad profile file should stop start-up, not a debrief.
app.Services.GetRequiredService<SymbologyCatalog>();

if (app.Services.GetRequiredService<IUserStore>() is FileUserStore userFile)
    await userFile.EnsureMigratedAsync(CancellationToken.None);

// Development convenience: create or update every writable database's tables on start.
if (database.EnsureCreatedOnStart)
{
    foreach (var store in app.Services.GetRequiredService<FlightStores>().Writable)
        await SchemaInstaller.EnsureDatabaseAsync(store.ConnectionString!);
}

app.UseResponseCompression();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<FlightStoreMiddleware>();

// The API description is public in Development (Swagger, client generation); signed-in only otherwise.
var openApi = app.MapOpenApi();
if (app.Environment.IsDevelopment())
    openApi.AllowAnonymous();
if (app.Environment.IsDevelopment())
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "PMDA API v1"));

app.MapAuth();
app.MapDatabases();
app.MapUsers();
app.MapImports();
app.MapFormats();
app.MapFlights();
app.MapPlayback();
app.MapDataViews();
app.MapSymbology();

app.Run();

/// <summary>Entry point; public so integration tests can host the app.</summary>
public partial class Program;
