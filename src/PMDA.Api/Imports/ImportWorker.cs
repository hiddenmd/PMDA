using Microsoft.Extensions.Options;
using PMDA.Data;

namespace PMDA.Api.Imports;

/// <summary>Drains the import queue, running up to MaxConcurrentImports jobs at once.</summary>
public sealed class ImportWorker(
    ImportQueue queue,
    ImportRunner runner,
    FlightRepository flights,
    DatabaseConnection connection,
    IOptions<ImportsOptions> options,
    ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await connection.WhenConfiguredAsync(stoppingToken); // set up from the app, possibly after start-up
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Jobs live in memory: anything still Importing was cut off by the previous shutdown.
        var interrupted = await flights.FailInterruptedImportsAsync(stoppingToken);
        if (interrupted > 0)
            logger.LogWarning("Marked {Count} interrupted import(s) as failed", interrupted);

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
}
