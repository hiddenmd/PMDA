using PMDA.Api.Database;
using PMDA.Data;

namespace PMDA.Api.Flights;

/// <summary>
/// Removes the data of deleted flights in the background. A delete request only marks flights
/// (instant, they disappear everywhere); this service then purges their events in batches, so a
/// user never waits for millions of rows and an aborted request cannot roll anything back.
/// </summary>
public sealed class FlightPurger(FlightStores stores, ILogger<FlightPurger> logger) : BackgroundService
{
    public const int BatchSize = 500_000;
    private static readonly TimeSpan Fallback = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _wake = new(0);

    /// <summary>Asks for a purge now (called after flights are marked deleted).</summary>
    public void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Also finishes deletions a previous run of the server did not complete.
        while (!stoppingToken.IsCancellationRequested)
        {
            // Each database on its own: one that is down never holds up the others.
            foreach (var store in stores.Writable)
            {
                try
                {
                    var purged = await store.Flights.PurgeDeletedAsync(BatchSize, stoppingToken);
                    if (purged > 0)
                        logger.LogInformation("Purged {Count} deleted flight(s) in {Database}", purged, store.Name);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Purging deleted flights in {Database} failed; will retry", store.Name);
                }
            }

            try
            {
                await _wake.WaitAsync(Fallback, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
