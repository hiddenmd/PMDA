using PMDA.Data;

namespace PMDA.Api.Flights;

/// <summary>
/// Removes the data of deleted flights in the background. A delete request only marks flights
/// (instant, they disappear everywhere); this service then purges their events in batches, so a
/// user never waits for millions of rows and an aborted request cannot roll anything back.
/// </summary>
public sealed class FlightPurger(FlightRepository flights, DatabaseConnection connection, ILogger<FlightPurger> logger) : BackgroundService
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
        try
        {
            await connection.WhenConfiguredAsync(stoppingToken); // set up from the app, possibly after start-up
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Also finishes deletions a previous run of the server did not complete.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var purged = await flights.PurgeDeletedAsync(BatchSize, stoppingToken);
                if (purged > 0)
                    logger.LogInformation("Purged {Count} deleted flight(s)", purged);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Purging deleted flights failed; will retry");
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
