// Development helper: creates a PMDA flight database (if missing) and imports recordings straight into it,
// without the API. Nothing is ever deleted; a file already in the database is reported as a duplicate.
//
//   dotnet run --project tools/PMDA.Seed -- --server localhost --database PMDA_Training samples/a.bin samples/b.bin
//
// Windows authentication (the current account). Add the database to PMDA afterwards: Manage databases → Find databases.

using System.Diagnostics;
using Microsoft.Data.SqlClient;
using PMDA.Data;
using PMDA.Import;
using PMDA.Parsers;
using PMDA.Parsers.Sample;

if (Parse(args) is not var (server, database, files))
{
    Console.Error.WriteLine("usage: PMDA.Seed --server <name> --database <name> <file.bin>...");
    return 1;
}

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = server,
    InitialCatalog = database,
    IntegratedSecurity = true,
    TrustServerCertificate = true,
    ApplicationName = "PMDA.Seed",
}.ConnectionString;

await SchemaInstaller.EnsureDatabaseAsync(connectionString);
Console.WriteLine($"{server}/{database}: schema ready");

var importer = new FlightImporter(new AdapterDetector([new SampleAdapter()]), new DecoderRegistry().AddSampleDecoders());
var flights = new FlightRepository(connectionString);
var failed = 0;
foreach (var path in files)
{
    var watch = Stopwatch.StartNew();
    await using var file = File.OpenRead(path);
    var result = await importer.ImportAsync(file, Path.GetFileName(path), new SqlEventSink(connectionString), new ImportOptions());
    var flightExists = result.Header is not null && result.Status != ImportStatus.Duplicate;
    if (flightExists && result.Diagnostics.Count > 0)
        await flights.SaveDiagnosticsAsync(result.FlightId, result.Diagnostics, default);
    if (result.Status == ImportStatus.Failed)
        failed++;
    Console.WriteLine($"  {Path.GetFileName(path),-28} {result.Status,-10} {result.Header?.Tail,-10} {result.EventCount,10:N0} events" +
                      $"{(result.ErrorCount > 0 ? $"  {result.ErrorCount} problems" : "")}{(result.FailureReason is { } why ? $"  {why}" : "")}  ({watch.Elapsed.TotalSeconds:N1} s)");
}
return failed > 0 ? 2 : 0;

static (string Server, string Database, string[] Files)? Parse(string[] args)
{
    string? server = null, database = null;
    var files = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--server" when i + 1 < args.Length:
                server = args[++i];
                break;
            case "--database" when i + 1 < args.Length:
                database = args[++i];
                break;
            default:
                files.Add(args[i]);
                break;
        }
    }
    return server is null || database is null || files.Count == 0 ? null : (server, database, files.ToArray());
}
