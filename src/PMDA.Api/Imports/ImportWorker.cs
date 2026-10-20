using Microsoft.Extensions.Options;
using PMDA.Api.Database;

namespace PMDA.Api.Imports;

/// <summary>Drains the import queue, running up to MaxConcurrentImports jobs at once.</summary>
public sealed class ImportWorker(
    ImportQueue queue,
    ImportRunner runner,
    FlightStores stores,
    IOptions<ImportsOptions> options,
    ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var store in stores.Writable)
            await RecoverAsync(store, stoppingToken);

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Value.MaxConcurrentImports,
            CancellationToken = stoppingToken,
        };
        try
        {
            await Parallel.ForEachAsync(queue.ReadAllAsync(stoppingToken), parallel,
                async (job, ct) => await runner.RunAsync(job, ct));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>
    /// Jobs live in memory: flights still Importing in a database were cut off by the previous shutdown. Run at
    /// start for every database, and for a database when it is added.
    /// </summary>
    public async Task RecoverAsync(FlightStore store, CancellationToken ct)
    {
        try
        {
            var interrupted = await store.Flights.FailInterruptedImportsAsync(ct);
            if (interrupted > 0)
                logger.LogWarning("Marked {Count} interrupted import(s) in {Database} as failed", interrupted, store.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not check {Database} for interrupted imports", store.Name);
        }
    }
}
