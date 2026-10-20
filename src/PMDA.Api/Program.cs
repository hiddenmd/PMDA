using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.ResponseCompression;
using PMDA.Api;
using PMDA.Api.Auth;
using PMDA.Api.Database;
using PMDA.Api.Symbology;
using PMDA.Api.Flights;
using PMDA.Api.Imports;
using PMDA.Data;

var builder = WebApplication.CreateBuilder(args);

// One swappable connection: set at start-up (settings file, else appsettings) or later from the app.
builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.AddSingleton<DatabaseConnection>();
builder.Services.AddSingleton<IConnectionSource>(sp => sp.GetRequiredService<DatabaseConnection>());
builder.Services.AddSingleton(sp => new FlightRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton(sp => new PlaybackRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton(sp => new EventRepository(sp.GetRequiredService<IConnectionSource>()));
builder.Services.AddSingleton(sp => new SeriesRepository(sp.GetRequiredService<IConnectionSource>()));
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
builder.Services.AddSingleton<DatabaseSetupService>();
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
builder.Services.AddOptions<MapOptions>().Bind(builder.Configuration.GetSection(MapOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddMemoryCache(o => o.SizeLimit = 200);
builder.Services.AddSingleton<EmitterFixService>();
builder.Services.AddSingleton<FlightPurger>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FlightPurger>());

var app = builder.Build();

// Load and validate RWR symbology now: a bad profile file should stop start-up, not a debrief.
app.Services.GetRequiredService<SymbologyCatalog>();

if (app.Services.GetRequiredService<IUserStore>() is FileUserStore userFile)
    await userFile.EnsureMigratedAsync(CancellationToken.None);

await app.Services.GetRequiredService<DatabaseSetupService>()
    .InitializeAsync(builder.Configuration.GetConnectionString(DatabaseOptions.ConnectionStringName), database.EnsureCreatedOnStart);

app.UseResponseCompression();
app.UseExceptionHandler();
app.UseStatusCodePages();
// Without a database only the database settings work; everything else says so instead of failing deep inside.
app.Use(async (http, next) =>
{
    if (http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments("/api/database")
        && !http.RequestServices.GetRequiredService<DatabaseConnection>().IsConfigured)
    {
        await Results.Problem(
            title: "Database not configured",
            detail: "PMDA is not connected to a database yet. Set it up on the server machine.",
            statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(http);
        return;
    }
    await next(http);
});
app.UseAuthentication();
app.UseAuthorization();

// The API description is public in Development (Swagger, client generation); signed-in only otherwise.
var openApi = app.MapOpenApi();
if (app.Environment.IsDevelopment())
    openApi.AllowAnonymous();
if (app.Environment.IsDevelopment())
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "PMDA API v1"));

app.MapAuth();
app.MapDatabase();
app.MapUsers();
app.MapImports();
app.MapFlights();
app.MapPlayback();
app.MapDataViews();
app.MapSymbology();

app.Run();

/// <summary>Entry point; public so integration tests can host the app.</summary>
public partial class Program;
