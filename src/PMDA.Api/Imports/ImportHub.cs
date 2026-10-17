using Microsoft.AspNetCore.SignalR;
using PMDA.Api.Contracts;

namespace PMDA.Api.Imports;

/// <summary>Methods the server calls on connected clients.</summary>
public interface IImportClient
{
    Task ImportUpdated(ImportJobDto job);

    /// <summary>Flights were deleted; clients refresh their lists.</summary>
    Task FlightsChanged();
}

/// <summary>Push-only hub: clients listen for <see cref="IImportClient.ImportUpdated"/>.</summary>
public sealed class ImportHub : Hub<IImportClient>
{
    public const string Path = "/hubs/imports";
}

/// <summary>Broadcasts every job change to all hub clients.</summary>
public sealed class ImportNotifier(IHubContext<ImportHub, IImportClient> hub, ILogger<ImportNotifier> logger)
{
    public void Watch(ImportJob job) => job.Changed += Publish;

    private void Publish(ImportJob job)
    {
        var dto = ImportJobDto.From(job.Snapshot());
        // Changed is raised synchronously from the import loop; never block it on the network.
        _ = SendAsync(dto);
    }

    private async Task SendAsync(ImportJobDto dto)
    {
        try
        {
            await hub.Clients.All.ImportUpdated(dto);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not publish update for import {ImportId}", dto.Id);
        }
    }
}
