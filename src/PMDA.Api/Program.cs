using System.IO.Compression;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.ResponseCompression;
using PMDA.Api;
using PMDA.Api.Auth;
using PMDA.Api.Symbology;
using PMDA.Api.Flights;
using PMDA.Api.Imports;
using PMDA.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(DatabaseOptions.ConnectionStringName)
    ?? throw new InvalidOperationException($"Connection string '{DatabaseOptions.ConnectionStringName}' is missing.");
builder.Services.AddSingleton(new SqlConnectionString(connectionString));
builder.Services.AddSingleton(new FlightRepository(connectionString));
builder.Services.AddSingleton(new PlaybackRepository(connectionString));
builder.Services.AddSingleton(new EventRepository(connectionString));
builder.Services.AddSingleton(new SeriesRepository(connectionString));
builder.Services.AddSingleton(new UserRepository(connectionString));
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

if (database.EnsureCreatedOnStart)
    await SchemaInstaller.EnsureDatabaseAsync(connectionString);

app.UseResponseCompression();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// The API description is public in Development (Swagger, client generation); signed-in only otherwise.
var openApi = app.MapOpenApi();
if (app.Environment.IsDevelopment())
    openApi.AllowAnonymous();
if (app.Environment.IsDevelopment())
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "PMDA API v1"));

app.MapAuth();
app.MapUsers();
app.MapImports();
app.MapFlights();
app.MapPlayback();
app.MapDataViews();
app.MapSymbology();

app.Run();

/// <summary>Entry point; public so integration tests can host the app.</summary>
public partial class Program;
