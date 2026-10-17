using Microsoft.Extensions.Options;

namespace PMDA.Api.Imports;

/// <summary>Drains the import queue, running up to MaxConcurrentImports jobs at once.</summary>
public sealed class ImportWorker(ImportQueue queue, ImportRunner runner, IOptions<ImportsOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
